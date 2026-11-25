package com.voxia.keyboard.audio

import android.annotation.SuppressLint
import android.content.Context
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.io.File
import java.io.RandomAccessFile
import kotlin.math.abs

/**
 * Grava 16 kHz mono 16 bits direto em WAV. O PCM sem compressão permite dividir ditados
 * longos nas pausas antes de enviar ao Whisper (ver AudioChunker) e ler o que já foi gravado
 * enquanto a gravação continua (transcrição durante a fala, ver LiveTranscriber).
 *
 * A fonte preferida é VOICE_RECOGNITION: o Android entrega o som do microfone ajustado para
 * reconhecimento de fala (resposta plana, sem o processamento de chamada telefônica que abafa
 * consoantes). Se o aparelho não aceitar, volta para MIC.
 */
class AudioRecorder(private val context: Context) {
    @Volatile private var record: AudioRecord? = null
    @Volatile private var isRecording = false
    @Volatile private var lastPeak = 0
    @Volatile private var anyNonZeroSample = false
    private var readerThread: Thread? = null
    private var output: RandomAccessFile? = null
    private var currentOutputFile: File? = null
    @Volatile private var dataBytes = 0L
    private var recordingStartTime = 0L

    @SuppressLint("MissingPermission") // a permissão é pedida na tela de configurações
    fun startRecording(): Boolean {
        if (isRecording) return false

        return try {
            val audioDir = File(context.cacheDir, "audio_recordings")
            if (!audioDir.exists()) audioDir.mkdirs()
            val file = File(audioDir, "voxia_dictation_${System.currentTimeMillis()}.wav")

            val minBuf = AudioRecord.getMinBufferSize(SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
            val bufSize = maxOf(minBuf, SAMPLE_RATE / 5 * 2) // pelo menos 200 ms
            val rec = openRecord(MediaRecorder.AudioSource.VOICE_RECOGNITION, bufSize)
                ?: openRecord(MediaRecorder.AudioSource.MIC, bufSize)
            if (rec == null) {
                Log.e("AudioRecorder", "AudioRecord não inicializou (permissão de microfone?)")
                return false
            }

            val raf = RandomAccessFile(file, "rw")
            raf.setLength(0)
            raf.write(ByteArray(WAV_HEADER_SIZE)) // cabeçalho é preenchido no fim
            output = raf
            currentOutputFile = file
            dataBytes = 0
            lastPeak = 0
            anyNonZeroSample = false

            rec.startRecording()
            record = rec
            isRecording = true
            recordingStartTime = System.currentTimeMillis()

            readerThread = Thread({ readLoop(rec, raf, bufSize) }, "VoxIA-AudioRecord").apply { start() }
            true
        } catch (e: Exception) {
            Log.e("AudioRecorder", "Erro ao iniciar gravação", e)
            cleanup(deleteFile = true)
            false
        }
    }

    @SuppressLint("MissingPermission")
    private fun openRecord(source: Int, bufSize: Int): AudioRecord? = try {
        val rec = AudioRecord(source, SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, bufSize)
        if (rec.state == AudioRecord.STATE_INITIALIZED) {
            Log.d("AudioRecorder", "Gravando com a fonte de áudio $source.")
            rec
        } else {
            rec.release()
            null
        }
    } catch (e: Exception) {
        Log.w("AudioRecorder", "Fonte de áudio $source indisponível: ${e.message}")
        null
    }

    /** Quantas amostras já foram gravadas no arquivo (gravação em andamento). */
    fun recordedSamples(): Int = if (isRecording) (dataBytes / 2).toInt() else 0

    /**
     * Lê do arquivo da gravação em andamento as amostras [fromSample, toSample) sem interromper
     * a gravação. Devolve null se não estiver gravando ou se a leitura falhar.
     */
    fun readRecorded(fromSample: Int, toSample: Int): ByteArray? {
        val file = currentOutputFile ?: return null
        if (!isRecording || toSample <= fromSample) return null
        return try {
            RandomAccessFile(file, "r").use { raf ->
                val bytes = ByteArray((toSample - fromSample) * 2)
                raf.seek(WAV_HEADER_SIZE + fromSample * 2L)
                raf.readFully(bytes)
                bytes
            }
        } catch (e: Exception) {
            Log.w("AudioRecorder", "Leitura durante a gravação falhou: ${e.message}")
            null
        }
    }

    private fun readLoop(rec: AudioRecord, raf: RandomAccessFile, bufSize: Int) {
        val buffer = ByteArray(bufSize)
        try {
            while (isRecording) {
                val n = rec.read(buffer, 0, buffer.size)
                if (n <= 0) continue
                raf.write(buffer, 0, n)
                dataBytes += n
                var peak = 0
                var i = 0
                while (i + 1 < n) {
                    val s = abs(((buffer[i].toInt() and 0xFF) or (buffer[i + 1].toInt() shl 8)).toShort().toInt())
                    if (s > peak) peak = s
                    i += 2
                }
                if (peak > 0) anyNonZeroSample = true
                lastPeak = peak
            }
        } catch (e: Exception) {
            Log.e("AudioRecorder", "Erro durante a gravação", e)
        }
    }

    suspend fun stopRecording(): File? = withContext(Dispatchers.IO) {
        if (!isRecording || record == null) return@withContext null

        val elapsed = System.currentTimeMillis() - recordingStartTime
        if (elapsed < 600) delay(600 - elapsed)

        isRecording = false
        try {
            record?.stop()
        } catch (e: Exception) {
            Log.w("AudioRecorder", "Aviso no stop do AudioRecord: ${e.message}")
        }
        readerThread?.join(1500)
        readerThread = null

        val file = currentOutputFile
        try {
            output?.let { writeWavHeader(it, dataBytes) }
        } catch (e: Exception) {
            Log.e("AudioRecorder", "Erro ao finalizar o WAV", e)
        }
        cleanup(deleteFile = false)

        if (file != null && file.exists() && file.length() > WAV_HEADER_SIZE + SAMPLE_RATE / 5) {
            file
        } else {
            Log.w("AudioRecorder", "Arquivo de áudio muito curto ou vazio")
            file?.delete()
            null
        }
    }

    fun cancelRecording() {
        isRecording = false
        try { record?.stop() } catch (ignored: Exception) {}
        readerThread?.join(500)
        readerThread = null
        cleanup(deleteFile = true)
    }

    fun isRecordingNow(): Boolean = isRecording

    /**
     * Verdadeiro quando a última gravação só teve zeros: é o que o Android entrega quando
     * bloqueia o microfone de um app em segundo plano.
     */
    fun lastRecordingWasMuted(): Boolean = !anyNonZeroSample

    /** Nível do microfone no último bloco lido, de 0.0 a 1.0. */
    fun currentLevel(): Float = if (isRecording) (lastPeak / 18000f).coerceIn(0f, 1f) else 0f

    private fun cleanup(deleteFile: Boolean) {
        try { record?.release() } catch (ignored: Exception) {}
        record = null
        isRecording = false
        try { output?.close() } catch (ignored: Exception) {}
        output = null
        if (deleteFile) {
            try { currentOutputFile?.delete() } catch (ignored: Exception) {}
        }
        currentOutputFile = null
    }

    companion object {
        const val SAMPLE_RATE = 16000
        const val WAV_HEADER_SIZE = 44

        fun writeWavHeader(raf: RandomAccessFile, pcmBytes: Long) {
            raf.seek(0)
            raf.write(buildWavHeader(pcmBytes, SAMPLE_RATE))
        }

        fun buildWavHeader(pcmBytes: Long, sampleRate: Int): ByteArray {
            val channels = 1
            val bits = 16
            val byteRate = sampleRate * channels * bits / 8
            val h = ByteArray(WAV_HEADER_SIZE)
            fun putStr(off: Int, s: String) = s.forEachIndexed { i, c -> h[off + i] = c.code.toByte() }
            fun putInt(off: Int, v: Long) { for (i in 0..3) h[off + i] = ((v shr (8 * i)) and 0xFF).toByte() }
            fun putShort(off: Int, v: Int) { h[off] = (v and 0xFF).toByte(); h[off + 1] = ((v shr 8) and 0xFF).toByte() }
            putStr(0, "RIFF"); putInt(4, 36 + pcmBytes); putStr(8, "WAVE")
            putStr(12, "fmt "); putInt(16, 16); putShort(20, 1); putShort(22, channels)
            putInt(24, sampleRate.toLong()); putInt(28, byteRate.toLong()); putShort(32, channels * bits / 8); putShort(34, bits)
            putStr(36, "data"); putInt(40, pcmBytes)
            return h
        }
    }
}
