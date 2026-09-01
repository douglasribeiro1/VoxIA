using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public static class HistoryRepository
    {
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
                return _history.OrderByDescending(h => h.Timestamp).ToList();
            }
        }

        public static void AddRecord(string rawText, string finalText, double durationSec, string targetProcess)
        {
            if (string.IsNullOrEmpty(finalText)) return;

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

        private static void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(AppPathService.HistoryFilePath))
                    {
                        string json = File.ReadAllText(AppPathService.HistoryFilePath);
                        var serializer = new JavaScriptSerializer();
                        _history = serializer.Deserialize<List<DictationHistoryItem>>(json) ?? new List<DictationHistoryItem>();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to load dictation history", ex);
                    _history = new List<DictationHistoryItem>();
                }
            }
        }

        private static void Save()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_history);
                File.WriteAllText(AppPathService.HistoryFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to save dictation history", ex);
            }
        }
    }
}
