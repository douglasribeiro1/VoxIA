package com.voxia.keyboard.service

import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.inputmethodservice.InputMethodService
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.util.Log
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.ImageButton
import android.widget.LinearLayout
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import com.voxia.keyboard.R
import com.voxia.keyboard.api.GroqClient
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
import java.io.File

class VoxIAInputMethodService : InputMethodService() {
    private val serviceScope = CoroutineScope(Dispatchers.Main + Job())
    private var backspaceRepeatJob: Job? = null
    private var isProcessingAudio = false
    private var pendingAudioFile: File? = null

    private lateinit var audioRecorder: AudioRecorder
    private lateinit var groqClient: GroqClient
    private lateinit var promptManager: PromptManager
    private lateinit var dictionaryManager: DictionaryManager

    // Layout Containers
    private var layoutMainVoiceView: LinearLayout? = null
    private var layoutPromptEditor: LinearLayout? = null
    private var layoutDictionaryQuickAdd: LinearLayout? = null

    // Main Voice Views
    private var btnMicRecord: ImageButton? = null
    private var tvStatusMessage: TextView? = null
    private var btnRetryAudio: Button? = null
    private var spinnerPromptProfile: Spinner? = null
    private var btnEditPrompt: ImageButton? = null
    private var btnAddPrompt: ImageButton? = null
    private var btnAddDict: ImageButton? = null
    private var btnOpenSettings: ImageButton? = null
    private var btnSwitchKeyboard: ImageButton? = null
    private var btnBackspace: ImageButton? = null
    private var btnEnter: ImageButton? = null
    private var btnSend: ImageButton? = null
    private var btnSpace: Button? = null
    private var btnComma: Button? = null
    private var btnDot: Button? = null

    // Prompt Editor Views
    private var tvPromptEditorHeading: TextView? = null
    private var btnClosePromptEditor: ImageButton? = null
    private var etPromptTitle: EditText? = null
    private var etPromptInstructions: EditText? = null
    private var btnCancelPromptEditor: Button? = null
    private var btnDeletePrompt: Button? = null
    private var btnSavePrompt: Button? = null
    private var currentEditingPromptId: String? = null

    // Dictionary Quick Add Views
    private var btnCloseDictEditor: ImageButton? = null
    private var etDictOriginal: EditText? = null
    private var etDictReplacement: EditText? = null
    private var cbDictVocabOnly: CheckBox? = null
    private var btnCancelDictEditor: Button? = null
    private var btnSaveDictEntry: Button? = null

    private var promptProfilesList: List<PromptProfile> = emptyList()

    override fun onCreate() {
        super.onCreate()
        audioRecorder = AudioRecorder(this)
        groqClient = GroqClient()
        promptManager = PromptManager(this)
        dictionaryManager = DictionaryManager(this)
    }

