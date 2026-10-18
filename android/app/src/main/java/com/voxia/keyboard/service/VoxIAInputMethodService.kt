package com.voxia.keyboard.service

import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.inputmethodservice.InputMethodService
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.text.InputType
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
import com.voxia.keyboard.audio.AudioRecorder
import com.voxia.keyboard.data.ApiKeyStore
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.data.PromptManager
import com.voxia.keyboard.data.PromptProfile
import com.voxia.keyboard.ui.SettingsActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.io.File

class VoxIAInputMethodService : InputMethodService() {
    private val serviceScope = CoroutineScope(Dispatchers.Main + Job())
    private var backspaceRepeatJob: Job? = null
    private var micPulseJob: Job? = null
    private var isProcessingAudio = false
    private var isStoppingRecording = false
    private var lastDictatedText: String? = null
    private var pendingAudioFile: File?
        get() = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
            .getString("pending_audio_path", null)
            ?.let { File(it) }
            ?.takeIf { it.exists() }
        set(value) {
            getSharedPreferences("voxia_settings", Context.MODE_PRIVATE).edit()
                .putString("pending_audio_path", value?.absolutePath)
                .apply()
        }
    // Texto já escrito antes do cursor (só em memória) para ajudar a grafia; vazio em campos de senha
    private var fieldContext: String = ""

    private lateinit var audioRecorder: AudioRecorder
    private lateinit var dictationEngine: DictationEngine
    private lateinit var promptManager: PromptManager
    private lateinit var dictionaryManager: DictionaryManager
    private lateinit var apiKeyStore: ApiKeyStore

    // Layout Containers
    private var layoutMainVoiceView: LinearLayout? = null
    private var layoutPromptEditor: LinearLayout? = null
    private var layoutDictionaryQuickAdd: LinearLayout? = null

