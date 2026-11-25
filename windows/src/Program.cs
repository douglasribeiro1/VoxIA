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

        /// <summary>Aberto pela atualização com um clique (mostra o aviso "VoxIA atualizado").</summary>
        public static bool JustUpdated { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            JustUpdated = args != null && Array.IndexOf(args, "--after-update") >= 0;
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
            _appMutex = new Mutex(false, appGuid);
            bool acquired;
            try
            {
                // Depois de uma atualização, a versão antiga ainda está fechando: espera por ela
                acquired = _appMutex.WaitOne(JustUpdated ? 20000 : 0, false);
            }
            catch (AbandonedMutexException)
            {
                acquired = true; // a versão anterior fechou sem liberar a trava
            }

            if (!acquired)
            {
                MessageBox.Show("VoxIA já está em execução e residente na área de notificação (bandeja do Windows).", "VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Logger.LogInfo("Starting VoxIA application...");
                // Migra logo na abertura a chave antiga em texto puro para o armazenamento protegido.
                SecureCredentialService.GetApiKey();
                // Carrega a fila (e limpa áudios órfãos) antes de qualquer gravação começar.
                PendingAudioRepository.GetAll();
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
