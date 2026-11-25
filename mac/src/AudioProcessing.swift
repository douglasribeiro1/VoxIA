import Foundation

/// Áudio do VoxIA para Mac: PCM 16 bits mono a 16 kHz, a mesma lógica das versões Windows e Android
/// (AudioPreprocessor, AudioChunker e FlacEncoder de lá).
public enum AudioFormat16k {
    public static let sampleRate = 16000
}

// MARK: - Tratamento do áudio

/// Filtro passa-alta e ganho para fala baixa. O áudio NUNCA é recortado: cortar silêncios pelo
/// volume apagava trechos de fala baixa (microfone longe da boca) nas outras versões.
public enum AudioPreprocessor {
    public struct Result {
        public var pcm: [Int16]
        public var changed: Bool
        public var gain: Double
        public var note: String
        /// Sem fala nenhuma: não deve ser enviado ao Whisper (ele inventaria palavras).
        public var silent: Bool
        /// Praticamente mudo (nem ruído de ambiente).
        public var mute: Bool
        /// Quadros de 20 ms com fala; -1 = não medido.
        public var speechFrames: Int
    }

    private static let frameMs = 20
    private static let highPassHz = 80.0
    private static let targetRms = 3000.0
    private static let quietRms = 2200.0
    private static let maxGain = 4.0
    private static let maxPeak = 30000.0
    private static let minSpeechRms = 60.0   // baixo de propósito: microfone longe ainda é fala
    private static let minSpeechFrames = 12  // 240 ms de fala no mínimo

    public static func process(_ pcm: [Int16], sampleRate: Int = AudioFormat16k.sampleRate) -> Result {
        func untouched(_ note: String, silent: Bool = false, mute: Bool = false, frames: Int = -1) -> Result {
            return Result(pcm: pcm, changed: false, gain: 1, note: note, silent: silent, mute: mute, speechFrames: frames)
        }
        let n = pcm.count
        if n < 2 || sampleRate < 8000 { return untouched("curto demais") }
        if Double(n) / Double(sampleRate) < 0.6 { return untouched("menos de 0,6 s") }

        // 1. Filtro passa-alta de 1ª ordem (tira ronco e nível DC)
        var x = [Double](repeating: 0, count: n)
        let rc = 1.0 / (2.0 * Double.pi * highPassHz)
        let dt = 1.0 / Double(sampleRate)
        let a = rc / (rc + dt)
        var prevIn = 0.0, prevOut = 0.0
        for i in 0..<n {
            let s = Double(pcm[i])
            let y = a * (prevOut + s - prevIn)
            prevIn = s
            prevOut = y
            x[i] = y
        }

        // 2. Energia por quadro
        let frameLen = sampleRate * frameMs / 1000
        let frames = (n + frameLen - 1) / frameLen
        var rms = [Double](repeating: 0, count: frames)
        for f in 0..<frames {
            let s0 = f * frameLen, s1 = min(n, s0 + frameLen)
            var sum = 0.0
            for i in s0..<s1 { sum += x[i] * x[i] }
            rms[f] = (sum / Double(max(1, s1 - s0))).squareRoot()
        }
        let sorted = rms.sorted()
        let noise = sorted[Int(Double(frames) * 0.10)]
        let loud = sorted[min(frames - 1, Int(Double(frames) * 0.90))]
        if loud < 60 { return untouched("gravação praticamente muda", silent: true, mute: true) }

        let speechLevel = max(minSpeechRms, noise * 3)
        let speechFrames = rms.filter { $0 > speechLevel }.count
        if speechFrames < minSpeechFrames { return untouched("nenhuma fala detectada", silent: true, frames: speechFrames) }

        // 3. Ganho para gravações baixas (nunca atenua), medido nos quadros de fala
        var thr = max(noise * 3, 100)
        thr = min(thr, loud * 0.25)
        thr = max(thr, 60)
        var sumSq = 0.0, cnt = 0.0, peak = 0.0
        for f in 0..<frames where rms[f] > thr {
            let s0 = f * frameLen, s1 = min(n, s0 + frameLen)
            for i in s0..<s1 {
                sumSq += x[i] * x[i]
                cnt += 1
                peak = max(peak, abs(x[i]))
            }
        }
        let speechRms = cnt > 0 ? (sumSq / cnt).squareRoot() : 0
        var gain = 1.0
        if speechRms > 0 && speechRms < quietRms {
            gain = min(maxGain, targetRms / speechRms)
            if peak * gain > maxPeak { gain = maxPeak / peak }
            if gain < 1.05 { gain = 1 }
        }

        // 4. Saída: o áudio inteiro, só filtrado e com ganho
        var out = [Int16](repeating: 0, count: n)
        for i in 0..<n {
            let v = min(32767, max(-32768, (x[i] * gain).rounded()))
            out[i] = Int16(v)
        }
        return Result(pcm: out, changed: true, gain: gain,
                      note: String(format: "ruído %.0f, fala rms %.0f", noise, speechRms),
                      silent: false, mute: false, speechFrames: speechFrames)
    }
}

