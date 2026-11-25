import Foundation

/// Whisper da Groq, com a mesma lógica das versões Windows e Android: áudio em FLAC sem perdas,
/// vocabulário do dicionário no prompt, ditados longos em blocos cortados nas pausas (cada bloco
/// recebe o texto dos anteriores como prompt) e filtros contra "alucinações" do Whisper.
public enum TranscriptionService {
    private static let endpoint = URL(string: "https://api.groq.com/openai/v1/audio/transcriptions")!
    private static let basePrompt = "Ditado por voz em português brasileiro sem legendas."
    private static let clearSpeechFrames = 25 // 0,5 s de fala

    public struct TranscriptionError: LocalizedError {
        public let message: String
        public let permanent: Bool
        public var errorDescription: String? { return message }
    }

    /// Transcreve a gravação (já tratada). [prefix] = texto já transcrito durante a fala, usado como
    /// prompt do primeiro bloco. Devolve só o texto deste áudio.
    public static func transcribe(pcm: [Int16], prefix: String, speechFrames: Int) async throws -> String {
        let vocabulary = DictionaryService.vocabularyTerms()
        let cuts = AudioChunker.findCuts(pcm)
        if cuts.isEmpty {
            let prompt = buildPrompt(vocabulary: vocabulary, context: prefix)
            let text = sanitize(try await send(FlacEncoder.encode(pcm), prompt: prompt))
            if isPromptEcho(text, context: prefix) || isVocabularyEcho(text, vocabulary: vocabulary, speechFrames: speechFrames) {
                Logger.info("Whisper returned only its prompt (no real speech); discarded.")
                return ""
            }
            return text
        }

        // Nenhum bloco é pulado por "parecer sem fala": fala baixa (microfone longe) também vai
        let bounds = [0] + cuts + [pcm.count]
        var soFar = ""
        for i in 0..<(bounds.count - 1) {
            let part = Array(pcm[bounds[i]..<bounds[i + 1]])
            let context = soFar.isEmpty ? prefix : join(prefix, soFar)
            let prompt = buildPrompt(vocabulary: vocabulary, context: context)
            let piece = sanitize(try await send(FlacEncoder.encode(part), prompt: prompt))
            if isPromptEcho(piece, context: context) || isVocabularyEcho(piece, vocabulary: vocabulary, speechFrames: AudioChunker.speechFrames(part)) {
                Logger.info("Whisper block \(i + 1)/\(bounds.count - 1) only repeated its prompt; ignored.")
                continue
            }
            soFar = join(soFar, piece)
        }
        Logger.info("Whisper: \(bounds.count - 1) blocks, \(soFar.count) chars.")
        return soFar
    }

    /// Um bloco cortado numa pausa enquanto a pessoa ainda fala (ver LiveTranscriber).
    public static func transcribeLiveChunk(pcm: [Int16], promptContext: String) async throws -> String {
        let vocabulary = DictionaryService.vocabularyTerms()
        let prompt = buildPrompt(vocabulary: vocabulary, context: promptContext)
        let text = sanitize(try await send(FlacEncoder.encode(pcm), prompt: prompt))
        if isPromptEcho(text, context: promptContext) || isVocabularyEcho(text, vocabulary: vocabulary, speechFrames: AudioChunker.speechFrames(pcm)) {
            return ""
        }
        return text
    }

    public static func join(_ a: String, _ b: String) -> String {
        let x = a.trimmingCharacters(in: .whitespacesAndNewlines)
        let y = b.trimmingCharacters(in: .whitespacesAndNewlines)
        if x.isEmpty { return y }
        if y.isEmpty { return x }
        return x + " " + y
    }

    // MARK: - Envio

