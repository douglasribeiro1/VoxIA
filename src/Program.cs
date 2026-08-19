using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using VoxIA.Services;

namespace VoxIA
{
    internal static class Program
    {
        private static Mutex _appMutex;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Enable TLS 1.2 for all Groq HTTPS requests
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072; // TLS 1.2 + TLS 1.3
            }
            catch
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                }
                catch { }
            }

            const string appGuid = "VoxIA_Single_Instance_Mutex_2026_91896598";
            bool createdNew;

            _appMutex = new Mutex(true, appGuid, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("VoxIA já está em execução e residente na área de notificação (bandeja do Windows).", "VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Logger.LogInfo("Starting VoxIA application...");
                Application.Run(new VoxIAContext());
            }
            catch (Exception ex)
            {
                Logger.LogError("Unhandled exception in main loop", ex);
                MessageBox.Show(string.Format("Ocorreu um erro não tratado no VoxIA: {0}", ex.Message), "Erro Crítico", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _appMutex.ReleaseMutex();
            }
        }
    }
}