// MARK: - Blocos nas pausas

/// Ditados longos são divididos nas pausas em blocos menores que a janela de 30 s do Whisper
/// (ele às vezes pula uma frase inteira num arquivo longo).
public enum AudioChunker {
    static let splitAboveSeconds = 30.0
    static let minChunkSeconds = 15.0
    static let maxChunkSeconds = 27.0
    static let minTailSeconds = 4.0
    static let frameMs = 20
    static let quietWindowFrames = 10

    /// Na transcrição durante a fala, um bloco só é cortado com áudio suficiente para escolher a pausa.
    public static let liveWindowSeconds = maxChunkSeconds + minTailSeconds

    /// Posições (em amostras) onde cortar. Vazio = não dividir.
    public static func findCuts(_ pcm: [Int16], sampleRate: Int = AudioFormat16k.sampleRate) -> [Int] {
        var cuts: [Int] = []
        let n = pcm.count
        if sampleRate < 8000 || Double(n) < Double(sampleRate) * splitAboveSeconds { return cuts }
        let frameLen = sampleRate * frameMs / 1000
        let frames = n / frameLen
        var rms = [Double](repeating: 0, count: frames)
        for f in 0..<frames {
            var sum = 0.0
            let s0 = f * frameLen
            for i in s0..<(s0 + frameLen) {
                let s = Double(pcm[i])
                sum += s * s
            }
            rms[f] = (sum / Double(frameLen)).squareRoot()
        }
        let framesPerSec = 1000 / frameMs
        let minF = Int(minChunkSeconds * Double(framesPerSec))
        let maxF = Int(maxChunkSeconds * Double(framesPerSec))
        let tailF = Int(minTailSeconds * Double(framesPerSec))
        var start = 0
        while frames - start > maxF {
            let lo = start + minF
            let hi = min(start + maxF, frames - tailF) - quietWindowFrames
            if hi <= lo { break }
            var windowSum = 0.0
            for k in lo..<(lo + quietWindowFrames) { windowSum += rms[k] }
            var best = windowSum
            var bestAt = lo
            if lo + 1 <= hi {
                for w in (lo + 1)...hi {
                    windowSum += rms[w + quietWindowFrames - 1] - rms[w - 1]
                    if windowSum <= best { best = windowSum; bestAt = w }
                }
            }
            let cutFrame = bestAt + quietWindowFrames / 2
            cuts.append(cutFrame * frameLen)
            start = cutFrame
        }
        return cuts
    }