    override fun onCreateInputView(): View {
        val view = layoutInflater.inflate(R.layout.keyboard_view, null)

        // 1. Containers
        layoutMainVoiceView = view.findViewById(R.id.layoutMainVoiceView)
        layoutPromptEditor = view.findViewById(R.id.layoutPromptEditor)
        layoutDictionaryQuickAdd = view.findViewById(R.id.layoutDictionaryQuickAdd)

        // 2. Main Voice Views
        btnMicRecord = view.findViewById(R.id.btnMicRecord)
        tvStatusMessage = view.findViewById(R.id.tvStatusMessage)
        btnRetryAudio = view.findViewById(R.id.btnRetryAudio)
        spinnerPromptProfile = view.findViewById(R.id.spinnerPromptProfile)
        btnEditPrompt = view.findViewById(R.id.btnEditPrompt)
        btnAddPrompt = view.findViewById(R.id.btnAddPrompt)
        btnAddDict = view.findViewById(R.id.btnAddDict)
        btnOpenSettings = view.findViewById(R.id.btnOpenSettings)
        btnSwitchKeyboard = view.findViewById(R.id.btnSwitchKeyboard)
        btnBackspace = view.findViewById(R.id.btnBackspace)
        btnEnter = view.findViewById(R.id.btnEnter)
        btnSend = view.findViewById(R.id.btnSend)
        btnSpace = view.findViewById(R.id.btnSpace)
        btnComma = view.findViewById(R.id.btnComma)
        btnDot = view.findViewById(R.id.btnDot)

        // 3. Prompt Editor Views
        tvPromptEditorHeading = view.findViewById(R.id.tvPromptEditorHeading)
        btnClosePromptEditor = view.findViewById(R.id.btnClosePromptEditor)
        etPromptTitle = view.findViewById(R.id.etPromptTitle)
        etPromptInstructions = view.findViewById(R.id.etPromptInstructions)
        btnCancelPromptEditor = view.findViewById(R.id.btnCancelPromptEditor)
        btnDeletePrompt = view.findViewById(R.id.btnDeletePrompt)
        btnSavePrompt = view.findViewById(R.id.btnSavePrompt)

        // 4. Dictionary Views
        btnCloseDictEditor = view.findViewById(R.id.btnCloseDictEditor)
        etDictOriginal = view.findViewById(R.id.etDictOriginal)
        etDictReplacement = view.findViewById(R.id.etDictReplacement)
        cbDictVocabOnly = view.findViewById(R.id.cbDictVocabOnly)
        btnCancelDictEditor = view.findViewById(R.id.btnCancelDictEditor)
        btnSaveDictEntry = view.findViewById(R.id.btnSaveDictEntry)

        setupPromptSpinner()
        setupListeners()

        return view
    }

    override fun onStartInputView(info: EditorInfo?, restarting: Boolean) {
        super.onStartInputView(info, restarting)
        showMainVoiceView()
        setupPromptSpinner()
        if (pendingAudioFile != null && pendingAudioFile!!.exists()) {
            btnRetryAudio?.visibility = View.VISIBLE
            tvStatusMessage?.text = "Áudio gravado guardado. Toque para reenviar."
        } else {
            btnRetryAudio?.visibility = View.GONE
            tvStatusMessage?.text = getString(R.string.status_ready)
        }
        btnMicRecord?.setBackgroundResource(R.drawable.bg_mic_button)
        btnMicRecord?.setImageResource(R.drawable.ic_mic)
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

    @SuppressLint("ClickableViewAccessibility")
    private fun setupListeners() {
        btnMicRecord?.setOnClickListener {
            handleMicToggle()
        }

        // --- BOTÃO REENVIAR ÁUDIO PRESERVADO ---
        btnRetryAudio?.setOnClickListener {
            vibrateFeedback()
            val file = pendingAudioFile
            if (file != null && file.exists()) {
                processAudioFile(file)
            } else {
                btnRetryAudio?.visibility = View.GONE
                tvStatusMessage?.text = getString(R.string.status_ready)
            }
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

        // --- BACKSPACE COM AUTO-REPEAT AO SEGURAR ---
        btnBackspace?.setOnTouchListener { v, event ->
            when (event.action) {
                MotionEvent.ACTION_DOWN -> {
                    v.isPressed = true
                    vibrateFeedback()
                    sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL)

                    backspaceRepeatJob?.cancel()
                    backspaceRepeatJob = serviceScope.launch {
                        delay(380)
                        var interval = 55L
                        var deleteCount = 0
                        while (true) {
                            sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL)
                            deleteCount++
                            if (deleteCount > 12 && interval > 30L) {
                                interval = 30L
                            }
                            delay(interval)
                        }
                    }
                    true
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                    v.isPressed = false
                    backspaceRepeatJob?.cancel()
                    backspaceRepeatJob = null
                    true
                }
                MotionEvent.ACTION_MOVE -> {
                    val x = event.x
                    val y = event.y
                    if (x < 0 || x > v.width || y < 0 || y > v.height) {
                        v.isPressed = false
                        backspaceRepeatJob?.cancel()
                        backspaceRepeatJob = null
                    }
                    true
                }
                else -> false
            }
        }

        btnEnter?.setOnClickListener {
            vibrateFeedback()
            sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER)
        }

