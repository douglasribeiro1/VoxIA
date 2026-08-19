import Foundation

public enum AudioQueueStatus: String, Codable {
    case PENDING
    case UPLOADING
    case TRANSCRIBING
    case CORRECTING
    case COMPLETED
    case ERROR
}

public struct AudioQueueItem: Codable {
    public var id: String
    public var createdAt: Date
    public var durationSeconds: Double
    public var filePath: String
    public var status: AudioQueueStatus
    public var retryCount: Int
    public var lastError: String?
    public var targetContext: TargetContext?
}

public class PendingAudioRepository {
    private static var items: [AudioQueueItem] = []
    private static let lock = NSLock()

    public static func loadQueue() {
        lock.lock()
        defer { lock.unlock() }

        if FileManager.default.fileExists(atPath: AppPathService.queueFilePath.path),
           let data = try? Data(contentsOf: AppPathService.queueFilePath),
           let list = try? JSONDecoder().decode([AudioQueueItem].self, from: data) {
            items = list.map { var item = $0; if item.status == .UPLOADING || item.status == .TRANSCRIBING || item.status == .CORRECTING { item.status = .PENDING }; return item }
        }
    }

    public static func getPendingItems() -> [AudioQueueItem] {
        lock.lock()
        defer { lock.unlock() }
        return items.filter { $0.status == .PENDING || $0.status == .ERROR }
    }

    public static func getAll() -> [AudioQueueItem] {
        lock.lock()
        defer { lock.unlock() }
        return items
    }

    public static func addRecording(filePath: String, durationSeconds: Double, targetContext: TargetContext?) -> AudioQueueItem {
        let item = AudioQueueItem(
            id: UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased(),
            createdAt: Date(),
            durationSeconds: durationSeconds,
            filePath: filePath,
            status: .PENDING,
            retryCount: 0,
            lastError: nil,
            targetContext: targetContext
        )

        lock.lock()
        items.append(item)
        saveQueueUnlocked()
        lock.unlock()

        Logger.info("Added recording to macOS persistent queue. ID: \(item.id)")
        return item
    }

    public static func markCompleted(id: String) {
        lock.lock()
        defer { lock.unlock() }

        if let idx = items.firstIndex(where: { $0.id == id }) {
            let path = items[idx].filePath
            try? FileManager.default.removeItem(atPath: path)
            items.remove(at: idx)
            saveQueueUnlocked()
            Logger.info("macOS queue item completed and audio purged: \(id)")
        }
    }

    public static func updateStatus(id: String, status: AudioQueueStatus, error: String? = nil) {
        lock.lock()
        defer { lock.unlock() }

        if let idx = items.firstIndex(where: { $0.id == id }) {
            items[idx].status = status
            if let err = error {
                items[idx].lastError = err
                items[idx].retryCount += 1
            }
            saveQueueUnlocked()
        }
    }

    public static func deleteItem(id: String) {
        lock.lock()
        defer { lock.unlock() }

        if let idx = items.firstIndex(where: { $0.id == id }) {
            try? FileManager.default.removeItem(atPath: items[idx].filePath)
            items.remove(at: idx)
            saveQueueUnlocked()
        }
    }

    private static func saveQueueUnlocked() {
        if let data = try? JSONEncoder().encode(items) {
            try? data.write(to: AppPathService.queueFilePath)
        }
    }
}
