import Foundation
import AVFoundation

/// Grava o microfone em memória como PCM 16 bits mono a 16 kHz (AVAudioEngine + conversor).
/// Guardar as amostras permite tratar o áudio, dividir ditados longos nas pausas e transcrever os
/// blocos enquanto a pessoa ainda fala (LiveTranscriber), como no Windows e no Android.
public final class AudioRecorderService {
    private let engine = AVAudioEngine()
    private var converter: AVAudioConverter?
    private var outFormat: AVAudioFormat?
    private let lock = NSLock()
    private var samples: [Int16] = []
    private var recordingStartTime = Date()

    public private(set) var isRecording = false
    /// Nível do microfone de 0 a 1 (para o indicador na tela).
    public private(set) var currentLevel: Float = 0

    public var recordingDuration: TimeInterval {
        return isRecording ? Date().timeIntervalSince(recordingStartTime) : 0
    }

    public func startRecording() throws {
        if isRecording { _ = stopCapture() }

        let input = engine.inputNode
        let inFormat = input.outputFormat(forBus: 0)
        guard inFormat.sampleRate > 0, inFormat.channelCount > 0 else {
            throw NSError(domain: "VoxIAMac", code: 1, userInfo: [NSLocalizedDescriptionKey: "Microfone indisponível. Permita o acesso em Ajustes do Sistema > Privacidade e Segurança > Microfone."])
        }
        guard let format = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: Double(AudioFormat16k.sampleRate), channels: 1, interleaved: true),
              let conv = AVAudioConverter(from: inFormat, to: format) else {
            throw NSError(domain: "VoxIAMac", code: 2, userInfo: [NSLocalizedDescriptionKey: "Não foi possível preparar a gravação."])
        }
        converter = conv
        outFormat = format
        lock.lock()
        samples = []
        samples.reserveCapacity(AudioFormat16k.sampleRate * 120)
        lock.unlock()

        input.installTap(onBus: 0, bufferSize: 4096, format: inFormat) { [weak self] buffer, _ in
            self?.append(buffer)
        }
        engine.prepare()
        do {
            try engine.start()
        } catch {
            input.removeTap(onBus: 0)
            throw error
        }
        isRecording = true
        recordingStartTime = Date()
        Logger.info("macOS recording started (AVAudioEngine, \(Int(inFormat.sampleRate)) Hz -> 16 kHz mono).")
    }

    private func append(_ buffer: AVAudioPCMBuffer) {
        guard let converter = converter, let format = outFormat else { return }
        let ratio = format.sampleRate / buffer.format.sampleRate
        let capacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio) + 256
        guard let out = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: capacity) else { return }
        var supplied = false
        var error: NSError?
        let status = converter.convert(to: out, error: &error) { _, inputStatus in
            if supplied {
                inputStatus.pointee = .noDataNow
                return nil
            }
            supplied = true
            inputStatus.pointee = .haveData
            return buffer
        }
        guard status != .error, let channel = out.int16ChannelData else { return }
        let n = Int(out.frameLength)
        if n == 0 { return }
        let ptr = channel[0]
        var sumSquares = 0.0
        var clipped = 0
        for i in 0..<n {
            let s = Int(ptr[i])
            sumSquares += Double(s * s)
            if s >= MicrophoneLevelService.clipPeak || s <= -MicrophoneLevelService.clipPeak { clipped += 1 }
        }
        lock.lock()
        samples.append(contentsOf: UnsafeBufferPointer(start: ptr, count: n))
        lock.unlock()

        let rms = (sumSquares / Double(n)).squareRoot()
        currentLevel = Float(min(1, max(0, (rms - 150) / 4500)))
        if clipped >= 3 { MicrophoneLevelService.reportClipping() }
    }

    /// Quantas amostras já foram gravadas.
    public var recordedSamples: Int {
        lock.lock()
        defer { lock.unlock() }
        return isRecording ? samples.count : 0
    }

    /// Cópia das amostras [from, to) da gravação em andamento.
    public func readRecorded(from: Int, to: Int) -> [Int16]? {
        lock.lock()
        defer { lock.unlock() }
        guard isRecording, from >= 0, to > from, to <= samples.count else { return nil }
        return Array(samples[from..<to])
    }

    /// Para o microfone e devolve todo o áudio gravado.
    public func stopCapture() -> [Int16] {
        guard isRecording else { return [] }
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        isRecording = false
        currentLevel = 0
        lock.lock()
        let all = samples
        samples = []
        lock.unlock()
        Logger.info("macOS recording stopped (\(all.count) samples).")
        return all
    }

    public func cancelRecording() {
        _ = stopCapture()
        Logger.info("macOS recording cancelled.")
    }
}