    private static func send(_ flac: Data, prompt: String) async throws -> String {
        let apiKey = SecureCredentialService.getApiKey()
        guard !apiKey.isEmpty else {
            throw TranscriptionError(message: "Chave da Groq não configurada.", permanent: true)
        }
        let model = SettingsService.current.whisperModel
        var lastError: Error = TranscriptionError(message: "Erro desconhecido na transcrição.", permanent: false)

        for attempt in 1...3 {
            let boundary = "Boundary-\(UUID().uuidString)"
            var request = URLRequest(url: endpoint)
            request.httpMethod = "POST"
            request.timeoutInterval = 120
            request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
            request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")

            var body = Data()
            func field(_ name: String, _ value: String) {
                body.append("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n".data(using: .utf8)!)
            }
            body.append("--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"audio.flac\"\r\nContent-Type: audio/flac\r\n\r\n".data(using: .utf8)!)
            body.append(flac)
            body.append("\r\n".data(using: .utf8)!)
            field("model", model)
            field("language", "pt")
            field("temperature", "0.0")
            field("prompt", prompt)
            field("response_format", "json")
            body.append("--\(boundary)--\r\n".data(using: .utf8)!)
            request.httpBody = body

            do {
                let (data, response) = try await URLSession.shared.data(for: request)
                let code = (response as? HTTPURLResponse)?.statusCode ?? 0
                let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
                if code == 200, let text = json?["text"] as? String {
                    return text
                }
                let detail = ((json?["error"] as? [String: Any])?["message"] as? String) ?? "HTTP \(code)"
                if code >= 400 && code < 500 && code != 429 {
                    throw TranscriptionError(message: "Erro do Whisper (\(code)): \(detail)", permanent: true)
                }
                lastError = TranscriptionError(message: "Erro do Whisper (\(code)): \(detail)", permanent: false)
            } catch let e as TranscriptionError {
                throw e
            } catch {
                lastError = TranscriptionError(message: "Falha de conexão. O áudio foi guardado para reenviar.", permanent: false)
            }
            if attempt < 3 { try? await Task.sleep(nanoseconds: UInt64(attempt) * 1_200_000_000) }
        }
        throw lastError
    }

    // MARK: - Prompt e filtros

    /// O Whisper se apoia no FINAL do prompt, então o texto já escrito vai por último.
    private static func buildPrompt(vocabulary: String, context: String) -> String {
        let base = vocabulary.isEmpty ? basePrompt : basePrompt + " Vocabulário: " + vocabulary
        let ctx = context.trimmingCharacters(in: .whitespacesAndNewlines)
        if ctx.isEmpty { return base }
        var tail = ctx.count > 220 ? String(ctx.suffix(220)) : ctx
        if ctx.count > 220, let space = tail.firstIndex(of: " "), tail.distance(from: tail.startIndex, to: space) < 40 {
            tail = String(tail[tail.index(after: space)...])
        }
        return base + " " + tail
    }

    private static func isPromptEcho(_ transcript: String, context: String) -> Bool {
        let t = transcript.trimmingCharacters(in: CharacterSet(charactersIn: ".!? ").union(.whitespacesAndNewlines))
        if t.count < 30 || context.isEmpty { return false }
        return context.range(of: t, options: .caseInsensitive) != nil
    }

    /// Resposta curta feita só de termos do vocabulário, com pouca fala no áudio: é o Whisper
    /// repetindo o prompt diante de ruído.
    private static func isVocabularyEcho(_ transcript: String, vocabulary: String, speechFrames: Int) -> Bool {
        if speechFrames < 0 || speechFrames >= clearSpeechFrames { return false }
        if transcript.isEmpty || vocabulary.isEmpty { return false }
        let spoken = words(transcript)
        if spoken.isEmpty || spoken.count > 4 { return false }
        let known = Set(words(basePrompt + " " + vocabulary))
        return spoken.allSatisfy { known.contains($0) }
    }

    private static func words(_ s: String) -> [String] {
        return s.lowercased().components(separatedBy: CharacterSet.alphanumerics.inverted).filter { !$0.isEmpty }
    }

    /// Tira marcações de som e frases de "fim de vídeo" que o Whisper inventa em silêncio.
    public static func sanitize(_ input: String) -> String {
        var text = input.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty { return "" }
        text = text.replacingOccurrences(of: "\\[[\\s\\S]*?\\]", with: "", options: .regularExpression)
        text = text.replacingOccurrences(of: "(?i)\\((?:m[úu]sica|risos|aplausos|sil[êe]ncio|ru[íi]do|legendas?[^\\)]*)\\)", with: "", options: .regularExpression)
        text = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let lower = text.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: " \t\r\n.,!?:;\"'"))
        let hallucinations: Set<String> = [
            "tchau obrigado", "tchau, obrigado", "obrigado por assistir", "muito obrigado por assistir",
            "inscreva-se no canal", "deixe seu like", "curta e compartilhe", "valeu tchau", "tchau tchau",
            "legendas pela comunidade amara.org", "amara.org", "subtitles by", "legendado por", "transcrito por"
        ]
        if hallucinations.contains(lower) { return "" }
        text = text.replacingOccurrences(of: "(?i)(?:legendas?\\s*(?:por|de|feitas?\\s*por)?|transcriç[ãa]o\\s*(?:por|de)?|subtitles?\\s*by)\\s*[:\\-–—]?\\s*[A-ZÀ-Úa-zà-ú\\s\\.]+$", with: "", options: .regularExpression)
        text = text.replacingOccurrences(of: "(?i)(?:obrigado por assistir|inscreva-se no canal|deixe seu like|curta e compartilhe|tchau,? obrigado)[\\.!\\s]*$", with: "", options: .regularExpression)
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
