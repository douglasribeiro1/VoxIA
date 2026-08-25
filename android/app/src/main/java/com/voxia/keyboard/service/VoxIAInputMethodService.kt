package com.voxia.keyboard.service

import android.content.Context
import android.content.Intent
import android.inputmethodservice.InputMethodService
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.ImageButton
import android.widget.Spinner
import android.widget.TextView
import com.voxia.keyboard.R
import com.voxia.keyboard.api.GroqClient
import com.voxia.keyboard.api.SupabaseClient
import com.voxia.keyboard.audio.AudioRecorder
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.data.PromptManager
import com.voxia.keyboard.data.PromptProfile
import com.voxia.keyboard.ui.SettingsActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class VoxIAInputMethodService : InputMethodService() {
    private val serviceScope = CoroutineScope(Dispatchers.Main + Job())
    
    private lateinit var audioRecorder: AudioRecorder
    private lateinit var groqClient: GroqClient
    private lateinit var promptManager: PromptManager
    private lateinit var dictionaryManager: DictionaryManager
    private lateinit var supabaseClient: SupabaseClient

    // UI Elements
    private var rootView: View? = null
    private var btnMicRecord: ImageButton? = null
    private var tvStatusMessage: TextView? = null
    private var spinnerPromptProfile: Spinner? = null
    private var btnSwitchKeyboard: ImageButton? = null
    private var btnBackspace: ImageButton? = null
    private var btnEnter: ImageButton? = null
    private var btnSpace: Button? = null
    private var btnComma: Button? = null
    private var btnDot: Button? = null
    private var btnOpenSettings: ImageButton? = null

    private var promptProfilesList: List<PromptProfile> = emptyList()

    override fun onCreate() {
        super.onCreate()
        audioRecorder = AudioRecorder(this)
        groqClient = GroqClient()
        promptManager = PromptManager(this)
        dictionaryManager = DictionaryManager(this)
        supabaseClient = SupabaseClient(this)
    }

    override fun onCreateInputView(): View {
        val view = layoutInflater.inflate(R.layout.keyboard_view, null)
        rootView = view

        btnMicRecord = view.findViewById(R.id.btnMicRecord)
        tvStatusMessage = view.findViewById(R.id.tvStatusMessage)
        spinnerPromptProfile = view.findViewById(R.id.spinnerPromptProfile)
        btnSwitchKeyboard = view.findViewById(R.id.btnSwitchKeyboard)
        btnBackspace = view.findViewById(R.id.btnBackspace)
        btnEnter = view.findViewById(R.id.btnEnter)
        btnSpace = view.findViewById(R.id.btnSpace)
        btnComma = view.findViewById(R.id.btnComma)
        btnDot = view.findViewById(R.id.btnDot)
        btnOpenSettings = view.findViewById(R.id.btnOpenSettings)

        setupPromptSpinner()
        setupListeners()

        return view
    }

    override fun onStartInputView(info: android.view.inputmethod.EditorInfo?, restarting: Boolean) {
        super.onStartInputView(info, restarting)
        setupPromptSpinner()
        tvStatusMessage?.text = getString(R.string.status_ready)
        btnMicRecord?.setBackgroundResource(R.drawable.bg_mic_button)
        btnMicRecord?.setImageResource(R.drawable.ic_mic)

        // Sincronização silenciosa em segundo plano se estiver logado
        if (supabaseClient.isLoggedIn()) {
            serviceScope.launch {
                supabaseClient.syncFromCloud()
                setupPromptSpinner()
            }
        }
    }

    private fun setupPromptSpinner() {
        promptProfilesList = promptManager.getPrompts()
        val adapter = ArrayAdapter(
            this,
            android.R.layout.simple_spinner_dropdown_item,
            promptProfilesList.map { it.title }
        )
        spinnerPromptProfile?.adapter = adapter

        val activeId = promptManager.getActivePromptId()
        val activeIndex = promptProfilesList.indexOfFirst { it.id == activeId }
        if (activeIndex >= 0) {
            spinnerPromptProfile?.setSelection(activeIndex)
        }

        spinnerPromptProfile?.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) {
                if (position in promptProfilesList.indices) {
                    promptManager.setActivePromptId(promptProfilesList[position].id)
                }
            }
            override fun onNothingSelected(parent: AdapterView<*>?) {}
        }
    }

    private fun setupListeners() {
        btnMicRecord?.setOnClickListener {
            handleMicToggle()
        }

        btnSwitchKeyboard?.setOnClickListener {
            vibrateFeedback()
            val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                if (!switchToPreviousInputMethod()) {
                    imm.showInputMethodPicker()
                }
            } else {
                imm.showInputMethodPicker()
            }
        }

        btnBackspace?.setOnClickListener {
            vibrateFeedback()
            sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL)
        }

        btnEnter?.setOnClickListener {
            vibrateFeedback()
            sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER)
        }

        btnSpace?.setOnClickListener {
            vibrateFeedback()
            currentInputConnection?.commitText(" ", 1)
        }

        btnComma?.setOnClickListener {
            vibrateFeedback()
            currentInputConnection?.commitText(", ", 1)
        }

        btnDot?.setOnClickListener {
            vibrateFeedback()
            currentInputConnection?.commitText(". ", 1)
        }

        btnOpenSettings?.setOnClickListener {
            vibrateFeedback()
            val intent = Intent(this, SettingsActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK
            }
            startActivity(intent)
        }
    }

    private fun handleMicToggle() {
        vibrateFeedback()
        val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
        val apiKey = prefs.getString("groq_api_key", "") ?: ""

        if (apiKey.isBlank()) {
            tvStatusMessage?.text = "⚠ Configure sua Groq API Key nas Configurações!"
            val intent = Intent(this, SettingsActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK
            }
            startActivity(intent)
            return
        }

        if (!audioRecorder.isRecordingNow()) {
            // Iniciar Gravação
            val started = audioRecorder.startRecording()
            if (started) {
                btnMicRecord?.setBackgroundResource(R.drawable.bg_mic_recording)
                btnMicRecord?.setImageResource(R.drawable.ic_stop)
                tvStatusMessage?.text = getString(R.string.status_recording)
            } else {
                tvStatusMessage?.text = "Erro ao acessar o microfone."
            }
        } else {
            // Encerrar Gravação e Processar
            val audioFile = audioRecorder.stopRecording()
            btnMicRecord?.setBackgroundResource(R.drawable.bg_mic_button)
            btnMicRecord?.setImageResource(R.drawable.ic_mic)

            if (audioFile == null || !audioFile.exists()) {
                tvStatusMessage?.text = "Erro: gravação de áudio vazia."
                return
            }

            tvStatusMessage?.text = getString(R.string.status_transcribing)

            serviceScope.launch {
                val whisperTerms = dictionaryManager.getWhisperPromptTerms()
                val transcriptionResult = groqClient.transcribeAudio(
                    audioFile = audioFile,
                    apiKey = apiKey,
                    initialPrompt = whisperTerms
                )

                transcriptionResult.onSuccess { rawTranscript ->
                    if (rawTranscript.isBlank()) {
                        withContext(Dispatchers.Main) {
                            tvStatusMessage?.text = "Nenhuma fala detectada."
                            resetStatusAfterDelay()
                        }
                    } else {
                        withContext(Dispatchers.Main) {
                            tvStatusMessage?.text = getString(R.string.status_correcting)
                        }

                        val activePrompt = promptManager.getActivePrompt()
                        val correctedResult = groqClient.correctText(
                            rawText = rawTranscript,
                            apiKey = apiKey,
                            systemPrompt = activePrompt.promptText
                        )

                        val finalRawText = correctedResult.getOrDefault(rawTranscript)
                        // Aplica regras de dicionário local
                        val finalText = dictionaryManager.applyReplacements(finalRawText)

                        withContext(Dispatchers.Main) {
                            // Insere no campo de texto ativo
                            currentInputConnection?.commitText(finalText + " ", 1)
                            tvStatusMessage?.text = getString(R.string.status_done)
                            resetStatusAfterDelay()
                        }
                    }
                }.onFailure { error ->
                    withContext(Dispatchers.Main) {
                        tvStatusMessage?.text = "Erro: ${error.message}"
                        resetStatusAfterDelay()
                    }
                }

                // Deleta arquivo temporário
                try { audioFile.delete() } catch (ignored: Exception) {}
            }
        }
    }

    private fun resetStatusAfterDelay() {
        serviceScope.launch {
            delay(2500)
            if (!audioRecorder.isRecordingNow()) {
                tvStatusMessage?.text = getString(R.string.status_ready)
            }
        }
    }

    private fun vibrateFeedback() {
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                val vibratorManager = getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager
                vibratorManager?.defaultVibrator?.vibrate(VibrationEffect.createOneShot(35, VibrationEffect.DEFAULT_AMPLITUDE))
            } else {
                @Suppress("DEPRECATION")
                val vibrator = getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
                vibrator?.vibrate(35)
            }
        } catch (ignored: Exception) {}
    }

    override fun onDestroy() {
        super.onDestroy()
        audioRecorder.cancelRecording()
    }
}
