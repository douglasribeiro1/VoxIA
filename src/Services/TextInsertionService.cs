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
        private const byte VK_C = 0x43;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        public static void RestoreTargetFocus(TargetContext targetContext)
        {
            if (targetContext == null || targetContext.WindowHandle == IntPtr.Zero) return;

            try
            {
                IntPtr currentFg = GetForegroundWindow();
                if (currentFg == targetContext.WindowHandle) return;

                uint currentThreadId = GetCurrentThreadId();
                uint dummy;
                uint fgThreadId = GetWindowThreadProcessId(currentFg, out dummy);
                uint targetThreadId = GetWindowThreadProcessId(targetContext.WindowHandle, out dummy);

                if (targetThreadId != 0 && currentThreadId != targetThreadId)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, true);
                }
                if (targetThreadId != 0 && fgThreadId != 0 && fgThreadId != targetThreadId)
                {
                    AttachThreadInput(fgThreadId, targetThreadId, true);
                }

                SetForegroundWindow(targetContext.WindowHandle);

                if (targetThreadId != 0 && currentThreadId != targetThreadId)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }
                if (targetThreadId != 0 && fgThreadId != 0 && fgThreadId != targetThreadId)
                {
                    AttachThreadInput(fgThreadId, targetThreadId, false);
                }

                Thread.Sleep(30);
            }
            catch { }
        }

        public static void ReleaseAllModifiers()
        {
            try
            {
                byte[] modifierKeys = new byte[]
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

                foreach (byte vk in modifierKeys)
                {
                    keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error in ReleaseAllModifiers", ex);
            }
        }

        public static bool InsertTextIntoContext(string text, TargetContext targetContext)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Strategy 1: Safe Clipboard Paste (Ctrl + V) at current cursor position (Preserves existing text!)
            if (TryClipboardPasteInsertion(text, targetContext))
            {
                return true;
            }

            // Strategy 2: UI Automation Append
            return TryUiAutomationAppend(text, targetContext);
        }

        public static bool TryClipboardPasteInsertion(string text, TargetContext targetContext = null)
        {
            try
            {
                // 1. Ensure target window has focus
                RestoreTargetFocus(targetContext);

                // 2. Put text on clipboard
                ClipboardService.SetText(text);
                Thread.Sleep(40);

                // 3. Clear any stuck modifiers before pasting
                ReleaseAllModifiers();
                Thread.Sleep(20);

                // 4. Simulate Ctrl + V keystroke cleanly
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                Thread.Sleep(30);

                // 5. Final safety release
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

                // 2. Perform clean Ctrl + C keystroke
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_C, 0, 0, UIntPtr.Zero);
                keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                Thread.Sleep(30);

                // 3. Final safety clean
                ReleaseAllModifiers();
            }
            catch (Exception ex)
            {
                Logger.LogError("SimulateCopyKeystroke failed", ex);
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
