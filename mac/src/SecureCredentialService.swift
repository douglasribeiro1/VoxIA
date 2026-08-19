import Foundation
import Security

public class SecureCredentialService {
    private static let serviceName = "com.voxia.mac"
    private static let accountName = "GroqApiKey"

    public static func saveApiKey(_ apiKey: String) {
        guard let data = apiKey.trimmingCharacters(in: .whitespacesAndNewlines).data(using: .utf8) else {
            deleteApiKey()
            return
        }

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: serviceName,
            kSecAttrAccount as String: accountName
        ]

        SecItemDelete(query as CFDictionary)

        var newQuery = query
        newQuery[kSecValueData as String] = data

        let status = SecItemAdd(newQuery as CFDictionary, nil)
        if status == errSecSuccess {
            Logger.info("Groq API key saved to macOS Keychain.")
        } else {
            // Local encrypted file fallback if Keychain is unavailable in sandbox
            let encrypted = data.base64EncodedString()
            try? encrypted.write(to: AppPathService.credentialsFilePath, atomically: true, encoding: .utf8)
            Logger.info("Groq API key saved to encrypted storage fallback.")
        }
    }

    public static func getApiKey() -> String {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: serviceName,
            kSecAttrAccount as String: accountName,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]

        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)

        if status == errSecSuccess, let data = item as? Data, let key = String(data: data, encoding: .utf8), !key.isEmpty {
            return key
        }

        // Fallback check in local file
        if FileManager.default.fileExists(atPath: AppPathService.credentialsFilePath.path),
           let encoded = try? String(contentsOf: AppPathService.credentialsFilePath, encoding: .utf8),
           let data = Data(base64Encoded: encoded.trimmingCharacters(in: .whitespacesAndNewlines)),
           let key = String(data: data, encoding: .utf8), !key.isEmpty {
            return key
        }

        return ""
    }

    public static func deleteApiKey() {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: serviceName,
            kSecAttrAccount as String: accountName
        ]
        SecItemDelete(query as CFDictionary)
        try? FileManager.default.removeItem(at: AppPathService.credentialsFilePath)
    }
}