    /// Quadros de 20 ms com fala (bem acima do ruído de fundo do próprio trecho).
    public static func speechFrames(_ pcm: [Int16], sampleRate: Int = AudioFormat16k.sampleRate) -> Int {
        let frameLen = sampleRate * frameMs / 1000
        let frames = pcm.count / frameLen
        if frames <= 0 { return 0 }
        var rms = [Double](repeating: 0, count: frames)
        for f in 0..<frames {
            var sum = 0.0, sumSq = 0.0
            let s0 = f * frameLen
            for i in s0..<(s0 + frameLen) {
                let s = Double(pcm[i])
                sum += s
                sumSq += s * s
            }
            let mean = sum / Double(frameLen)
            rms[f] = max(0, sumSq / Double(frameLen) - mean * mean).squareRoot()
        }
        let noise = rms.sorted()[Int(Double(frames) * 0.10)]
        let threshold = max(60, noise * 3)
        return rms.filter { $0 > threshold }.count
    }
}

// MARK: - WAV

public enum WavFile {
    public static func data(_ pcm: [Int16], sampleRate: Int = AudioFormat16k.sampleRate) -> Data {
        var d = Data()
        func u32(_ v: UInt32) { var le = v.littleEndian; d.append(Data(bytes: &le, count: 4)) }
        func u16(_ v: UInt16) { var le = v.littleEndian; d.append(Data(bytes: &le, count: 2)) }
        let bytes = UInt32(pcm.count * 2)
        d.append("RIFF".data(using: .ascii)!); u32(36 + bytes); d.append("WAVE".data(using: .ascii)!)
        d.append("fmt ".data(using: .ascii)!); u32(16); u16(1); u16(1)
        u32(UInt32(sampleRate)); u32(UInt32(sampleRate * 2)); u16(2); u16(16)
        d.append("data".data(using: .ascii)!); u32(bytes)
        pcm.withUnsafeBufferPointer { buf in
            for s in buf { var le = s.littleEndian; d.append(Data(bytes: &le, count: 2)) }
        }
        return d
    }

    /// Amostras de um WAV gravado pelo VoxIA (cabeçalho de 44 bytes, 16 bits mono).
    public static func read(_ url: URL) -> [Int16]? {
        guard let d = try? Data(contentsOf: url), d.count >= 44 else { return nil }
        let count = (d.count - 44) / 2
        var out = [Int16](repeating: 0, count: count)
        d.withUnsafeBytes { raw in
            for i in 0..<count {
                let lo = UInt16(raw[44 + 2 * i]), hi = UInt16(raw[45 + 2 * i])
                out[i] = Int16(bitPattern: lo | (hi << 8))
            }
        }
        return out
    }
}

// MARK: - FLAC sem perdas

/// Codificador FLAC (mono, 16 bits): preditores fixos de ordem 0 a 4 com resíduo em código de Rice
/// particionado. As amostras saem idênticas; só o arquivo fica menor e o envio mais rápido.
public enum FlacEncoder {
    private static let blockSize = 4096
    private static let maxPartitionOrder = 6
    private static let maxRiceParam = 14

    public static func encode(_ samples: [Int16], sampleRate: Int = AudioFormat16k.sampleRate) -> Data {
        let x = samples.map { Int($0) }
        var out = Data("fLaC".utf8)
        out.append(streamInfo(total: x.count, sampleRate: sampleRate))
        var offset = 0, frameNumber = 0
        while offset < x.count {
            let size = min(blockSize, x.count - offset)
            out.append(frame(x, offset: offset, size: size, sampleRate: sampleRate, frameNumber: frameNumber))
            frameNumber += 1
            offset += size
        }
        return out
    }

    private static func streamInfo(total: Int, sampleRate: Int) -> Data {
        var w = BitWriter()
        w.write(1, 1); w.write(0, 7); w.write(34, 24)
        w.write(UInt64(blockSize), 16); w.write(UInt64(blockSize), 16)
        w.write(0, 24); w.write(0, 24)
        w.write(UInt64(sampleRate), 20); w.write(0, 3); w.write(15, 5)
        w.write(UInt64(total), 36)
        for _ in 0..<16 { w.write(0, 8) }
        return Data(w.bytes())
    }

