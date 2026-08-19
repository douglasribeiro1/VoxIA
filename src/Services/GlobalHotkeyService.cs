using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoxIA.Services
{
    public static class GlobalHotkeyService
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint WM_QUIT = 0x0012;

        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_CONTROL = 0x11;

        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_MENU = 0x12;

        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_SHIFT = 0x10;

        private const int VK_ESCAPE = 0x1B;
        private const int VK_W = 0x57;
        private const int VK_D = 0x44;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static Thread _hookThread;
        private static uint _hookThreadId;
        private static volatile bool _running = false;
        private static SynchronizationContext _syncContext;

        public static event Action OnHotkeyPressed;
        public static event Action OnHotkeyReleased;
        public static event Action OnCancelPressed;
        public static event Action OnDictionaryHotkeyPressed;

        private static bool _comboActive = false;
        private static DateTime _lastTriggerTime = DateTime.MinValue;
        private static DateTime _lastDictTriggerTime = DateTime.MinValue;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref MSG lpmsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsKeyPhysicallyDown(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }

        private static bool IsCtrlPhysicallyDown()
        {
            return IsKeyPhysicallyDown(VK_LCONTROL) || IsKeyPhysicallyDown(VK_RCONTROL) || IsKeyPhysicallyDown(VK_CONTROL);
        }

        private static bool IsWinPhysicallyDown()
        {
            return IsKeyPhysicallyDown(VK_LWIN) || IsKeyPhysicallyDown(VK_RWIN);
        }

        private static bool IsAltPhysicallyDown()
        {
            return IsKeyPhysicallyDown(VK_LMENU) || IsKeyPhysicallyDown(VK_RMENU) || IsKeyPhysicallyDown(VK_MENU);
        }

        private static bool IsShiftPhysicallyDown()
        {
            return IsKeyPhysicallyDown(VK_LSHIFT) || IsKeyPhysicallyDown(VK_RSHIFT) || IsKeyPhysicallyDown(VK_SHIFT);
        }

        public static void Start()
        {
            _syncContext = SynchronizationContext.Current;

            if (_hookThread != null && _hookThread.IsAlive)
            {
                return;
            }

            _running = true;
            _hookThread = new Thread(HookThreadLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "VoxIA_Dedicated_KeyboardHook_Thread"
            };
            _hookThread.Start();
            Logger.LogInfo("Dedicated High-Priority Keyboard Hook thread started.");
        }

        private static void HookThreadLoop()
        {
            _hookThreadId = GetCurrentThreadId();

            try
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                }

                if (_hookID != IntPtr.Zero)
                {
                    Logger.LogInfo(string.Format("Global Low-Level Hook attached to isolated high-priority message loop (Thread ID: {0})", _hookThreadId));
                }
                else
                {
                    Logger.LogError("Failed to attach Global Low-Level Hook on dedicated thread.", null);
                }

                // Dedicated isolated message loop for keyboard hook (Immune to UI thread pauses or GC!)
                MSG msg;
                while (_running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error in dedicated HookThreadLoop", ex);
            }
            finally
            {
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }
            }
        }

        public static void Stop()
        {
            _running = false;
            if (_hookThreadId != 0)
            {
                PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            if (_hookThread != null && _hookThread.IsAlive)
            {
                _hookThread.Join(500);
            }
            _hookThread = null;
            Logger.LogInfo("Global Low-Level Keyboard Hook thread stopped.");
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isKeyDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                bool isKeyUp = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

                KBDLLHOOKSTRUCT hookStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                int vkCode = (int)hookStruct.vkCode;

                // Check for ESC key to cancel active recording
                if (isKeyDown && vkCode == VK_ESCAPE)
                {
                    DispatchToUI(() =>
                    {
                        Action handlerCancel = OnCancelPressed;
                        if (handlerCancel != null) handlerCancel();
                    });
                }

                // Check for Dictionary Quick Add Hotkey: Ctrl + Shift + W (W = Word) or Ctrl + Shift + D
                if (isKeyDown && (vkCode == VK_W || vkCode == VK_D) && IsCtrlPhysicallyDown() && IsShiftPhysicallyDown())
                {
                    double msSinceLast = (DateTime.Now - _lastDictTriggerTime).TotalMilliseconds;
                    if (msSinceLast > 400)
                    {
                        _lastDictTriggerTime = DateTime.Now;
                        DispatchToUI(() =>
                        {
                            Action handlerDict = OnDictionaryHotkeyPressed;
                            if (handlerDict != null) handlerDict();
                        });
                    }
                    return (IntPtr)1; // Suppress key typing so editor isn't affected
                }

                // Hotkey Match Logic based on real-time hardware key state
                var s = SettingsService.Current;
                bool matchNow = false;

                if (s.HotkeyVirtualKey == 0) // Default: Ctrl + Win combination
                {
                    matchNow = IsCtrlPhysicallyDown() && IsWinPhysicallyDown();
                }
                else
                {
                    bool ctrlOk = !s.HotkeyUseCtrl || IsCtrlPhysicallyDown();
                    bool winOk = !s.HotkeyUseWin || IsWinPhysicallyDown();
                    bool altOk = !s.HotkeyUseAlt || IsAltPhysicallyDown();
                    bool shiftOk = !s.HotkeyUseShift || IsShiftPhysicallyDown();
                    bool keyOk = IsKeyPhysicallyDown(s.HotkeyVirtualKey);

                    matchNow = ctrlOk && winOk && altOk && shiftOk && keyOk;
                }

                if (matchNow)
                {
                    // EDGE DETECTION: Trigger OnHotkeyPressed ONLY on the initial transition into combo active
                    if (!_comboActive)
                    {
                        _comboActive = true;
                        double msSinceLast = (DateTime.Now - _lastTriggerTime).TotalMilliseconds;
                        if (msSinceLast > 350)
                        {
                            _lastTriggerTime = DateTime.Now;
                            DispatchToUI(() =>
                            {
                                Action handlerPress = OnHotkeyPressed;
                                if (handlerPress != null) handlerPress();
                            });
                        }
                    }

                    // Suppress Win key from launching Windows Start menu when combined with Ctrl
                    if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                    {
                        return (IntPtr)1;
                    }
                }
                else
                {
                    // If combo was previously active and now any key is released
                    if (_comboActive && isKeyUp)
                    {
                        _comboActive = false;
                        DispatchToUI(() =>
                        {
                            Action handlerRelease = OnHotkeyReleased;
                            if (handlerRelease != null) handlerRelease();
                        });
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static void DispatchToUI(Action action)
        {
            if (_syncContext != null)
            {
                _syncContext.Post(_ => action(), null);
            }
            else
            {
                ThreadPool.QueueUserWorkItem(_ => action());
            }
        }
    }
}
