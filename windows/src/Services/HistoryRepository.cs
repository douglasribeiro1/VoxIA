using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public class DictationHistoryItem
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public double DurationSeconds { get; set; }
        public string RawTranscript { get; set; }
        public string FinalText { get; set; }
        public string TargetProcess { get; set; }
        public int WordCount { get; set; }
    }

    /// <summary>
    /// Histórico de ditados, gravado protegido pelo Windows (DPAPI) e apagado depois do prazo escolhido
    /// nas configurações (HistoryRetentionDays).
    /// </summary>
    public static class HistoryRepository
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VoxIA_History_Entropy_2026");
        private static List<DictationHistoryItem> _history = new List<DictationHistoryItem>();
        private static readonly object _lock = new object();

        static HistoryRepository()
        {
            Load();
        }

        public static List<DictationHistoryItem> GetAll()
        {
            lock (_lock)
            {
                ApplyRetention();
                return _history.OrderByDescending(h => h.Timestamp).ToList();
            }
        }

        public static void AddRecord(string rawText, string finalText, double durationSec, string targetProcess)
        {
            if (string.IsNullOrEmpty(finalText)) return;
            if (SettingsService.Current.HistoryRetentionDays == 0) return;

            int wordCount = finalText.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

            var item = new DictationHistoryItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.Now,
                DurationSeconds = Math.Round(durationSec, 1),
                RawTranscript = rawText,
                FinalText = finalText,
                TargetProcess = targetProcess,
                WordCount = wordCount
            };

            lock (_lock)
            {
                _history.Add(item);
                // Keep history capped at 1000 items to avoid bloating
                if (_history.Count > 1000)
                {
                    _history = _history.OrderByDescending(h => h.Timestamp).Take(1000).ToList();
                }
                ApplyRetention();
                Save();
            }
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                _history.Clear();
                Save();
            }
        }

        /// <summary>Apaga os ditados mais antigos que o prazo configurado (ou todos, se o histórico estiver desligado).</summary>
        public static void ApplyRetention()
        {
            lock (_lock)
            {
                int days = SettingsService.Current.HistoryRetentionDays;
                if (days < 0) return;

                DateTime cutoff = days == 0 ? DateTime.MaxValue : DateTime.Now.AddDays(-days);
                int removed = _history.RemoveAll(h => h.Timestamp < cutoff);
                if (removed > 0)
                {
                    Logger.LogInfo(string.Format("History retention removed {0} old dictation(s).", removed));
                    Save();
                }
            }
        }

        private static void Load()
        {
            lock (_lock)
            {
                try
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    if (File.Exists(AppPathService.ProtectedHistoryFilePath))
                    {
                        byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(AppPathService.ProtectedHistoryFilePath), Entropy, DataProtectionScope.CurrentUser);
                        _history = serializer.Deserialize<List<DictationHistoryItem>>(Encoding.UTF8.GetString(raw)) ?? new List<DictationHistoryItem>();
                    }
                    else if (File.Exists(AppPathService.HistoryFilePath))
                    {
                        // Migração: o history.json antigo, em texto puro, vira history.dat protegido.
                        string json = File.ReadAllText(AppPathService.HistoryFilePath);
                        _history = serializer.Deserialize<List<DictationHistoryItem>>(json) ?? new List<DictationHistoryItem>();
                        Save();
                        if (File.Exists(AppPathService.ProtectedHistoryFilePath))
                        {
                            File.Delete(AppPathService.HistoryFilePath);
                            Logger.LogInfo("Dictation history migrated to protected storage.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Ex.: pasta copiada de outro computador; o histórico de lá não pode ser lido aqui.
                    Logger.LogError("Failed to load dictation history", ex);
                    _history = new List<DictationHistoryItem>();
                }

                ApplyRetention();
            }
        }

        private static void Save()
        {
            try
            {
                if (_history.Count == 0)
                {
                    if (File.Exists(AppPathService.ProtectedHistoryFilePath)) File.Delete(AppPathService.ProtectedHistoryFilePath);
                    if (File.Exists(AppPathService.HistoryFilePath)) File.Delete(AppPathService.HistoryFilePath);
                    return;
                }

                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                byte[] plain = Encoding.UTF8.GetBytes(serializer.Serialize(_history));
                File.WriteAllBytes(AppPathService.ProtectedHistoryFilePath, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to save dictation history", ex);
            }
        }
    }
}
