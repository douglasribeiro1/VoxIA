import Foundation

public class TextCorrectionService {
    private static let endpoint = URL(string: "https://api.groq.com/openai/v1/chat/completions")!

    public static func correctText(_ rawText: String, modelOverride: String? = nil, completion: @escaping (Result<String, Error>) -> Void) {
        guard !rawText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            completion(.success(""))
            return
        }

        let apiKey = SecureCredentialService.getApiKey()
        guard !apiKey.isEmpty else {
            completion(.failure(NSError(domain: "VoxIAMac", code: 401, userInfo: [NSLocalizedDescriptionKey: "Groq API key is missing. Configure in Settings."])))
            return
        }

        let model = modelOverride ?? (SettingsService.current.correctionModel.isEmpty ? "openai/gpt-oss-120b" : SettingsService.current.correctionModel)
        let systemPrompt = getSystemPrompt(preset: SettingsService.current.selectedPromptPreset, custom: SettingsService.current.customPromptText)

        var request = URLRequest(url: endpoint)
        request.timeoutInterval = 90
        request.httpMethod = "POST"
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")

        // max_tokens also covers the reasoning tokens of gpt-oss models; keep it generous for long dictations
        let maxTokens = max(1024, min(7000, rawText.count * 2 + 1500))
        var payload: [String: Any] = [
            "model": model,
            "temperature": 0.0,
            "max_tokens": maxTokens,
            "messages": [
                ["role": "system", "content": systemPrompt],
                ["role": "user", "content": rawText]
            ]
        ]

        if model.lowercased().contains("gpt-oss") {
            payload["reasoning_effort"] = "low"
        }

        guard let bodyData = try? JSONSerialization.data(withJSONObject: payload) else {
            completion(.success(rawText))
            return
        }

        request.httpBody = bodyData

        Logger.info("Sending macOS raw transcript (\(rawText.count) chars) to Groq LLM ('\(model)')...")

        let task = URLSession.shared.dataTask(with: request) { data, response, error in
            if let error = error {
                completion(.failure(error))
                return
            }

            guard let data = data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let choices = json["choices"] as? [[String: Any]],
                  let firstChoice = choices.first,
                  let message = firstChoice["message"] as? [String: Any],
                  let content = message["content"] as? String else {
                completion(.success(rawText))
                return
            }

            if let finish = firstChoice["finish_reason"] as? String, finish == "length" {
                Logger.info("LLM output truncated (finish_reason=length); using raw transcript to avoid losing content.")
                completion(.success(rawText))
                return
            }

            let cleaned = cleanOutput(content)
            if rawText.count >= 200 && Double(cleaned.count) < Double(rawText.count) * 0.75 {
                Logger.info("LLM output suspiciously shorter than input (\(cleaned.count) vs \(rawText.count) chars); using raw transcript.")
                completion(.success(rawText))
                return
            }
            completion(.success(cleaned))
        }
        task.resume()
    }

    private static func getSystemPrompt(preset: String, custom: String) -> String {
        if !custom.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && preset == "Customizado" {
            return custom
        }

        return """
Você é um corretor ortográfico automático e estritamente literal de transcrição de voz em português.
SUA ÚNICA TAREFA: Corrigir a ortografia, acentuação, capitalização e pontuação da frase falada.

REGRAS ABSOLUTAS E INVIOLÁVEIS:
1. NUNCA RESPONDA A PERGUNTAS! Se o usuário ditar uma pergunta (exemplo: 'O paciente tem febre?' ou 'Qual é o tratamento?'), NUNCA tente responder à pergunta. Sua função NÃO É conversar nem responder. Sua ÚNICA função é retornar a própria pergunta transcrita e corrigida ortograficamente (exemplo: 'O paciente tem febre?').
2. NUNCA REESCREVA A FRASE e NUNCA OMITA NENHUMA PALAVRA.
3. NUNCA substitua palavras por sinônimos nem altere o vocabulário.
4. NUNCA resuma, comente ou adicione informações.
5. NÃO inclua explicações, introduções, aspas ou formatação Markdown.

RETORNE APENAS A TRANSCRIÇÃO CORRIGIDA.
"""
    }

    private static func cleanOutput(_ text: String) -> String {
        var str = text.trimmingCharacters(in: .whitespacesAndNewlines)

        // Strip out <think>...</think> reasoning blocks from Qwen / DeepSeek models
        if let thinkRange = str.range(of: "</think>", options: .caseInsensitive) {
            str = String(str[thinkRange.upperBound...]).trimmingCharacters(in: .whitespacesAndNewlines)
        }

        if str.hasPrefix("\"") && str.hasSuffix("\"") && str.count > 1 {
            str = String(str.dropFirst().dropLast()).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return str
    }
}
