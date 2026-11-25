using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VoxIA.Services
{
    /// <summary>
    /// Busca de atualizações com um clique. O GitHub Actions publica, junto com o VoxIA.exe, um
    /// version-windows.json com o número do build e o SHA-256 do .exe. Se o número for maior que o
    /// deste VoxIA (BuildInfo.Build), a atualização baixa o .exe novo, confere o SHA-256, troca o
    /// arquivo (o Windows deixa renomear um .exe em uso) e reabre o VoxIA. Se a pasta não deixar
    /// gravar, abre o link de download no navegador.
    /// </summary>
    public static class UpdateService
    {
        private const string VersionUrl = "https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/version-windows.json";
        public const string ZipUrl = "https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/VoxIA-Windows.zip";

        public class UpdateInfo
        {
            public int Build;
            public string Url;
            public string Sha256;
        }

        /// <summary>Se este VoxIA veio do GitHub Actions (compilado à mão não se atualiza sozinho).</summary>
        public static bool IsSupported
        {
            get { return BuildInfo.Build > 0; }
        }

        /// <summary>Versão nova disponível, ou null (já está atualizado, sem internet, ou build manual).</summary>
        public static async Task<UpdateInfo> CheckAsync()
        {
            if (!IsSupported) return null;
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(20);
                    string json = await http.GetStringAsync(VersionUrl);
                    var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    var info = new UpdateInfo
                    {
                        Build = Convert.ToInt32(dict["build"]),
                        Url = Convert.ToString(dict["url"]),
                        Sha256 = Convert.ToString(dict["sha256"]).Trim().ToLowerInvariant()
                    };
                    SettingsService.Current.LastUpdateCheckTicks = DateTime.UtcNow.Ticks;
                    SettingsService.Save();
                    Logger.LogInfo(string.Format("Update check: this build {0}, published build {1}.", BuildInfo.Build, info.Build));
                    if (info.Build <= BuildInfo.Build || string.IsNullOrEmpty(info.Url) || info.Sha256.Length != 64) return null;
                    return info;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Update check failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>A busca automática roda no máximo uma vez a cada 20 horas.</summary>
        public static bool IsAutoCheckDue()
        {
            if (!IsSupported || !SettingsService.Current.AutoCheckUpdates) return false;
            long last = SettingsService.Current.LastUpdateCheckTicks;
            return last <= 0 || (DateTime.UtcNow - new DateTime(last, DateTimeKind.Utc)).TotalHours >= 20;
        }

        /// <summary>
        /// Baixa e instala. Devolve true quando o VoxIA.exe já foi trocado e o app deve reabrir
        /// (quem chamou fecha o VoxIA; a versão nova é aberta aqui e espera o fechamento).
        /// </summary>
        public static async Task<bool> ApplyAsync(UpdateInfo info)
        {
            string exePath = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exePath) ?? "";
            string newPath = Path.Combine(dir, "VoxIA.update.exe");
            string oldPath = Path.Combine(dir, "VoxIA.old.exe");
            try
            {
                byte[] data;
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(120);
                    data = await http.GetByteArrayAsync(info.Url);
                }
                string sha = Sha256Hex(data);
                if (sha != info.Sha256)
                {
                    Logger.LogError(string.Format("Update download failed the SHA-256 check ({0} vs {1}); not installed.", sha, info.Sha256));
                    MessageBox.Show("O arquivo baixado não passou na conferência de segurança e não foi instalado. Tente de novo mais tarde.", "VoxIA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                File.WriteAllBytes(newPath, data);
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(exePath, oldPath);
                try
                {
                    File.Move(newPath, exePath);
                }
                catch
                {
                    File.Move(oldPath, exePath); // desfaz: o VoxIA atual continua funcionando
                    throw;
                }

                string shaFile = Path.Combine(dir, "VoxIA.exe.sha256.txt");
                try { if (File.Exists(shaFile)) File.WriteAllText(shaFile, sha.ToUpperInvariant() + "  VoxIA.exe"); } catch { }

                Process.Start(new ProcessStartInfo(exePath, "--after-update") { UseShellExecute = false, WorkingDirectory = dir });
                Logger.LogInfo(string.Format("Updated to build {0}; restarting.", info.Build));
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.LogWarning("Update could not write to the VoxIA folder: " + ex.Message);
                OfferManualDownload("O Windows não deixou o VoxIA trocar o próprio arquivo nesta pasta.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError("Update failed", ex);
                OfferManualDownload("Não foi possível baixar ou instalar a atualização (" + ex.Message + ").");
                return false;
            }
            finally
            {
                try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
            }
        }

        /// <summary>Na abertura: apaga o .exe antigo que ficou da última atualização.</summary>
        public static void CleanupOldExe()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                string oldPath = Path.Combine(dir, "VoxIA.old.exe");
                if (File.Exists(oldPath)) File.Delete(oldPath);
            }
            catch { } // o processo antigo pode ainda estar fechando; fica para a próxima abertura
        }

        private static void OfferManualDownload(string reason)
        {
            var answer = MessageBox.Show(reason + "\n\nQuer abrir o link para baixar a versão nova manualmente?", "VoxIA", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
            {
                try { Process.Start(ZipUrl); } catch { }
            }
        }

        private static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
