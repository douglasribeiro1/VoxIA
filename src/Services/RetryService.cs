using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoxIA.Services
{
    public static class RetryService
    {
        private static System.Threading.Timer _retryTimer;
        private static bool _isProcessing = false;
        private static readonly object _lock = new object();

        public static event Action<string, string> OnProcessingCompleted;
        public static event Action<string> OnProcessingFailed;

        public static void Start()
        {
            if (_retryTimer == null)
            {
                // Check every 30 seconds for pending retry items
                _retryTimer = new System.Threading.Timer(TimerCallback, null, 5000, 30000);
                Logger.LogInfo("Background RetryService started.");
            }
        }

        public static void Stop()
        {
            if (_retryTimer != null)
            {
                _retryTimer.Dispose();
                _retryTimer = null;
            }
        }

        public static async Task ProcessItemAsync(AudioQueueItem item)
        {
            if (item == null) return;

            lock (_lock)
            {
                if (_isProcessing) return;
                _isProcessing = true;
            }

            try
            {
                Logger.LogInfo(string.Format("Processing queue item {0} (Attempt {1})...", item.Id, item.RetryCount + 1));

                // Step 1: Transcribe with Groq Whisper
                PendingAudioRepository.UpdateStatus(item.Id, AudioQueueStatus.TRANSCRIBING);
                string rawText = await TranscriptionService.TranscribeAudioAsync(item.FilePath);

                if (string.IsNullOrWhiteSpace(rawText))
                {
                    throw new InvalidOperationException("Whisper returned empty transcription.");
                }

                Logger.LogInfo(string.Format("Whisper Raw Transcript: \"{0}\"", rawText));

                // Step 2: Correct text with Groq LLM
                PendingAudioRepository.UpdateStatus(item.Id, AudioQueueStatus.CORRECTING);
                string finalText = await TextCorrectionService.CorrectTextAsync(rawText);

                if (string.IsNullOrWhiteSpace(finalText))
                {
                    finalText = rawText;
                }

                // Step 2.5: Deterministic Local Dictionary & Custom Vocabulary Replacements
                finalText = DictionaryService.ApplyReplacements(finalText);

                Logger.LogInfo(string.Format("Final Post-Processed Text: \"{0}\"", finalText));

                // Step 3: Text Insertion & Clipboard
                TextInsertionService.InsertTextIntoContext(finalText, item.TargetContext);

                if (SettingsService.Current.AutoCopyToClipboard)
                {
                    ClipboardService.SetText(finalText);
                }

                // Step 4: Record history & Mark completed (which deletes local audio file)
                string procName = (item.TargetContext != null && item.TargetContext.ProcessName != null) ? item.TargetContext.ProcessName : "Unknown";
                HistoryRepository.AddRecord(rawText, finalText, item.DurationSeconds, procName);
                PendingAudioRepository.MarkCompleted(item.Id, rawText, finalText);

                AudioFeedbackService.PlaySuccessBeep();
                if (OnProcessingCompleted != null)
                    OnProcessingCompleted(rawText, finalText);

                Logger.LogInfo(string.Format("Queue item {0} fully completed and audio purged.", item.Id));
            }
            catch (Exception ex)
            {
                Logger.LogError(string.Format("Error processing queue item {0}: {1}", item.Id, ex.Message), ex);

                // If error is permanently unrecoverable (e.g. audio too short or corrupted format), delete item instead of looping forever
                if (ex.Message != null && (ex.Message.IndexOf("too short", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("HTTP 400", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    PendingAudioRepository.DeleteItem(item.Id);
                    Logger.LogWarning(string.Format("Queue item {0} permanently deleted due to unrecoverable client error.", item.Id));
                }
                else
                {
                    PendingAudioRepository.UpdateStatus(item.Id, AudioQueueStatus.ERROR, ex.Message);
                }

                if (OnProcessingFailed != null)
                    OnProcessingFailed(ex.Message);
            }
            finally
            {
                lock (_lock)
                {
                    _isProcessing = false;
                }
            }
        }

        private static async void TimerCallback(object state)
        {
            if (!SettingsService.Current.AutoRetryOnReconnect) return;

            var pending = PendingAudioRepository.GetPendingItems();
            if (pending != null && pending.Count > 0)
            {
                Logger.LogInfo(string.Format("RetryService found {0} pending items. Triggering processing...", pending.Count));
                foreach (var item in pending)
                {
                    await ProcessItemAsync(item);
                }
            }
        }
    }
}
