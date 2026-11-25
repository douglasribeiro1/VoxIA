import Foundation
import CoreAudio

/// Durante o ditado, sobe o volume de entrada do microfone do Mac para 100% e devolve o nível original
/// ao parar. Se o som saturar (microfone perto demais), o nível desce sozinho, aos poucos, nunca abaixo
/// do nível original. Se o VoxIA fechar no meio, o nível original fica salvo e volta na próxima abertura.
/// Alguns microfones não deixam o sistema mudar o volume; nesse caso nada muda.
public enum MicrophoneLevelService {
    public static let clipPeak = 32000
    private static let stepDown: Float32 = 0.08

    private static var device: AudioDeviceID = 0
    private static var originalLevel: Float32 = -1
    private static var currentLevel: Float32 = -1
    private static var clipEvents = 0
    private static let lock = NSLock()
    private static var timer: Timer?

    private static var restoreFile: URL {
        return AppPathService.dataDirectory.appendingPathComponent("mic_level_restore.txt")
    }

    /// Chamado pela gravação (qualquer thread) quando um trecho satura.
    public static func reportClipping() {
        lock.lock()
        clipEvents += 1
        lock.unlock()
    }

    /// Chamar na thread principal, antes de gravar.
    public static func boost() {
        restore()
        guard SettingsService.current.boostMicrophoneDuringDictation,
              let dev = defaultInputDevice(), let level = volume(dev) else { return }
        if level >= 0.99 { return }
        try? String(level).write(to: restoreFile, atomically: true, encoding: .utf8)
        guard setVolume(dev, 1.0) else {
            try? FileManager.default.removeItem(at: restoreFile)
            return
        }
        device = dev
        originalLevel = level
        currentLevel = 1.0
        lock.lock()
        clipEvents = 0
        lock.unlock()
        timer = Timer.scheduledTimer(withTimeInterval: 0.3, repeats: true) { _ in onClipTick() }
        Logger.info("Microphone input raised from \(Int(level * 100))% to 100% for dictation.")
    }

    /// Devolve o nível original (se foi alterado). Chamar na thread principal.
    public static func restore() {
        timer?.invalidate()
        timer = nil
        guard device != 0, originalLevel >= 0 else { return }
        _ = setVolume(device, originalLevel)
        Logger.info("Microphone input restored to \(Int(originalLevel * 100))%.")
        device = 0
        originalLevel = -1
        currentLevel = -1
        try? FileManager.default.removeItem(at: restoreFile)
    }

    /// Na abertura: devolve o nível se o VoxIA fechou no meio de um ditado.
    public static func restoreAfterCrash() {
        guard let saved = try? String(contentsOf: restoreFile, encoding: .utf8),
              let level = Float32(saved.trimmingCharacters(in: .whitespacesAndNewlines)) else { return }
        if let dev = defaultInputDevice() { _ = setVolume(dev, max(0, min(1, level))) }
        try? FileManager.default.removeItem(at: restoreFile)
        Logger.info("Microphone input restored after an unexpected exit.")
    }

    private static func onClipTick() {
        lock.lock()
        let clips = clipEvents
        clipEvents = 0
        lock.unlock()
        guard clips > 0, device != 0, currentLevel > originalLevel else { return }
        let next = max(originalLevel, currentLevel - stepDown)
        if setVolume(device, next) {
            currentLevel = next
            Logger.info("Microphone was clipping; input lowered to \(Int(next * 100))%.")
        }
    }

    // MARK: - CoreAudio

    private static func defaultInputDevice() -> AudioDeviceID? {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultInputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var dev = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        let status = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &dev)
        return status == noErr && dev != 0 ? dev : nil
    }

    private static func volumeAddress() -> AudioObjectPropertyAddress {
        return AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyVolumeScalar,
            mScope: kAudioDevicePropertyScopeInput,
            mElement: kAudioObjectPropertyElementMain)
    }

    private static func volume(_ dev: AudioDeviceID) -> Float32? {
        var address = volumeAddress()
        guard AudioObjectHasProperty(dev, &address) else { return nil }
        var value = Float32(0)
        var size = UInt32(MemoryLayout<Float32>.size)
        let status = AudioObjectGetPropertyData(dev, &address, 0, nil, &size, &value)
        return status == noErr ? value : nil
    }

    private static func setVolume(_ dev: AudioDeviceID, _ level: Float32) -> Bool {
        var address = volumeAddress()
        var settable: DarwinBoolean = false
        guard AudioObjectHasProperty(dev, &address),
              AudioObjectIsPropertySettable(dev, &address, &settable) == noErr, settable.boolValue else { return false }
        var value = level
        let status = AudioObjectSetPropertyData(dev, &address, 0, nil, UInt32(MemoryLayout<Float32>.size), &value)
        return status == noErr
    }
}
