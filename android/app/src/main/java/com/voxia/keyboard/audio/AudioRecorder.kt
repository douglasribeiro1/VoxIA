package com.voxia.keyboard.audio

import android.content.Context
import android.media.MediaRecorder
import android.os.Build
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.io.File

class AudioRecorder(private val context: Context) {
    @Volatile private var recorder: MediaRecorder? = null
    private var currentOutputFile: File? = null
    @Volatile private var isRecording = false
    private var recordingStartTime = 0L

    fun startRecording(): Boolean {
        if (isRecording) return false

        return try {
            val audioDir = File(context.cacheDir, "audio_recordings")
            if (!audioDir.exists()) audioDir.mkdirs()

            currentOutputFile = File(audioDir, "voxia_dictation_${System.currentTimeMillis()}.m4a")

            recorder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                MediaRecorder(context)
            } else {
                @Suppress("DEPRECATION")
                MediaRecorder()
            }.apply {
                setAudioSource(MediaRecorder.AudioSource.MIC)
                setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
                setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
                setAudioSamplingRate(16000)
                setAudioEncodingBitRate(64000)
                setOutputFile(currentOutputFile!!.absolutePath)
                prepare()
                start()
            }

            recordingStartTime = System.currentTimeMillis()
            isRecording = true
            true
        } catch (e: Exception) {
            Log.e("AudioRecorder", "Erro ao iniciar gravação", e)
            cleanup()
            false
        }
    }

    suspend fun stopRecording(): File? = withContext(Dispatchers.IO) {
        if (!isRecording || recorder == null) return@withContext null

        val elapsed = System.currentTimeMillis() - recordingStartTime
        if (elapsed < 600) {
            // Pequena pausa para evitar o erro stop failed do MediaRecorder em gravações ultracurtas
            delay(600 - elapsed)
        }

        try {
            recorder?.apply {
                try {
                    stop()
                } catch (e: Exception) {
                    Log.w("AudioRecorder", "Aviso no stop do MediaRecorder: ${e.message}")
                }
                release()
            }
            recorder = null
            isRecording = false

            val file = currentOutputFile
            if (file != null && file.exists() && file.length() > 200) {
                file
            } else {
                Log.w("AudioRecorder", "Arquivo de áudio muito curto ou vazio")
                null
            }
        } catch (e: Exception) {
            Log.e("AudioRecorder", "Erro ao parar gravação", e)
            cleanup()
            null
        }
    }

    fun cancelRecording() {
        cleanup()
    }

    fun isRecordingNow(): Boolean = isRecording

    /** Nível do microfone desde a última chamada, de 0.0 a 1.0. */
    fun currentLevel(): Float {
        val amplitude = try {
            if (isRecording) recorder?.maxAmplitude ?: 0 else 0
        } catch (ignored: Exception) {
            0
        }
        return (amplitude / 18000f).coerceIn(0f, 1f)
    }

    private fun cleanup() {
        try {
            recorder?.release()
        } catch (ignored: Exception) {}
        recorder = null
        isRecording = false
        try {
            currentOutputFile?.delete()
        } catch (ignored: Exception) {}
        currentOutputFile = null
    }
}