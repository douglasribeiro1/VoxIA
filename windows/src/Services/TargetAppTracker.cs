using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace VoxIA.Services
{
    public class TargetContext
    {
        public long WindowHandleVal { get; set; }
        public long ControlHandleVal { get; set; }
        public string WindowTitle { get; set; }
        public string ProcessName { get; set; }

        [ScriptIgnore]
        public IntPtr WindowHandle
        {
            get { return new IntPtr(WindowHandleVal); }
            set { WindowHandleVal = value.ToInt64(); }
        }

        [ScriptIgnore]
        public IntPtr ControlHandle
        {
            get { return new IntPtr(ControlHandleVal); }
            set { ControlHandleVal = value.ToInt64(); }
        }

        [ScriptIgnore]
        public AutomationElement AutomationElement { get; set; }

        // Text already written before the cursor (kept in memory only, never persisted)
        [ScriptIgnore]
        public string ContextText { get; set; }
    }

    public static class TargetAppTracker
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndFocusList;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        public static TargetContext CaptureActiveContext()
        {
            var ctx = new TargetContext();
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                ctx.WindowHandle = hWnd;

                if (hWnd != IntPtr.Zero)
                {
                    StringBuilder sbTitle = new StringBuilder(256);
                    GetWindowText(hWnd, sbTitle, 256);
                    ctx.WindowTitle = sbTitle.ToString();

                    uint processId;
                    uint threadId = GetWindowThreadProcessId(hWnd, out processId);

                    try
                    {
                        var proc = System.Diagnostics.Process.GetProcessById((int)processId);
                        ctx.ProcessName = proc.ProcessName;
                    }
                    catch { }

                    GUITHREADINFO guiInfo = new GUITHREADINFO();
                    guiInfo.cbSize = Marshal.SizeOf(guiInfo);
                    if (GetGUIThreadInfo(threadId, ref guiInfo))
                    {
                        ctx.ControlHandle = guiInfo.hwndFocus != IntPtr.Zero ? guiInfo.hwndFocus : hWnd;
                    }
                    else
                    {
                        ctx.ControlHandle = hWnd;
                    }

                    try
                    {
                        ctx.AutomationElement = AutomationElement.FocusedElement;
                    }
                    catch
                    {
                        try
                        {
                            ctx.AutomationElement = AutomationElement.FromHandle(ctx.ControlHandle);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error capturing target active context", ex);
            }

            if (SettingsService.Current.EnableFieldContext && ctx.AutomationElement != null)
            {
                ctx.ContextText = ReadTextBeforeCaret(ctx.AutomationElement);
            }

            // O título da janela não vai para o log: em prontuários ele costuma trazer o nome do paciente.
            Logger.LogInfo(string.Format("Captured Target Context: Process='{0}', HWnd=0x{1:X}, FieldContext={2} chars", ctx.ProcessName, ctx.WindowHandle.ToInt64(), ctx.ContextText != null ? ctx.ContextText.Length : 0));
            return ctx;
        }

        private const int MaxContextChars = 400;

        /// <summary>
        /// Reads (at most) the last 400 characters written before the cursor in the focused field.
        /// Runs with a short timeout because some applications answer UI Automation very slowly.
        /// Returns null when nothing can be read.
        /// </summary>
        private static string ReadTextBeforeCaret(AutomationElement element)
        {
            try
            {
                string result = null;
                var task = System.Threading.Tasks.Task.Run(() => { result = ReadTextBeforeCaretCore(element); });
                if (!task.Wait(600)) return null;
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadTextBeforeCaretCore(AutomationElement element)
        {
            try
            {
                if (element.Current.IsPassword) return null;

                string text = null;
                object patternObj;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out patternObj))
                {
                    var tp = (TextPattern)patternObj;
                    var sel = tp.GetSelection();
                    if (sel != null && sel.Length > 0)
                    {
                        var range = sel[0].Clone();
                        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -MaxContextChars);
                        range.MoveEndpointByRange(TextPatternRangeEndpoint.End, sel[0], TextPatternRangeEndpoint.Start);
                        text = range.GetText(MaxContextChars);
                    }
                }

                if (string.IsNullOrEmpty(text) && element.TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
                {
                    // No caret information: assume the user is dictating at the end of the field
                    var vp = (ValuePattern)patternObj;
                    string value = vp.Current.Value;
                    if (!string.IsNullOrEmpty(value))
                        text = value.Length > MaxContextChars ? value.Substring(value.Length - MaxContextChars) : value;
                }

                if (string.IsNullOrWhiteSpace(text)) return null;

                var sb = new StringBuilder(text.Length);
                bool lastSpace = false;
                foreach (char c in text)
                {
                    if (char.IsControl(c) || char.IsWhiteSpace(c))
                    {
                        if (!lastSpace) sb.Append(' ');
                        lastSpace = true;
                    }
                    else
                    {
                        sb.Append(c);
                        lastSpace = false;
                    }
                }
                string clean = sb.ToString().Trim();
                return clean.Length >= 8 ? clean : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
