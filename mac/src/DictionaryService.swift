import Foundation

public struct DictionaryEntry: Codable {
    public var id: String
    public var original: String
    public var replacement: String
    public var isVocabularyOnly: Bool
    public var createdAt: String

    public init(id: String = UUID().uuidString, original: String, replacement: String, isVocabularyOnly: Bool = false, createdAt: String? = nil) {
        self.id = id
        self.original = original.trimmingCharacters(in: .whitespacesAndNewlines)
        self.replacement = replacement.trimmingCharacters(in: .whitespacesAndNewlines)
        self.isVocabularyOnly = isVocabularyOnly
        
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
        self.createdAt = createdAt ?? formatter.string(from: Date())
    }
}

public class DictionaryService {
    private static var entries: [DictionaryEntry] = []
    private static let queue = DispatchQueue(label: "com.voxia.mac.dictionary", attributes: .concurrent)
    private static var isLoaded = false

    public static func load() {
        queue.async(flags: .barrier) {
            let path = AppPathService.dictionaryFilePath.path
            let fileManager = FileManager.default

            if fileManager.fileExists(atPath: path) {
                do {
                    let data = try Data(contentsOf: AppPathService.dictionaryFilePath)
                    let decoder = JSONDecoder()
                    entries = try decoder.decode([DictionaryEntry].self, from: data)
                    Logger.info("Dictionary loaded successfully (\(entries.count) entries).")
                } catch {
                    Logger.error("Failed to decode dictionary.json: \(error.localizedDescription)")
                    entries = []
                }
            } else {
                entries = [
                    DictionaryEntry(original: "desartria", replacement: "disartria")
                ]
                saveInternal()
            }
            isLoaded = true
        }
    }

    public static func getAll() -> [DictionaryEntry] {
        return queue.sync {
            if !isLoaded {
                // synchronous initial load
                loadSync()
            }
            return entries
        }
    }

    private static func loadSync() {
        let path = AppPathService.dictionaryFilePath.path
        if FileManager.default.fileExists(atPath: path),
           let data = try? Data(contentsOf: AppPathService.dictionaryFilePath),
           let decoded = try? JSONDecoder().decode([DictionaryEntry].self, from: data) {
            entries = decoded
        } else {
            entries = [DictionaryEntry(original: "desartria", replacement: "disartria")]
        }
        isLoaded = true
    }

    public static func addOrUpdate(original: String, replacement: String, isVocabularyOnly: Bool = false) {
        var cleanOrig = original.trimmingCharacters(in: .whitespacesAndNewlines)
        var cleanRepl = replacement.trimmingCharacters(in: .whitespacesAndNewlines)

        if cleanOrig.isEmpty && cleanRepl.isEmpty { return }
        if cleanOrig.isEmpty { cleanOrig = cleanRepl }
        if cleanRepl.isEmpty { cleanRepl = cleanOrig }

        queue.async(flags: .barrier) {
            if let index = entries.firstIndex(where: { $0.original.caseInsensitiveCompare(cleanOrig) == .orderedSame }) {
                entries[index].replacement = cleanRepl
                entries[index].isVocabularyOnly = isVocabularyOnly
            } else {
                let newEntry = DictionaryEntry(original: cleanOrig, replacement: cleanRepl, isVocabularyOnly: isVocabularyOnly)
                entries.insert(newEntry, at: 0)
            }
            saveInternal()
        }
    }

    public static func remove(id: String) {
        queue.async(flags: .barrier) {
            entries.removeAll(where: { $0.id == id })
            saveInternal()
        }
    }

    public static func clear() {
        queue.async(flags: .barrier) {
            entries.removeAll()
            saveInternal()
        }
    }

    private static func saveInternal() {
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = .prettyPrinted
            let data = try encoder.encode(entries)
            try data.write(to: AppPathService.dictionaryFilePath, options: .atomic)
            Logger.info("Dictionary saved to disk.")
        } catch {
            Logger.error("Failed to save dictionary: \(error.localizedDescription)")
        }
    }

    /// Grafias corretas dos termos do dicionário, separadas por vírgula, para orientar o Whisper e a
    /// correção pela IA (as palavras "como deve ficar" e os termos só de vocabulário).
    public static func vocabularyTerms() -> String {
        var seen = Set<String>()
        var terms: [String] = []
        for e in getAll() {
            let term = (e.isVocabularyOnly || e.replacement.isEmpty) ? e.original : e.replacement
            let clean = term.trimmingCharacters(in: .whitespacesAndNewlines)
            if clean.isEmpty || seen.contains(clean.lowercased()) { continue }
            seen.insert(clean.lowercased())
            terms.append(clean)
        }
        return terms.joined(separator: ", ")
    }

    /// Applies deterministic local regex replacement with intelligent Case-Matching across the text.
    public static func applyReplacements(_ text: String) -> String {
        guard !text.isEmpty else { return text }
        let currentEntries = getAll()
        guard !currentEntries.isEmpty else { return text }

        var result = text

        for entry in currentEntries {
            if entry.isVocabularyOnly { continue }
            if entry.original.isEmpty || entry.replacement.isEmpty { continue }
            if entry.original.caseInsensitiveCompare(entry.replacement) == .orderedSame { continue }

            let pattern = "\\b" + NSRegularExpression.escapedPattern(for: entry.original) + "\\b"
            if let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive) {
                let nsString = result as NSString
                let matches = regex.matches(in: result, options: [], range: NSRange(location: 0, length: nsString.length))
                
                // Process replacements in reverse order so ranges stay valid
                for match in matches.reversed() {
                    let matchedWord = nsString.substring(with: match.range)
                    let replacedWord = matchCase(originalMatch: matchedWord, target: entry.replacement)
                    result = (result as NSString).replacingCharacters(in: match.range, with: replacedWord)
                }
            }
        }

        return result
    }

    private static func matchCase(originalMatch: String, target: String) -> String {
        guard !originalMatch.isEmpty, !target.isEmpty else { return target }

        // All upper: "DESARTRIA" -> "DISARTRIA"
        if originalMatch == originalMatch.uppercased() && originalMatch.rangeOfCharacter(from: .letters) != nil {
            return target.uppercased()
        }

        // Capitalized / Title case: "Desartria" -> "Disartria"
        if let first = originalMatch.first, first.isUppercase {
            return target.prefix(1).uppercased() + target.dropFirst()
        }

        // Lowercase / Default: "desartria" -> "disartria"
        return target
    }
}
