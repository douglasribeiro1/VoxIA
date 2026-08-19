import Foundation

public class TranscriptionService {
    private static let endpoint = URL(string: "https://api.groq.com/openai/v1/audio/transcriptions")!

    public static func transcribeAudio(fileURL: URL, modelOverride: String? = nil, completion: @escaping (Result<String, Error>) -> Void) {
        let apiKey = SecureCredentialService.getApiKey()
        guard !apiKey.isEmpty else {
            completion(.failure(NSError(domain: "VoxIAMac", code: 401, userInfo: [NSLocalizedDescriptionKey: "Groq API key is missing. Configure in Settings."])))
            return
        }

        let model = modelOverride ?? (SettingsService.current.whisperModel.isEmpty ? "whisper-large-v3" : SettingsService.current.whisperModel)
        let boundary = "Boundary-\(UUID().uuidString)"

        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")

        var body = Data()

        // Audio File field
        if let audioData = try? Data(contentsOf: fileURL) {
            body.append("--\(boundary)\r\n".data(using: .utf8)!)
            body.append("Content-Disposition: form-data; name=\"file\"; filename=\"\(fileURL.lastPathComponent)\"\r\n".data(using: .utf8)!)
            body.append("Content-Type: audio/wav\r\n\r\n".data(using: .utf8)!)
            body.append(audioData)
            body.append("\r\n".data(using: .utf8)!)
        }

        // Model field
        body.append("--\(boundary)\r\n".data(using: .utf8)!)
        body.append("Content-Disposition: form-data; name=\"model\"\r\n\r\n".data(using: .utf8)!)
        body.append("\(model)\r\n".data(using: .utf8)!)

        // Language field
        body.append("--\(boundary)\r\n".data(using: .utf8)!)
        body.append("Content-Disposition: form-data; name=\"language\"\r\n\r\n".data(using: .utf8)!)
        body.append("pt\r\n".data(using: .utf8)!)

        // Temperature field
        body.append("--\(boundary)\r\n".data(using: .utf8)!)
        body.append("Content-Disposition: form-data; name=\"temperature\"\r\n\r\n".data(using: .utf8)!)
        body.append("0.0\r\n".data(using: .utf8)!)

        body.append("--\(boundary)--\r\n".data(using: .utf8)!)
        request.httpBody = body

        Logger.info("Sending macOS audio file (\(fileURL.lastPathComponent)) to Groq Whisper ('\(model)')...")

        let task = URLSession.shared.dataTask(with: request) { data, response, error in
            if let error = error {
                completion(.failure(error))
                return
            }

            guard let data = data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                completion(.failure(NSError(domain: "VoxIAMac", code: 500, userInfo: [NSLocalizedDescriptionKey: "Invalid response from Groq Whisper API."])))
                return
            }

            if let text = json["text"] as? String {
                completion(.success(text))
            } else if let errDict = json["error"] as? [String: Any], let msg = errDict["message"] as? String {
                completion(.failure(NSError(domain: "VoxIAMac", code: 400, userInfo: [NSLocalizedDescriptionKey: msg])))
            } else {
                completion(.failure(NSError(domain: "VoxIAMac", code: 500, userInfo: [NSLocalizedDescriptionKey: "Failed to parse Whisper response."])))
            }
        }
        task.resume()
    }
}
