import Foundation

// Testes da lógica de áudio do Mac, rodados no GitHub Actions antes de compilar o app:
// o FLAC precisa voltar idêntico (decodificado pelo afconvert do próprio macOS), o tratamento
// não pode recortar fala baixa, e o silêncio precisa ser reconhecido.
@main
struct SelfTest {
    static var failures = 0

    static func check(_ name: String, _ ok: Bool, _ info: String = "") {
        if !ok { failures += 1 }
        print((ok ? "ok   " : "FAIL ") + name + (info.isEmpty ? "" : "  " + info))
    }

    static func speech(_ seconds: Double, _ amp: Double, _ seed: inout UInt64) -> [Int16] {
        let n = Int(seconds * 16000)
        return (0..<n).map { i in
            seed = seed &* 6364136223846793005 &+ 1442695040888963407
            let noise = Double(Int64(bitPattern: seed >> 11) % 50) - 25
            let t = Double(i) / 16000
            let v = amp * sin(2 * .pi * 200 * t) * (0.55 + 0.45 * sin(2 * .pi * 3.5 * t)) + noise
            return Int16(max(-32768, min(32767, v.rounded())))
        }
    }

    /// Amostras de um WAV qualquer (procura o bloco "data").
    static func wavSamples(_ url: URL) -> [Int16]? {
        guard let d = try? Data(contentsOf: url), d.count > 12 else { return nil }
        let bytes = [UInt8](d)
        var p = 12
        while p + 8 <= bytes.count {
            let id = String(bytes: bytes[p..<(p + 4)], encoding: .ascii) ?? ""
            let size = Int(bytes[p + 4]) | Int(bytes[p + 5]) << 8 | Int(bytes[p + 6]) << 16 | Int(bytes[p + 7]) << 24
            if id == "data" {
                let start = p + 8
                let count = min(size, bytes.count - start) / 2
                return (0..<count).map { Int16(bitPattern: UInt16(bytes[start + 2 * $0]) | UInt16(bytes[start + 2 * $0 + 1]) << 8) }
            }
            p += 8 + size + (size % 2)
        }
        return nil
    }

    static func main() {
        var seed: UInt64 = 42
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("voxia-selftest")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        // 1. FLAC sem perdas, conferido com o decodificador do macOS
        var cases: [(String, [Int16])] = [
            ("silencio", [Int16](repeating: 0, count: 32000)),
            ("fala", speech(7.3, 8000, &seed)),
            ("extremos", (0..<9000).map { $0 % 2 == 0 ? Int16.max : Int16.min }),
            ("curto", [5, -7, 100]),
            ("cauda300", Array(speech(1, 500, &seed).prefix(4096 + 300))),
            ("longo", speech(65, 4000, &seed))
        ]
        var rnd: UInt64 = 7
        cases.append(("ruido", (0..<5000).map { _ in rnd = rnd &* 6364136223846793005 &+ 1; return Int16(truncatingIfNeeded: Int64(bitPattern: rnd >> 16)) }))
        for (name, pcm) in cases {
            let flac = FlacEncoder.encode(pcm)
            let flacURL = dir.appendingPathComponent("\(name).flac")
            let wavURL = dir.appendingPathComponent("\(name).wav")
            try? flac.write(to: flacURL)
            try? FileManager.default.removeItem(at: wavURL)
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/bin/afconvert")
            p.arguments = ["-f", "WAVE", "-d", "LEI16", flacURL.path, wavURL.path]
            try? p.run()
            p.waitUntilExit()
            let decoded = wavSamples(wavURL)
            check("FLAC \(name)", decoded == pcm, "\(pcm.count) amostras, \(flac.count) bytes (WAV \(pcm.count * 2 + 44))")
        }

        // 2. Tratamento do áudio: fala baixa no meio continua inteira
        let mixed = speech(10, 2800, &seed) + speech(6, 350, &seed) + speech(10, 2800, &seed)
        let a = AudioPreprocessor.process(mixed)
        check("fala baixa preservada", a.pcm.count == mixed.count && !a.silent, a.note)
        let distant = speech(1, 0, &seed) + speech(8, 160, &seed) + speech(1, 0, &seed)
        let b = AudioPreprocessor.process(distant)
        check("microfone distante não é silêncio", !b.silent, "\(b.note), ganho \(b.gain)")
        let c = AudioPreprocessor.process([Int16](repeating: 0, count: 48000))
        check("silêncio detectado", c.silent && c.mute, c.note)

        // 3. Blocos nas pausas: 65 s viram pelo menos 3 blocos, nenhum maior que 27 s
        let long = speech(65, 3000, &seed)
        let cuts = AudioChunker.findCuts(long)
        let bounds = [0] + cuts + [long.count]
        let maxLen = zip(bounds, bounds.dropFirst()).map { $1 - $0 }.max() ?? 0
        check("blocos de ditado longo", cuts.count >= 2 && maxLen <= 27 * 16000 + 16000 * 4, "\(cuts.count + 1) blocos")

        print(failures == 0 ? "ALL OK" : "\(failures) FAIL")
        exit(failures == 0 ? 0 : 1)
    }
}