        btnSpace?.setOnClickListener {
            vibrateFeedback()
            commitTextToInput(" ")
        }

        btnComma?.setOnClickListener {
            vibrateFeedback()
            commitTextToInput(", ")
        }

        btnDot?.setOnClickListener {
            vibrateFeedback()
            commitTextToInput(". ")
        }

        btnSend?.setOnClickListener {
            vibrateFeedback()
            performSendAction()
        }

        btnOpenSettings?.setOnClickListener {
            vibrateFeedback()
            val intent = Intent(this, SettingsActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK
            }
            startActivity(intent)
        }

        btnEditPrompt?.setOnClickListener {
            vibrateFeedback()
            openPromptEditor(editingCurrent = true)
        }

        btnAddPrompt?.setOnClickListener {
            vibrateFeedback()
            openPromptEditor(editingCurrent = false)
        }

        btnClosePromptEditor?.setOnClickListener {
            vibrateFeedback()
            showMainVoiceView()
        }

        btnCancelPromptEditor?.setOnClickListener {
            vibrateFeedback()
            showMainVoiceView()
        }

        btnSavePrompt?.setOnClickListener {
            vibrateFeedback()
            savePromptFromEditor()
        }

        btnDeletePrompt?.setOnClickListener {
            vibrateFeedback()
            deleteCurrentEditingPrompt()
        }

        btnAddDict?.setOnClickListener {
            vibrateFeedback()
            openDictionaryQuickAdd()
        }

        btnCloseDictEditor?.setOnClickListener {
            vibrateFeedback()
            showMainVoiceView()
        }

        btnCancelDictEditor?.setOnClickListener {
            vibrateFeedback()
            showMainVoiceView()
        }

