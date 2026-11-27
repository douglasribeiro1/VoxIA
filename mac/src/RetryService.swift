import Foundation
import AppKit

/// Processa a fila de áudios: Whisper (com o texto já transcrito durante a fala como prefixo) →
/// correção pela IA → dicionário → texto inserido no app onde o ditado começou. Em falha de rede o
/// áudio fica guardado e é reenviado a cada 30 s.
public enum RetryService {
    private static var timer: Timer?
    private static var isProcessing = false
    private static let lock = NSLock()
    /// Chamado (na thread principal) quando o texto foi colado sem a correção da IA; recebe o motivo.
    public static var onCorrectionWarning: ((String) -> Void)?

    public static func start() {
        DispatchQueue.main.async {
            timer?.invalidate()
            timer = Timer.scheduledTimer(withTimeInterval: 30.0, repeats: true) { _ in checkAndRetry() }
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

        Task.detached(priority: .userInitiated) {
            let ok = await run(item)
            lock.lock()
            isProcessing = false
            lock.unlock()
            DispatchQueue.main.async { completion?(ok) }
        }
    }

    private static func run(_ item: AudioQueueItem) async -> Bool {
        PendingAudioRepository.updateStatus(id: item.id, status: .TRANSCRIBING)
        let prefix = item.prefixText ?? ""
        do {
            var tail = ""
            if item.audioIsSilent != true {
                if let pcm = WavFile.read(URL(fileURLWithPath: item.filePath)) {
                    tail = try await TranscriptionService.transcribe(pcm: pcm, prefix: prefix, speechFrames: item.speechFrames ?? -1)
                } else if prefix.isEmpty {
                    Logger.error("Queued audio file is missing; item removed.")
                    PendingAudioRepository.deleteItem(id: item.id)
                    return false
                }
            }
            return await finish(item, raw: TranscriptionService.join(prefix, tail))
        } catch let error as TranscriptionService.TranscriptionError where error.permanent {
            Logger.error("Unrecoverable transcription error; item removed: \(error.message)")
            PendingAudioRepository.deleteItem(id: item.id)
            return false
        } catch {
            Logger.error("macOS queue item error: \(error.localizedDescription)")
            PendingAudioRepository.updateStatus(id: item.id, status: .ERROR, error: error.localizedDescription)
            return false
        }
    }

    private static func finish(_ item: AudioQueueItem, raw: String) async -> Bool {
        if raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            PendingAudioRepository.markCompleted(id: item.id)
            return true
        }
        PendingAudioRepository.updateStatus(id: item.id, status: .CORRECTING)
        let corrected = await TextCorrectionService.correct(raw) ?? raw
        let correctionIssue = TextCorrectionService.lastIssue
        let finalText = DictionaryService.applyReplacements(corrected.isEmpty ? raw : corrected)

        await MainActor.run {
            TargetAppTracker.reactivate(item.targetContext)
        }
        try? await Task.sleep(nanoseconds: 150_000_000)
        await MainActor.run {
            // Cola no cursor; o texto também fica na área de transferência
            TextInsertionService.insertText(finalText + " ", targetCtx: item.targetContext)
        }
        HistoryRepository.addRecord(rawText: raw, finalText: finalText, durationSec: item.durationSeconds,
                                    targetProcess: item.targetContext?.appName ?? "MacApp")
        PendingAudioRepository.markCompleted(id: item.id)
        if let issue = correctionIssue {
            await MainActor.run { onCorrectionWarning?(issue) }
        }
        return true
    }

    private static func checkAndRetry() {
        guard SettingsService.current.autoRetryOnReconnect else { return }
        if let first = PendingAudioRepository.getPendingItems().first {
            processItem(first)
        }
    }
}