    private static func frame(_ x: [Int], offset: Int, size: Int, sampleRate: Int, frameNumber: Int) -> Data {
        var w = BitWriter()
        w.write(0xFFF8, 16)
        let bsCode: UInt64 = size == blockSize ? 12 : (size <= 256 ? 6 : 7)
        let srCode: UInt64
        switch sampleRate {
        case 8000: srCode = 4
        case 16000: srCode = 5
        case 22050: srCode = 6
        case 24000: srCode = 7
        case 32000: srCode = 8
        case 44100: srCode = 9
        case 48000: srCode = 10
        default: srCode = 0
        }
        w.write(bsCode, 4); w.write(srCode, 4); w.write(0, 4); w.write(4, 3); w.write(0, 1)
        writeUtf8(&w, UInt64(frameNumber))
        if bsCode == 6 { w.write(UInt64(size - 1), 8) }
        if bsCode == 7 { w.write(UInt64(size - 1), 16) }
        w.write(UInt64(crc8(w.bytes())), 8)
        writeSubframe(&w, x, offset: offset, size: size)
        w.alignToByte()
        var bytes = w.bytes()
        let crc = crc16(bytes)
        bytes.append(UInt8((crc >> 8) & 0xFF))
        bytes.append(UInt8(crc & 0xFF))
        return Data(bytes)
    }

    private struct RicePlan {
        var partitionOrder: Int
        var params: [Int]
        var bits: Int
    }

    private static func writeSubframe(_ w: inout BitWriter, _ x: [Int], offset: Int, size: Int) {
        var bestOrder = -1
        var bestBits = 16 * size + 8
        var bestResidual: [Int] = []
        var bestPlan: RicePlan? = nil
        for order in 0...4 {
            if size <= order { break }
            let res = residual(x, offset: offset, size: size, order: order)
            let plan = planRice(res, size: size, order: order)
            let bits = 8 + 16 * order + plan.bits
            if bits < bestBits {
                bestBits = bits; bestOrder = order; bestResidual = res; bestPlan = plan
            }
        }
        guard bestOrder >= 0, let plan = bestPlan else {
            w.write(0, 1); w.write(1, 6); w.write(0, 1) // VERBATIM
            for i in 0..<size { w.writeSigned(x[offset + i], 16) }
            return
        }
        w.write(0, 1); w.write(UInt64(8 + bestOrder), 6); w.write(0, 1) // FIXED
        for i in 0..<bestOrder { w.writeSigned(x[offset + i], 16) }
        w.write(0, 2)
        w.write(UInt64(plan.partitionOrder), 4)
        let partitions = 1 << plan.partitionOrder
        let perPartition = size >> plan.partitionOrder
        var idx = 0
        for p in 0..<partitions {
            let count = p == 0 ? perPartition - bestOrder : perPartition
            let k = plan.params[p]
            w.write(UInt64(k), 4)
            for _ in 0..<count {
                w.writeRice(bestResidual[idx], k)
                idx += 1
            }
        }
    }

    private static func residual(_ x: [Int], offset o: Int, size: Int, order: Int) -> [Int] {
        var r = [Int](repeating: 0, count: size - order)
        for i in order..<size {
            let s = o + i
            switch order {
            case 0: r[i - order] = x[s]
            case 1: r[i - order] = x[s] - x[s - 1]
            case 2: r[i - order] = x[s] - 2 * x[s - 1] + x[s - 2]
            case 3: r[i - order] = x[s] - 3 * x[s - 1] + 3 * x[s - 2] - x[s - 3]
            default: r[i - order] = x[s] - 4 * x[s - 1] + 6 * x[s - 2] - 4 * x[s - 3] + x[s - 4]
            }
        }
        return r
    }

    private static func planRice(_ res: [Int], size: Int, order: Int) -> RicePlan {
        var best: RicePlan? = nil
        for po in 0...maxPartitionOrder {
            if size % (1 << po) != 0 { break }
            let perPartition = size >> po
            if perPartition <= order { break }
            let partitions = 1 << po
            var params = [Int](repeating: 0, count: partitions)
            var bits = 6
            var idx = 0
            for p in 0..<partitions {
                let count = p == 0 ? perPartition - order : perPartition
                var sum = 0
                for j in 0..<count { sum += zigzag(res[idx + j]) }
                let (k, cost) = bestParam(res, start: idx, count: count, sum: sum)
                params[p] = k
                bits += 4 + cost
                idx += count
            }
            if best == nil || bits < best!.bits { best = RicePlan(partitionOrder: po, params: params, bits: bits) }
        }
        return best!
    }