        btnSaveDictEntry?.setOnClickListener {
            vibrateFeedback()
            saveDictionaryEntryFromEditor()
        }
    }

    private fun commitTextToInput(text: String) {
        val ic = currentInputConnection
        if (ic != null) {
            ic.beginBatchEdit()
            ic.commitText(text, 1)
            ic.endBatchEdit()
        } else {
            Log.e("VoxIA", "InputConnection é nulo ao tentar inserir texto: '$text'")
        }
    }

    private fun performSendAction() {
        val editorInfo = currentInputEditorInfo
        val ic = currentInputConnection

        var handled = false
        if (editorInfo != null && ic != null) {
            val action = editorInfo.imeOptions and EditorInfo.IME_MASK_ACTION
            if (action != EditorInfo.IME_ACTION_NONE && action != EditorInfo.IME_ACTION_UNSPECIFIED) {
                handled = ic.performEditorAction(action)
            } else if (editorInfo.actionId != 0) {
                handled = ic.performEditorAction(editorInfo.actionId)
            }
        }

        if (!handled) {
            val sent = ic?.performEditorAction(EditorInfo.IME_ACTION_SEND) ?: false
            if (!sent) {
                sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER)
            }
        }
    }

    private fun openPromptEditor(editingCurrent: Boolean) {
        layoutMainVoiceView?.visibility = View.GONE
        layoutDictionaryQuickAdd?.visibility = View.GONE
        layoutPromptEditor?.visibility = View.VISIBLE

        if (editingCurrent) {
            val activePrompt = promptManager.getActivePrompt()
            currentEditingPromptId = activePrompt.id
            tvPromptEditorHeading?.text = getString(R.string.prompt_edit_title)
            etPromptTitle?.setText(activePrompt.title)
            etPromptInstructions?.setText(activePrompt.promptText)
            btnDeletePrompt?.visibility = if (activePrompt.isBuiltIn) View.GONE else View.VISIBLE
        } else {
            currentEditingPromptId = null
            tvPromptEditorHeading?.text = getString(R.string.prompt_new_title)
            etPromptTitle?.setText("")
            etPromptInstructions?.setText("")
            btnDeletePrompt?.visibility = View.GONE
        }
    }

    private fun savePromptFromEditor() {
        val title = etPromptTitle?.text?.toString()?.trim() ?: ""
        val text = etPromptInstructions?.text?.toString()?.trim() ?: ""

        if (title.isBlank()) {
            Toast.makeText(this, "Informe um título para o prompt.", Toast.LENGTH_SHORT).show()
            return
        }

        val saved = promptManager.addOrUpdatePrompt(currentEditingPromptId, title, text)
        setupPromptSpinner()
        showMainVoiceView()
        Toast.makeText(this, "Prompt '${saved.title}' salvo e ativado!", Toast.LENGTH_SHORT).show()
    }

    private fun deleteCurrentEditingPrompt() {
        val id = currentEditingPromptId
        if (id != null) {
            val deleted = promptManager.deletePrompt(id)
            if (deleted) {
                setupPromptSpinner()
                showMainVoiceView()
                Toast.makeText(this, "Prompt excluído.", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun openDictionaryQuickAdd() {
        layoutMainVoiceView?.visibility = View.GONE
        layoutPromptEditor?.visibility = View.GONE
        layoutDictionaryQuickAdd?.visibility = View.VISIBLE

        etDictOriginal?.setText("")
        etDictReplacement?.setText("")
        cbDictVocabOnly?.isChecked = false
    }

    private fun saveDictionaryEntryFromEditor() {
        val orig = etDictOriginal?.text?.toString()?.trim() ?: ""
        val repl = etDictReplacement?.text?.toString()?.trim() ?: ""
        val isVocab = cbDictVocabOnly?.isChecked ?: false

        if (orig.isBlank()) {
            Toast.makeText(this, "Informe o termo falado.", Toast.LENGTH_SHORT).show()
            return
        }

        dictionaryManager.addOrUpdateEntry(null, orig, repl, isVocab)
        showMainVoiceView()
        Toast.makeText(this, "Termo '$orig' adicionado ao Dicionário!", Toast.LENGTH_SHORT).show()
    }

    private fun showMainVoiceView() {
        layoutPromptEditor?.visibility = View.GONE
        layoutDictionaryQuickAdd?.visibility = View.GONE
        layoutMainVoiceView?.visibility = View.VISIBLE
    }

    private fun handleMicToggle() {
        vibrateFeedback()
        val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
        val apiKey = prefs.getString("groq_api_key", "") ?: ""

        if (apiKey.isBlank()) {
            tvStatusMessage?.text = "⚠ Configure sua Chave da Groq nas Configurações!"
            val intent = Intent(this, SettingsActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK
            }
            startActivity(intent)
            return
        }

        if (!audioRecorder.isRecordingNow()) {
            val started = audioRecorder.startRecording()
            if (started) {
                btnRetryAudio?.visibility = View.GONE
                btnMicRecord?.setBackgroundResource(R.drawable.bg_mic_recording)
                btnMicRecord?.setImageResource(R.drawable.ic_stop)
                tvStatusMessage?.text = getString(R.string.status_recording)
            } else {
                tvStatusMessage?.text = "⚠ Erro ao acessar o microfone."
            }
        } else {
            val audioFile = audioRecorder.stopRecording()
            btnMicRecord?.setBackgroundResource(R.drawable.bg_mic_button)
            btnMicRecord?.setImageResource(R.drawable.ic_mic)

            if (audioFile == null || !audioFile.exists() || audioFile.length() < 100) {
                tvStatusMessage?.text = "⚠ Fale um pouco mais (áudio vazio)."
                resetStatusAfterDelay(2500)
                return
            }

            pendingAudioFile = audioFile
            processAudioFile(audioFile)
        }
    }

    /**
     * Processa a transcrição e pós-processamento preservando o arquivo em caso de oscilação de rede
     */
    private fun processAudioFile(audioFile: File) {
        if (isProcessingAudio) return
        isProcessingAudio = true

        val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
        val apiKey = prefs.getString("groq_api_key", "") ?: ""

        tvStatusMessage?.text = getString(R.string.status_transcribing)
        btnRetryAudio?.visibility = View.GONE

        serviceScope.launch {
            val whisperTerms = dictionaryManager.getWhisperPromptTerms()
            Log.d("VoxIA", "Enviando para Whisper (${audioFile.length()} bytes, termos: $whisperTerms)...")

            val transcriptionResult = groqClient.transcribeAudio(
                audioFile = audioFile,
                apiKey = apiKey,
                initialPrompt = whisperTerms
            )

            transcriptionResult.onSuccess { rawTranscript ->
                Log.d("VoxIA", "Transcrição Whisper concluída: '$rawTranscript'")
                if (rawTranscript.isBlank()) {
                    withContext(Dispatchers.Main) {
                        tvStatusMessage?.text = "Nenhuma fala detectada."
                        resetStatusAfterDelay(2500)
                    }
                    // Áudio vazio com sucesso de transcrição pode ser limpo
                    pendingAudioFile = null
                    try { audioFile.delete() } catch (ignored: Exception) {}
                } else {
                    withContext(Dispatchers.Main) {
                        tvStatusMessage?.text = getString(R.string.status_correcting)
                    }

                    val activePrompt = promptManager.getActivePrompt()
                    Log.d("VoxIA", "Enviando para LLM (openai/gpt-oss-120b) com prompt '${activePrompt.title}'...")
                    val correctedResult = groqClient.correctText(
                        rawText = rawTranscript,
                        apiKey = apiKey,
                        systemPrompt = activePrompt.promptText,
                        model = "openai/gpt-oss-120b"
                    )

                    val correctedText = correctedResult.getOrNull()
                    val finalRawText = if (!correctedText.isNullOrBlank()) correctedText else rawTranscript
                    val finalText = dictionaryManager.applyReplacements(finalRawText)
                    Log.d("VoxIA", "Texto final formatado: '$finalText'")

                    withContext(Dispatchers.Main) {
                        if (finalText.isNotBlank()) {
                            commitTextToInput(finalText + " ")
                            tvStatusMessage?.text = getString(R.string.status_done)

                            val autoSend = prefs.getBoolean("auto_send_enabled", false)
                            if (autoSend) {
                                delay(200)
                                performSendAction()
                            }
                        } else {
                            commitTextToInput(rawTranscript + " ")
                            tvStatusMessage?.text = getString(R.string.status_done)
                        }

                        // Sucesso total: limpa arquivo pendente
                        pendingAudioFile = null
                        btnRetryAudio?.visibility = View.GONE
                        try { audioFile.delete() } catch (ignored: Exception) {}

                        resetStatusAfterDelay(2000)
                    }
                }
            }.onFailure { error ->
                Log.e("VoxIA", "Falha de rede ou processamento de áudio", error)
                withContext(Dispatchers.Main) {
                    // PRESERVA O ARQUIVO DE ÁUDIO NO DISCO PARA RETENTATIVA!
                    pendingAudioFile = audioFile
                    btnRetryAudio?.visibility = View.VISIBLE
                    tvStatusMessage?.text = "⚠ Oscilação de rede. Áudio guardado!"
                    Toast.makeText(
                        this@VoxIAInputMethodService,
                        "Sem internet: O áudio foi salvo! Toque em 'Reenviar' quando restabelecer a conexão.",
                        Toast.LENGTH_LONG
                    ).show()
                }
            }

            isProcessingAudio = false
        }
    }

    private fun resetStatusAfterDelay(delayMs: Long = 2500) {
        serviceScope.launch {
            delay(delayMs)
            if (!audioRecorder.isRecordingNow() && pendingAudioFile == null) {
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
        backspaceRepeatJob?.cancel()
        audioRecorder.cancelRecording()
    }
}