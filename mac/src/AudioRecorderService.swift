import Foundation
import AVFoundation

public class AudioRecorderService: NSObject, AVAudioRecorderDelegate {
    private var audioRecorder: AVAudioRecorder?
    public private(set) var isRecording: Bool = false
    public private(set) var currentFilePath: URL?
    private var recordingStartTime: Date = Date()

    public var recordingDuration: TimeInterval {
        return isRecording ? Date().timeIntervalSince(recordingStartTime) : 0
    }

    public func startRecording() throws -> URL {
        if isRecording {
            _ = stopRecording()
        }

        let audioId = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        let fileURL = AppPathService.audioQueueDirectory.appendingPathComponent("rec_\(audioId).wav")
        currentFilePath = fileURL

        let settings: [String: Any] = [
            AVFormatIDKey: Int(kAudioFormatLinearPCM),
            AVSampleRateKey: 16000.0,
            AVNumberOfChannelsKey: 1,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsBigEndianKey: false,
            AVLinearPCMIsFloatKey: false
        ]

        audioRecorder = try AVAudioRecorder(url: fileURL, settings: settings)
        audioRecorder?.delegate = self
        audioRecorder?.prepareToRecord()

        guard audioRecorder?.record() == true else {
            throw NSError(domain: "VoxIAMac", code: 1, userInfo: [NSLocalizedDescriptionKey: "Failed to start AVAudioRecorder on macOS."])
        }

        isRecording = true
        recordingStartTime = Date()
        Logger.info("macOS AVFoundation audio recording started (16kHz Mono WAV): \(fileURL.path)")
        return fileURL
    }

    public func stopRecording() -> URL? {
        guard isRecording else { return currentFilePath }

        audioRecorder?.stop()
        isRecording = false

        if let path = currentFilePath, FileManager.default.fileExists(atPath: path.path) {
            Logger.info("macOS audio recording stopped and saved: \(path.path)")
            return path
        }
        return nil
    }

    public func cancelRecording() {
        guard isRecording else { return }
        audioRecorder?.stop()
        isRecording = false

        if let path = currentFilePath {
            try? FileManager.default.removeItem(at: path)
        }
        currentFilePath = nil
        Logger.info("macOS audio recording cancelled and file purged.")
    }
}