    private static func bestParam(_ res: [Int], start: Int, count: Int, sum: Int) -> (Int, Int) {
        if count == 0 { return (0, 0) }
        let mean = sum / count
        var guess = 0
        while guess < maxRiceParam && (1 << (guess + 1)) <= mean { guess += 1 }
        var bestK = guess
        var bestCost = Int.max
        for k in max(0, guess - 1)...min(maxRiceParam, guess + 1) {
            var cost = 0
            for j in 0..<count { cost += (zigzag(res[start + j]) >> k) + 1 + k }
            if cost < bestCost { bestCost = cost; bestK = k }
        }
        return (bestK, bestCost)
    }

    private static func zigzag(_ v: Int) -> Int { return v >= 0 ? 2 * v : -2 * v - 1 }

    private static func writeUtf8(_ w: inout BitWriter, _ v: UInt64) {
        if v < 0x80 {
            w.write(v, 8)
        } else if v < 0x800 {
            w.write(0xC0 | (v >> 6), 8); w.write(0x80 | (v & 0x3F), 8)
        } else if v < 0x10000 {
            w.write(0xE0 | (v >> 12), 8); w.write(0x80 | ((v >> 6) & 0x3F), 8); w.write(0x80 | (v & 0x3F), 8)
        } else {
            w.write(0xF0 | (v >> 18), 8); w.write(0x80 | ((v >> 12) & 0x3F), 8)
            w.write(0x80 | ((v >> 6) & 0x3F), 8); w.write(0x80 | (v & 0x3F), 8)
        }
    }

    private static func crc8(_ data: [UInt8]) -> Int {
        var crc = 0
        for b in data {
            crc ^= Int(b)
            for _ in 0..<8 { crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF }
        }
        return crc
    }

    private static func crc16(_ data: [UInt8]) -> Int {
        var crc = 0
        for b in data {
            crc ^= Int(b) << 8
            for _ in 0..<8 { crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x8005) & 0xFFFF : (crc << 1) & 0xFFFF }
        }
        return crc
    }

    private struct BitWriter {
        private var out: [UInt8] = []
        private var acc: UInt64 = 0
        private var nbits = 0

        mutating func write(_ value: UInt64, _ bits: Int) {
            var remaining = bits
            while remaining > 0 {
                let take = min(remaining, 32)
                remaining -= take
                let chunk = (value >> UInt64(remaining)) & ((UInt64(1) << UInt64(take)) - 1)
                acc = (acc << UInt64(take)) | chunk
                nbits += take
                while nbits >= 8 {
                    nbits -= 8
                    out.append(UInt8((acc >> UInt64(nbits)) & 0xFF))
                }
                acc &= (UInt64(1) << UInt64(nbits)) - 1
            }
        }

        mutating func writeSigned(_ value: Int, _ bits: Int) {
            write(UInt64(bitPattern: Int64(value)) & ((UInt64(1) << UInt64(bits)) - 1), bits)
        }

        mutating func writeRice(_ value: Int, _ k: Int) {
            let u = UInt64(value >= 0 ? 2 * value : -2 * value - 1)
            var q = u >> UInt64(k)
            while q >= 32 { write(0, 32); q -= 32 }
            write(1, Int(q) + 1)
            if k > 0 { write(u & ((UInt64(1) << UInt64(k)) - 1), k) }
        }

        mutating func alignToByte() { if nbits > 0 { write(0, 8 - nbits) } }

        func bytes() -> [UInt8] {
            if nbits == 0 { return out }
            return out + [UInt8((acc << UInt64(8 - nbits)) & 0xFF)]
        }
    }
}
