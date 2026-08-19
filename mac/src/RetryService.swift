import Foundation
import AppKit

public class RetryService {
    private static var timer: Timer?
    private static var isProcessing = false
    private static let lock = NSLock()

    public static func start() {
        DispatchQueue.main.async {
            timer?.invalidate()
            timer = Timer.scheduledTimer(withTimeInterval: 30.0, repeats: true) { _ in
                checkAndRetry()
            }
        }
    }

    public static func processItem(_ item: AudioQueueItem, completion: ((Bool) -> Void)? = nil) {
        lock.lock()
        if isProcessing {
            lock.unlock()
            completion?(false)
            return
        }
        isProcessing = true
        lock.unlock()

        let fileURL = URL(fileURLWithPath: item.filePath)
        PendingAudioRepository.updateStatus(id: item.id, status: .TRANSCRIBING)

        TranscriptionService.transcribeAudio(fileURL: fileURL) { result in
            switch result {
            case .success(let rawText):
                Logger.info("macOS Whisper Raw Transcript: \"\(rawText)\"")
                PendingAudioRepository.updateStatus(id: item.id, status: .CORRECTING)

                TextCorrectionService.correctText(rawText) { corrResult in
                    let correctedText: String
                    switch corrResult {
                    case .success(let text): correctedText = text
                    case .failure: correctedText = rawText
                    }

                    // Deterministic Local Dictionary & Custom Vocabulary Replacements
                    let finalText = DictionaryService.applyReplacements(correctedText)

                    Logger.info("macOS Final Post-Processed Text: \"\(finalText)\"")
                    TextInsertionService.insertText(finalText, targetCtx: item.targetContext)

                    if SettingsService.current.autoCopyToClipboard {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.setString(finalText, forType: .string)
                    }

                    HistoryRepository.addRecord(rawText: rawText, finalText: finalText, durationSec: item.durationSeconds, targetProcess: item.targetContext?.appName ?? "MacApp")
                    PendingAudioRepository.markCompleted(id: item.id)

                    lock.lock()
                    isProcessing = false
                    lock.unlock()
                    completion?(true)
                }

            case .failure(let error):
                Logger.error("macOS queue item error: \(error.localizedDescription)")
                PendingAudioRepository.updateStatus(id: item.id, status: .ERROR, error: error.localizedDescription)

                lock.lock()
                isProcessing = false
                lock.unlock()
                completion?(false)
            }
        }
    }

    private static func checkAndRetry() {
        guard SettingsService.current.autoRetryOnReconnect else { return }
        let pending = PendingAudioRepository.getPendingItems()
        if let first = pending.first {
            processItem(first)
        }
    }
}
