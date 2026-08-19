import Foundation

public struct MacAppSettings: Codable {
    public var whisperModel: String
    public var correctionModel: String
    public var selectedPromptPreset: String
    public var customPromptText: String
    public var autoCopyToClipboard: Bool
    public var hotkeyDisplay: String
    
    public init() {
        self.whisperModel = "whisper-large-v3-turbo"
        self.correctionModel = "openai/gpt-oss-120b"
        self.selectedPromptPreset = "Geral / Ortografia"
        self.customPromptText = ""
        self.autoCopyToClipboard = true
        self.hotkeyDisplay = "Option + Space"
    }
}

public class SettingsService {
    public static var current = MacAppSettings()
    private static let settingsURL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!.appendingPathComponent("VoxIAMac/settings.json")

    public static func load() {
        if let data = try? Data(contentsOf: settingsURL),
           var settings = try? JSONDecoder().decode(MacAppSettings.self, from: data) {
            if settings.whisperModel != "whisper-large-v3" && settings.whisperModel != "whisper-large-v3-turbo" {
                settings.whisperModel = "whisper-large-v3-turbo"
            }
            if settings.correctionModel != "openai/gpt-oss-120b" && 
               settings.correctionModel != "llama-3.3-70b-versatile" && 
               settings.correctionModel != "openai/gpt-oss-20b" {
                settings.correctionModel = "openai/gpt-oss-120b"
            }
            if settings.selectedPromptPreset != "Customizado" {
                settings.selectedPromptPreset = "Geral / Ortografia"
            }
            self.current = settings
        } else {
            self.current = MacAppSettings()
        }
    }

    public static func save() {
        if current.whisperModel != "whisper-large-v3" && current.whisperModel != "whisper-large-v3-turbo" {
            current.whisperModel = "whisper-large-v3-turbo"
        }
        if current.correctionModel != "openai/gpt-oss-120b" && 
           current.correctionModel != "llama-3.3-70b-versatile" && 
           current.correctionModel != "openai/gpt-oss-20b" {
            current.correctionModel = "openai/gpt-oss-120b"
        }
        if current.selectedPromptPreset != "Customizado" {
            current.selectedPromptPreset = "Geral / Ortografia"
        }
        
        let folder = settingsURL.deletingLastPathComponent()
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true, attributes: nil)
        
        if let data = try? JSONEncoder().encode(current) {
            try? data.write(to: settingsURL)
            Logger.info("macOS Settings saved successfully.")
        }
    }
}
