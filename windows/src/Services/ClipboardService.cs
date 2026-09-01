using System;
using System.Threading;
using System.Windows.Forms;

namespace VoxIA.Services
{
    public static class ClipboardService
    {
        public static void SetText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            ExecuteInSta(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                    Logger.LogInfo("Text copied to clipboard successfully.");
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to copy text to clipboard", ex);
                }
            });
        }

        public static string GetText()
        {
            string result = null;
            ExecuteInSta(() =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        result = Clipboard.GetText();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to get clipboard text", ex);
                }
            });
            return result;
        }

        private static void ExecuteInSta(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                action();
            }
            else
            {
                Thread thread = new Thread(() => action());
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
        }
    }
}
