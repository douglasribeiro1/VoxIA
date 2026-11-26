import Foundation

public struct DictationHistoryItem: Codable {
    public var id: String
    public var timestamp: Date
    public var durationSeconds: Double
    public var rawTranscript: String
    public var finalText: String
    public var targetProcess: String
    public var wordCount: Int
}

public class HistoryRepository {
    private static var history: [DictationHistoryItem] = []
    private static let lock = NSLock()

    public static func load() {
        lock.lock()
        defer { lock.unlock() }

        if FileManager.default.fileExists(atPath: AppPathService.historyFilePath.path),
           let data = try? Data(contentsOf: AppPathService.historyFilePath),
           let list = try? JSONDecoder().decode([DictationHistoryItem].self, from: data) {
            history = list
        }
    }

    /// Ditados mais recentes primeiro (usado pela janela principal).
    public static func recent(limit: Int = 20) -> [DictationHistoryItem] {
        lock.lock()
        defer { lock.unlock() }
        return Array(history.sorted { $0.timestamp > $1.timestamp }.prefix(limit))
    }

    public static func addRecord(rawText: String, finalText: String, durationSec: Double, targetProcess: String) {
        guard !finalText.isEmpty else { return }
        let wordCount = finalText.components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }.count

        let item = DictationHistoryItem(
            id: UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased(),
            timestamp: Date(),
            durationSeconds: (durationSec * 10).rounded() / 10,
            rawTranscript: rawText,
            finalText: finalText,
            targetProcess: targetProcess,
            wordCount: wordCount
        )

        lock.lock()
        history.insert(item, at: 0)
        if history.count > 1000 {
            history = Array(history.prefix(1000))
        }
        if let data = try? JSONEncoder().encode(history) {
            try? data.write(to: AppPathService.historyFilePath)
        }
        lock.unlock()
    }
}
