using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace VoxIA.Services
{
    public static class TextInsertionService
    {
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint INPUT_KEYBOARD = 1;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void ReleaseAllModifiers()
        {
            try
            {
                ushort[] modifierKeys = new ushort[]
                {
                    0x11, // VK_CONTROL
                    0xA2, // VK_LCONTROL
                    0xA3, // VK_RCONTROL
                    0x5B, // VK_LWIN
                    0x5C, // VK_RWIN
                    0x12, // VK_MENU (ALT)
                    0xA4, // VK_LMENU
                    0xA5, // VK_RMENU
                    0x10, // VK_SHIFT
                    0xA0, // VK_LSHIFT
                    0xA1  // VK_RSHIFT
                };

                INPUT[] inputs = new INPUT[modifierKeys.Length];
                for (int i = 0; i < modifierKeys.Length; i++)
                {
                    inputs[i] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT
                        {
                            wVk = modifierKeys[i],
                            wScan = 0,
                            dwFlags = KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    };
                }

                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
            catch (Exception ex)
            {
                Logger.LogError("Error in ReleaseAllModifiers", ex);
            }
        }

        public static bool InsertTextIntoContext(string text, TargetContext targetContext)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Ensure target window is in foreground
            if (targetContext != null && targetContext.WindowHandle != IntPtr.Zero)
            {
                try
                {
                    IntPtr currentFg = GetForegroundWindow();
                    if (currentFg != targetContext.WindowHandle)
                    {
                        SetForegroundWindow(targetContext.WindowHandle);
                        Thread.Sleep(50);
                    }
                }
                catch { }
            }

            // Always release any stuck modifiers before attempting any text insertion
            ReleaseAllModifiers();
            Thread.Sleep(20);

            // Strategy 1: Safe Clipboard Paste (Ctrl + V) at current cursor position (Preserves existing text!)
            if (TryClipboardPasteInsertion(text))
            {
                return true;
            }

            // Strategy 2: SendInput Unicode at current cursor position
            if (TrySendInputUnicode(text))
            {
                return true;
            }

            // Strategy 3: UI Automation Append
            return TryUiAutomationAppend(text, targetContext);
        }

        public static bool TryClipboardPasteInsertion(string text)
        {
            try
            {
                ClipboardService.SetText(text);
                Thread.Sleep(50);

                // 1. Explicitly clear all physical/virtual modifier keys
                ReleaseAllModifiers();
                Thread.Sleep(20);

                // 2. Perform atomic pure Ctrl + V keystroke using SendInput
                INPUT[] pasteInputs = new INPUT[4];

                // Ctrl DOWN
                pasteInputs[0] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = VK_CONTROL, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                };
                // V DOWN
                pasteInputs[1] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = VK_V, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                };
                // V UP
                pasteInputs[2] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = VK_V, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                };
                // Ctrl UP
                pasteInputs[3] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = VK_CONTROL, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                };

                SendInput(4, pasteInputs, Marshal.SizeOf(typeof(INPUT)));
                Thread.Sleep(30);

                // 3. Final safety clean
                ReleaseAllModifiers();

                Logger.LogInfo("Text insertion succeeded via Strategy 1 (Clipboard Paste at cursor position).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Clipboard Paste insertion failed", ex);
                return false;
            }
        }

        public static void SimulateCopyKeystroke()
        {
            try
            {
                // 1. Explicitly clear all modifier keys
                ReleaseAllModifiers();
                Thread.Sleep(20);

                // 2. Perform atomic pure Ctrl + C keystroke using SendInput
                INPUT[] copyInputs = new INPUT[4];

                // Ctrl DOWN
                copyInputs[0] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = VK_CONTROL, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                };
                // C DOWN (0x43)
                copyInputs[1] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = 0x43, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                };
                // C UP
                copyInputs[2] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = 0x43, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                };
                // Ctrl UP
                copyInputs[3] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = VK_CONTROL, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                };

                SendInput(4, copyInputs, Marshal.SizeOf(typeof(INPUT)));
                Thread.Sleep(30);

                // 3. Final safety clean
                ReleaseAllModifiers();
            }
            catch (Exception ex)
            {
                Logger.LogError("SimulateCopyKeystroke failed", ex);
            }
        }

        public static bool TrySendInputUnicode(string text)
        {
            try
            {
                foreach (char ch in text)
                {
                    INPUT[] inputs = new INPUT[2];

                    inputs[0] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = ch,
                            dwFlags = 0x0004, // KEYEVENTF_UNICODE
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    };

                    inputs[1] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = ch,
                            dwFlags = 0x0004 | KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    };

                    SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                    Thread.Sleep(2);
                }

                Logger.LogInfo("Text insertion succeeded via Strategy 2 (SendInput Unicode at cursor).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("SendInput Unicode insertion failed", ex);
                return false;
            }
        }

        private static bool TryUiAutomationAppend(string text, TargetContext targetContext)
        {
            try
            {
                if (targetContext == null || targetContext.WindowHandle == IntPtr.Zero) return false;

                AutomationElement element = targetContext.AutomationElement;
                if (element == null)
                {
                    try { element = AutomationElement.FocusedElement; } catch { }
                }
                if (element == null) return false;

                object patternObj;
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
                {
                    ValuePattern valuePattern = (ValuePattern)patternObj;
                    if (!valuePattern.Current.IsReadOnly)
                    {
                        string currentVal = valuePattern.Current.Value ?? "";
                        // Append text at current end instead of overwriting
                        string newVal = string.IsNullOrEmpty(currentVal) ? text : currentVal + " " + text;
                        valuePattern.SetValue(newVal);
                        Logger.LogInfo("Text insertion succeeded via Strategy 3 (UI Automation Append).");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("UI Automation Append failed", ex);
            }

            return false;
        }
    }
}
