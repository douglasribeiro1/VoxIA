using System;
using System.IO;
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
                    // O texto continua disponível para Ctrl+V, mas o Windows é instruído a não guardá-lo
                    // no histórico da área de transferência (Win+V) nem sincronizá-lo com a nuvem.
                    var data = new DataObject();
                    data.SetText(text, TextDataFormat.UnicodeText);
                    data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
                    data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
                    Clipboard.SetDataObject(data, true, 5, 100);
                    Logger.LogInfo("Text copied to clipboard successfully (excluded from clipboard history).");
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
