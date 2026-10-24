using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public enum AudioQueueStatus
    {
        PENDING,
        UPLOADING,
        TRANSCRIBING,
        CORRECTING,
        COMPLETED,
        ERROR
    }

    public class AudioQueueItem
    {
        public string Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public double DurationSeconds { get; set; }
        public string FilePath { get; set; }
        public AudioQueueStatus Status { get; set; }
        public int RetryCount { get; set; }
        public string LastError { get; set; }
        public string RawTranscript { get; set; }
        public string FinalText { get; set; }
        public TargetContext TargetContext { get; set; }
    }

    public static class PendingAudioRepository
    {
        private static List<AudioQueueItem> _items = new List<AudioQueueItem>();
        private static readonly object _lock = new object();

        static PendingAudioRepository()
        {
            LoadQueue();
        }

        public static List<AudioQueueItem> GetAll()
        {
            lock (_lock)
            {
                return _items.ToList();
            }
        }

        public static List<AudioQueueItem> GetPendingItems()
        {
            lock (_lock)
            {
                return _items.Where(i => i.Status == AudioQueueStatus.PENDING || i.Status == AudioQueueStatus.ERROR).ToList();
            }
        }

        public static AudioQueueItem AddRecording(string filePath, double durationSeconds, TargetContext targetContext)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Logger.LogWarning("AddRecording called with missing or null file path.");
                return null;
            }

            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length < 2000 || durationSeconds < 0.2)
                {
                    Logger.LogWarning(string.Format("Discarded too short or empty audio recording ({0} bytes, {1:F2}s): {2}", fi.Length, durationSeconds, filePath));
                    try { File.Delete(filePath); } catch { }
                    AudioChunker.DeleteParts(filePath);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error validating audio recording file", ex);
            }

            var item = new AudioQueueItem
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.Now,
                DurationSeconds = durationSeconds,
                FilePath = filePath,
                Status = AudioQueueStatus.PENDING,
                RetryCount = 0,
                LastError = string.Empty,
                TargetContext = targetContext
            };

            lock (_lock)
            {
                _items.Add(item);
                SaveQueue();
            }

            Logger.LogInfo(string.Format("Added recording to persistent queue. ID: {0}", item.Id));
            return item;
        }

        public static void UpdateStatus(string id, AudioQueueStatus status, string error)
        {
            lock (_lock)
            {
                var item = _items.FirstOrDefault(i => i.Id == id);
                if (item != null)
                {
                    item.Status = status;
                    if (!string.IsNullOrEmpty(error))
                    {
                        item.LastError = error;
                        item.RetryCount++;
                    }
                    SaveQueue();
                }
            }
        }

        public static void UpdateStatus(string id, AudioQueueStatus status)
        {
            UpdateStatus(id, status, null);
        }

        public static void MarkCompleted(string id, string rawText, string finalText)
        {
            lock (_lock)
            {
                var item = _items.FirstOrDefault(i => i.Id == id);
                if (item != null)
                {
                    item.Status = AudioQueueStatus.COMPLETED;
                    item.RawTranscript = rawText;
                    item.FinalText = finalText;
                    SaveQueue();

                    // Only delete local audio file AFTER complete success
                    AudioChunker.DeleteParts(item.FilePath);
                    if (File.Exists(item.FilePath))
                    {
                        try
                        {
                            File.Delete(item.FilePath);
                            Logger.LogInfo(string.Format("Local audio file deleted after completion success: {0}", item.FilePath));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(string.Format("Failed to delete audio file {0}: {1}", item.FilePath, ex.Message));
                        }
                    }

                    // Remove item from active queue list
                    _items.Remove(item);
                    SaveQueue();
                }
            }
        }

        public static void DeleteItem(string id)
        {
            lock (_lock)
            {
                var item = _items.FirstOrDefault(i => i.Id == id);
                if (item != null)
                {
                    if (File.Exists(item.FilePath))
                    {
                        try { File.Delete(item.FilePath); } catch { }
                    }
                    AudioChunker.DeleteParts(item.FilePath);
                    _items.Remove(item);
                    SaveQueue();
                    Logger.LogInfo(string.Format("Queue item manually deleted: {0}", id));
                }
            }
        }

        public static void LoadQueue()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(AppPathService.QueueFilePath))
                    {
                        string json = File.ReadAllText(AppPathService.QueueFilePath);
                        var serializer = new JavaScriptSerializer();
                        _items = serializer.Deserialize<List<AudioQueueItem>>(json) ?? new List<AudioQueueItem>();

                        // Clean up invalid or non-existent files
                        _items.RemoveAll(item =>
                        {
                            if (string.IsNullOrEmpty(item.FilePath) || !File.Exists(item.FilePath)) return true;
                            try { return new FileInfo(item.FilePath).Length < 2000; } catch { return false; }
                        });

                        // Reset interrupted uploading/transcribing states back to PENDING upon boot
                        foreach (var item in _items)
                        {
                            if (item.Status == AudioQueueStatus.UPLOADING ||
                                item.Status == AudioQueueStatus.TRANSCRIBING ||
                                item.Status == AudioQueueStatus.CORRECTING)
                            {
                                item.Status = AudioQueueStatus.PENDING;
                            }
                        }
                        SaveQueue();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error loading pending audio queue", ex);
                    _items = new List<AudioQueueItem>();
                }

                DeleteOrphanAudioFiles();
            }
        }

        /// <summary>
        /// Apaga gravações e blocos que ficaram na pasta audio_queue sem pertencer a nenhum item da fila
        /// (ex.: o app foi fechado no meio do processamento). Assim não sobra áudio de ditado esquecido no disco.
        /// </summary>
        private static void DeleteOrphanAudioFiles()
        {
            try
            {
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in _items)
                {
                    if (string.IsNullOrEmpty(item.FilePath)) continue;
                    keep.Add(Path.GetFileNameWithoutExtension(item.FilePath));
                }

                int removed = 0;
                foreach (string file in Directory.GetFiles(AppPathService.AudioQueueDirectory))
                {
                    // rec_x.m4a e os blocos rec_x.part01.flac pertencem ao item rec_x
                    string name = Path.GetFileName(file);
                    int dot = name.IndexOf('.');
                    string stem = dot > 0 ? name.Substring(0, dot) : name;
                    if (keep.Contains(stem)) continue;
                    // Por segurança, nunca mexe numa gravação recente (pode estar em andamento).
                    if (File.GetLastWriteTime(file) > DateTime.Now.AddHours(-1)) continue;
                    try { File.Delete(file); removed++; } catch { }
                }

                if (removed > 0)
                    Logger.LogInfo(string.Format("Deleted {0} orphan audio file(s) from the queue folder.", removed));
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to clean orphan audio files", ex);
            }
        }

        public static void SaveQueue()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_items);
                File.WriteAllText(AppPathService.QueueFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError("Error saving pending audio queue", ex);
            }
        }
    }
}
