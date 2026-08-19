import Foundation

public class AppPathService {
    public static var dataDirectory: URL {
        let fileManager = FileManager.default
        let homeDir = fileManager.homeDirectoryForCurrentUser
        
        // Check for portable mode directory (./data alongside executable)
        let appDir = Bundle.main.bundleURL.deletingLastPathComponent()
        let portableDir = appDir.appendingPathComponent("data")
        let portableMarker = appDir.appendingPathComponent("portable.txt")
        
        if fileManager.fileExists(atPath: portableDir.path) || fileManager.fileExists(atPath: portableMarker.path) {
            EnsureDirectory(portableDir)
            return portableDir
        }
        
        // Default macOS Application Support directory: ~/Library/Application Support/VoxIA
        let appSupportDir = homeDir.appendingPathComponent("Library/Application Support/VoxIA")
        EnsureDirectory(appSupportDir)
        return appSupportDir
    }
    
    public static var audioQueueDirectory: URL {
        let dir = dataDirectory.appendingPathComponent("audio_queue")
        EnsureDirectory(dir)
        return dir
    }
    
    public static var logsDirectory: URL {
        let dir = dataDirectory.appendingPathComponent("logs")
        EnsureDirectory(dir)
        return dir
    }
    
    public static var settingsFilePath: URL {
        return dataDirectory.appendingPathComponent("settings.json")
    }
    
    public static var queueFilePath: URL {
        return dataDirectory.appendingPathComponent("queue.json")
    }
    
    public static var historyFilePath: URL {
        return dataDirectory.appendingPathComponent("history.json")
    }
    
    public static var dictionaryFilePath: URL {
        return dataDirectory.appendingPathComponent("dictionary.json")
    }
    
    public static var credentialsFilePath: URL {
        return dataDirectory.appendingPathComponent("credentials.dat")
    }
    
    private static func EnsureDirectory(_ url: URL) {
        if !FileManager.default.fileExists(atPath: url.path) {
            try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true, attributes: nil)
        }
    }
}
