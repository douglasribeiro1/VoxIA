package com.voxia.keyboard.audio

import android.content.Context
import android.media.MediaRecorder
import android.os.Build
import android.util.Log
import java.io.File

class AudioRecorder(private val context: Context) {
    private var recorder: MediaRecorder? = null
    private var currentOutputFile: File? = null
    private var isRecording = false

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

            isRecording = true
            true
        } catch (e: Exception) {
            Log.e("AudioRecorder", "Erro ao iniciar gravação", e)
            cleanup()
            false
        }
    }

    fun stopRecording(): File? {
        if (!isRecording || recorder == null) return null

        return try {
            recorder?.apply {
                stop()
                release()
            }
            recorder = null
            isRecording = false
            currentOutputFile
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