    // Main Voice Views
    private var btnMicRecord: ImageButton? = null
    private var viewMicPulse: View? = null
    private var btnUndoDictation: ImageButton? = null
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
        dictationEngine = DictationEngine(this)
        promptManager = PromptManager(this)
        dictionaryManager = DictionaryManager(this)
        apiKeyStore = ApiKeyStore(this)
    }

    override fun onCreateInputView(): View {
        val view = layoutInflater.inflate(R.layout.keyboard_view, null)

        // 1. Containers
        layoutMainVoiceView = view.findViewById(R.id.layoutMainVoiceView)
        layoutPromptEditor = view.findViewById(R.id.layoutPromptEditor)
        layoutDictionaryQuickAdd = view.findViewById(R.id.layoutDictionaryQuickAdd)

        // 2. Main Voice Views
        btnMicRecord = view.findViewById(R.id.btnMicRecord)
        viewMicPulse = view.findViewById(R.id.viewMicPulse)
        btnUndoDictation = view.findViewById(R.id.btnUndoDictation)
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
        if (!restarting) {
            lastDictatedText = null
            btnUndoDictation?.visibility = View.GONE
        }
        if (audioRecorder.isRecordingNow()) {
            return
        }
        if (pendingAudioFile != null) {
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
            if (file != null) {
                processAudioFile(file)
            } else {
                btnRetryAudio?.visibility = View.GONE
                tvStatusMessage?.text = getString(R.string.status_ready)
            }
        }

        btnUndoDictation?.setOnClickListener {
            vibrateFeedback()
            undoLastDictation()
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

    /**
     * Lê até 400 caracteres antes do cursor para servir de contexto ao Whisper e à LLM.
     * Campos de senha nunca são lidos.
     */
    private fun readFieldContext(): String {
        return try {
            val info = currentInputEditorInfo
            if (info != null) {
                val cls = info.inputType and InputType.TYPE_MASK_CLASS
                val variation = info.inputType and InputType.TYPE_MASK_VARIATION
                val isPassword = when (cls) {
                    InputType.TYPE_CLASS_TEXT ->
                        variation == InputType.TYPE_TEXT_VARIATION_PASSWORD ||
                            variation == InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD ||
                            variation == InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD
                    InputType.TYPE_CLASS_NUMBER -> variation == InputType.TYPE_NUMBER_VARIATION_PASSWORD
                    else -> false
                }
                if (isPassword) return ""
            }
            val before = currentInputConnection?.getTextBeforeCursor(400, 0)?.toString() ?: ""
            before.replace(Regex("""\s+"""), " ").trim()
        } catch (e: Exception) {
            Log.w("VoxIA", "Não foi possível ler o contexto do campo: ${e.message}")
            ""
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

    private fun commitDictation(text: String) {
        commitTextToInput(text)
        lastDictatedText = text
        btnUndoDictation?.visibility = View.VISIBLE
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
        if (isStoppingRecording || isProcessingAudio) return
        val apiKey = apiKeyStore.get()

        if (apiKey.isBlank()) {
            tvStatusMessage?.text = "⚠ Configure sua Chave da Groq nas Configurações!"
            val intent = Intent(this, SettingsActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK
            }
            startActivity(intent)
            return
        }

        if (!audioRecorder.isRecordingNow()) {
            fieldContext = readFieldContext()
            val started = audioRecorder.startRecording()
            if (started) {
                btnRetryAudio?.visibility = View.GONE
                btnMicRecord?.setBackgroundResource(R.drawable.bg_mic_recording)
                btnMicRecord?.setImageResource(R.drawable.ic_stop)
                tvStatusMessage?.text = getString(R.string.status_recording)
                startMicPulse()
            } else {
                tvStatusMessage?.text = "⚠ Erro ao acessar o microfone."
            }
        } else {
            isStoppingRecording = true
            stopMicPulse()
            btnMicRecord?.setBackgroundResource(R.drawable.bg_mic_button)
            btnMicRecord?.setImageResource(R.drawable.ic_mic)

            serviceScope.launch {
                val audioFile = audioRecorder.stopRecording()
                isStoppingRecording = false

                if (audioFile == null || !audioFile.exists() || audioFile.length() < 100) {
                    tvStatusMessage?.text = "⚠ Fale um pouco mais (áudio vazio)."
                    resetStatusAfterDelay(2500)
                    return@launch
                }

                pendingAudioFile?.takeIf { it != audioFile }?.delete()
                pendingAudioFile = audioFile
                processAudioFile(audioFile)
            }
        }
    }

    private fun startMicPulse() {
        val pulse = viewMicPulse ?: return
        micPulseJob?.cancel()
        pulse.visibility = View.VISIBLE
        micPulseJob = serviceScope.launch {
            var smoothed = 0f
            while (isActive && audioRecorder.isRecordingNow()) {
                val level = audioRecorder.currentLevel()
                // Sobe rápido e desce devagar, para o anel não "piscar"
                smoothed = if (level > smoothed) smoothed + (level - smoothed) * 0.6f else smoothed * 0.85f
                val scale = 1f + smoothed * 0.18f
                pulse.scaleX = scale
                pulse.scaleY = scale
                pulse.alpha = 0.35f + smoothed * 0.65f
                delay(40)
            }
        }
    }

    private fun stopMicPulse() {
        micPulseJob?.cancel()
        micPulseJob = null
        viewMicPulse?.apply {
            visibility = View.INVISIBLE
            scaleX = 1f
            scaleY = 1f
        }
    }

    private fun undoLastDictation() {
        val inserted = lastDictatedText
        val ic = currentInputConnection
        if (inserted.isNullOrEmpty() || ic == null) {
            btnUndoDictation?.visibility = View.GONE
            return
        }

        val before = ic.getTextBeforeCursor(inserted.length, 0)?.toString()
        if (before == inserted) {
            ic.deleteSurroundingText(inserted.length, 0)
            tvStatusMessage?.text = "↶ Último ditado desfeito."
            resetStatusAfterDelay(2000)
        } else {
            Toast.makeText(this, "O texto foi alterado depois do ditado; não é possível desfazer.", Toast.LENGTH_SHORT).show()
        }
        lastDictatedText = null
        btnUndoDictation?.visibility = View.GONE
    }

    /**
     * Processa a transcrição e pós-processamento preservando o arquivo em caso de oscilação de rede
     */
    private fun processAudioFile(audioFile: File) {
        if (isProcessingAudio) return
        isProcessingAudio = true

        val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
        val apiKey = apiKeyStore.get()

        tvStatusMessage?.text = getString(R.string.status_transcribing)
        btnRetryAudio?.visibility = View.GONE

        serviceScope.launch {
            val result = dictationEngine.process(audioFile, apiKey, fieldContext) { stage ->
                if (stage == DictationEngine.Stage.CORRECTING) {
                    tvStatusMessage?.text = getString(R.string.status_correcting)
                }
            }

            result.onSuccess { finalText ->
                if (finalText.isBlank()) {
                    tvStatusMessage?.text = "Nenhuma fala detectada."
                    resetStatusAfterDelay(2500)
                } else {
                    commitDictation(finalText + " ")
                    tvStatusMessage?.text = getString(R.string.status_done)

                    val autoSend = prefs.getBoolean("auto_send_enabled", false)
                    if (autoSend) {
                        delay(200)
                        performSendAction()
                    }
                    resetStatusAfterDelay(2000)
                }
                // Sucesso (com ou sem fala): limpa arquivo pendente
                pendingAudioFile = null
                btnRetryAudio?.visibility = View.GONE
                try { audioFile.delete() } catch (ignored: Exception) {}
            }.onFailure { error ->
                Log.e("VoxIA", "Falha de rede ou processamento de áudio", error)
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
        serviceScope.cancel()
    }
}