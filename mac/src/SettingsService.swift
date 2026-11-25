import Foundation

public struct MacAppSettings: Codable {
    public var whisperModel = "whisper-large-v3"
    public var correctionModel = "openai/gpt-oss-120b"
    public var selectedPromptPreset = "Geral / Ortografia"
    public var customPromptText = ""
    public var autoCopyToClipboard = true
    public var hotkeyDisplay = "Option + Space"
    public var autoRetryOnReconnect = true
    /// Sobe o volume de captação do microfone para 100% durante o ditado (e baixa se saturar).
    public var boostMicrophoneDuringDictation = true
    /// Procura atualizações ao abrir e uma vez por dia.
    public var autoCheckUpdates = true
    /// Última busca de atualizações (segundos desde 1970); 0 = nunca.
    public var lastUpdateCheck: Double = 0

    public init() {}

    // Configurações salvas por versões antigas não têm todos os campos: o que faltar usa o padrão
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = MacAppSettings()
        whisperModel = try c.decodeIfPresent(String.self, forKey: .whisperModel) ?? d.whisperModel
        correctionModel = try c.decodeIfPresent(String.self, forKey: .correctionModel) ?? d.correctionModel
        selectedPromptPreset = try c.decodeIfPresent(String.self, forKey: .selectedPromptPreset) ?? d.selectedPromptPreset
        customPromptText = try c.decodeIfPresent(String.self, forKey: .customPromptText) ?? d.customPromptText
        autoCopyToClipboard = try c.decodeIfPresent(Bool.self, forKey: .autoCopyToClipboard) ?? d.autoCopyToClipboard
        hotkeyDisplay = try c.decodeIfPresent(String.self, forKey: .hotkeyDisplay) ?? d.hotkeyDisplay
        autoRetryOnReconnect = try c.decodeIfPresent(Bool.self, forKey: .autoRetryOnReconnect) ?? d.autoRetryOnReconnect
        boostMicrophoneDuringDictation = try c.decodeIfPresent(Bool.self, forKey: .boostMicrophoneDuringDictation) ?? d.boostMicrophoneDuringDictation
        autoCheckUpdates = try c.decodeIfPresent(Bool.self, forKey: .autoCheckUpdates) ?? d.autoCheckUpdates
        lastUpdateCheck = try c.decodeIfPresent(Double.self, forKey: .lastUpdateCheck) ?? d.lastUpdateCheck
    }
}

public enum SettingsService {
    public static var current = MacAppSettings()

    public static func load() {
        if let data = try? Data(contentsOf: AppPathService.settingsFilePath),
           let settings = try? JSONDecoder().decode(MacAppSettings.self, from: data) {
            current = settings
        } else {
            current = MacAppSettings()
        }
        // Precisão em primeiro lugar: o modelo completo do Whisper, como no Windows e no Android
        current.whisperModel = "whisper-large-v3"
        let allowed = ["openai/gpt-oss-120b", "llama-3.3-70b-versatile", "openai/gpt-oss-20b"]
        if !allowed.contains(current.correctionModel) { current.correctionModel = "openai/gpt-oss-120b" }
    }

    public static func save() {
        let encoder = JSONEncoder()
        encoder.outputFormatting = .prettyPrinted
        if let data = try? encoder.encode(current) {
            try? data.write(to: AppPathService.settingsFilePath, options: .atomic)
        }
    }
}
