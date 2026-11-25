import Foundation

/// Transcrição durante a fala: enquanto a pessoa ainda dita, cada bloco de ~15 a 27 s é cortado numa
/// pausa e já vai para o Whisper. Ao parar, só falta o último trecho. Se um bloco falhar (rede) ou
/// demorar demais, a transcrição ao vivo é abandonada e o ditado inteiro segue pelo caminho normal.
public final class LiveTranscriber {
    public struct Partial {
        public let committedSamples: Int
        public let text: String
    }

    private let recorder: AudioRecorderService
    private let lock = NSLock()
    private var task: Task<Void, Never>?
    private var stopRequested = false
    private var failed = false
    private var committed = 0
    private var text = ""

    public init(recorder: AudioRecorderService) {
        self.recorder = recorder
    }

    public func start() {
        task = Task.detached(priority: .utility) { [weak self] in
            await self?.loop()
        }
    }

    private var shouldStop: Bool {
        lock.lock()
        defer { lock.unlock() }
        return stopRequested || failed
    }

    private func loop() async {
        let window = Int(AudioChunker.liveWindowSeconds * Double(AudioFormat16k.sampleRate))
        while !shouldStop {
            try? await Task.sleep(nanoseconds: 1_000_000_000)
            if shouldStop { break }
            let available = recorder.recordedSamples
            lock.lock()
            let start = committed
            let soFar = text
            lock.unlock()
            if available - start < window { continue }
            guard let pcm = recorder.readRecorded(from: start, to: available),
                  let cut = AudioChunker.findCuts(pcm).first else { continue }

            let chunk = Array(pcm[0..<cut])
            let prep = AudioPreprocessor.process(chunk)
            var piece = ""
            // Bloco do meio só é pulado se estiver praticamente mudo: fala baixa também vai
            if !prep.mute {
                do {
                    piece = try await TranscriptionService.transcribeLiveChunk(pcm: prep.pcm, promptContext: soFar)
                } catch {
                    Logger.warning("Live block failed; the whole dictation will be sent when it stops: \(error.localizedDescription)")
                    lock.lock()
                    failed = true
                    lock.unlock()
                    break
                }
            }
            lock.lock()
            text = TranscriptionService.join(text, piece)
            committed = start + cut
            lock.unlock()
            Logger.info("Live block ready (\(cut / AudioFormat16k.sampleRate) s).")
        }
    }

    /// Para de cortar blocos e espera o que estiver em andamento (no máximo 20 s). Devolve o que já
    /// foi transcrito, ou nil quando o ditado deve ir inteiro.
    public func finish() async -> Partial? {
        lock.lock()
        stopRequested = true
        lock.unlock()
        if let running = task {
            let done = await waitWithTimeout(running, seconds: 20)
            if !done {
                Logger.warning("Live block took too long; the whole dictation will be sent.")
                lock.lock()
                failed = true
                lock.unlock()
            }
        }
        task = nil
        lock.lock()
        defer { lock.unlock() }
        if failed || committed == 0 { return nil }
        return Partial(committedSamples: committed, text: text)
    }

    public func cancel() {
        lock.lock()
        stopRequested = true
        failed = true
        lock.unlock()
        task?.cancel()
        task = nil
    }

    private func waitWithTimeout(_ running: Task<Void, Never>, seconds: UInt64) async -> Bool {
        let once = OnceFlag()
        return await withCheckedContinuation { (continuation: CheckedContinuation<Bool, Never>) in
            Task {
                await running.value
                if once.fire() { continuation.resume(returning: true) }
            }
            Task {
                try? await Task.sleep(nanoseconds: seconds * 1_000_000_000)
                if once.fire() { continuation.resume(returning: false) }
            }
        }
    }
}

/// Garante que só o primeiro de dois eventos concorrentes seja usado.
final class OnceFlag {
    private let lock = NSLock()
    private var fired = false

    func fire() -> Bool {
        lock.lock()
        defer { lock.unlock() }
        if fired { return false }
        fired = true
        return true
    }
}
