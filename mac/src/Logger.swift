import Foundation

public class Logger {
    private static let lock = NSLock()

    public static func info(_ message: String) {
        write("INFO", message)
    }

    public static func warning(_ message: String) {
        write("WARN", message)
    }

    public static func error(_ message: String, error: Error? = nil) {
        var msg = message
        if let err = error {
            msg += " | Error: \(err.localizedDescription)"
        }
        write("ERROR", msg)
    }

    private static func write(_ level: String, _ message: String) {
        lock.lock()
        defer { lock.unlock() }

        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd"
        let dateStr = formatter.string(from: Date())

        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
        let timeStr = formatter.string(from: Date())

        let sanitizedMsg = message.replacingOccurrences(of: "gsk_[a-zA-Z0-9_-]+", with: "gsk_REDACTED", options: .regularExpression)
        let logLine = "[\(timeStr)] [\(level)] \(sanitizedMsg)\n"

        let logFile = AppPathService.logsDirectory.appendingPathComponent("app_\(dateStr).log")

        if let data = logLine.data(using: .utf8) {
            if FileManager.default.fileExists(atPath: logFile.path) {
                if let handle = try? FileHandle(forWritingTo: logFile) {
                    handle.seekToEndOfFile()
                    handle.write(data)
                    handle.closeFile()
                }
            } else {
                try? data.write(to: logFile)
            }
        }
    }
}
