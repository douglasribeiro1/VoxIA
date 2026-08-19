using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace VoxIA.Services
{
    public static class TextInsertionService
    {
        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint INPUT_KEYBOARD = 1;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
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
                Thread.Sleep(60);

                // Simulate Ctrl + V keypress
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                Logger.LogInfo("Text insertion succeeded via Strategy 1 (Clipboard Paste at cursor position).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Clipboard Paste insertion failed", ex);
                return false;
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
