using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Automation;

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

            Logger.LogInfo(string.Format("Captured Target Context: Process='{0}', Title='{1}', HWnd=0x{2:X}", ctx.ProcessName, ctx.WindowTitle, ctx.WindowHandle.ToInt64()));
            return ctx;
        }
    }
}
