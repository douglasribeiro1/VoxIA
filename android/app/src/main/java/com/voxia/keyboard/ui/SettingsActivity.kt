package com.voxia.keyboard.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.Gravity
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.FrameLayout
import android.widget.ImageButton
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.google.android.material.button.MaterialButton
import com.google.android.material.button.MaterialButtonToggleGroup
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.materialswitch.MaterialSwitch
import com.google.android.material.slider.Slider
import com.google.android.material.textfield.TextInputEditText
import com.voxia.keyboard.R
import com.voxia.keyboard.data.ApiKeyStore
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.service.VoxIABubbleService
import com.voxia.keyboard.update.UpdateManager
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.launch
import java.io.File

class SettingsActivity : AppCompatActivity() {

    private lateinit var prefs: SharedPreferences
    private lateinit var dictionaryManager: DictionaryManager
    private lateinit var apiKeyStore: ApiKeyStore

    // Situação
    private lateinit var tvAppVersion: TextView
    private lateinit var tvStatusKeyboard: TextView
    private lateinit var tvStatusBubble: TextView
    private lateinit var tvStatusKey: TextView

    // Chave
    private lateinit var etGroqApiKey: TextInputEditText
    private lateinit var btnSaveGroqKey: MaterialButton
    private lateinit var btnOpenGroq: MaterialButton

    // Teclado
    private lateinit var btnEnableKeyboard: MaterialButton
    private lateinit var btnSelectKeyboard: MaterialButton

    // Balão
    private lateinit var tvBubbleStatus: TextView
    private lateinit var switchBubbleVisible: MaterialSwitch
    private lateinit var btnEnableBubble: MaterialButton
    private lateinit var btnBubbleRestricted: MaterialButton
    private lateinit var bubblePreviewCircle: View
    private lateinit var layoutBubbleColors: LinearLayout
    private lateinit var tvBubbleAlpha: TextView
    private lateinit var sliderBubbleAlpha: Slider
    private lateinit var toggleBubbleSize: MaterialButtonToggleGroup

    // Preferências
    private lateinit var switchSendContext: MaterialSwitch
    private lateinit var switchLearnCorrections: MaterialSwitch
    private lateinit var switchAutoSend: MaterialSwitch

    // Dicionário
    private lateinit var etNewDictOriginal: TextInputEditText
    private lateinit var etNewDictReplacement: TextInputEditText
    private lateinit var cbNewDictVocabOnly: MaterialCheckBox
    private lateinit var btnAddDictEntry: MaterialButton
    private lateinit var layoutDictEntriesList: LinearLayout

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_settings)

        prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
        dictionaryManager = DictionaryManager(this)
        apiKeyStore = ApiKeyStore(this)

        initViews()
        loadSavedData()
        setupListeners()
        buildColorChoices()
        updateBubblePreview()
        renderDictionaryList()
        checkPermissions()
        showWelcomeIfNoKey()
        setupUpdateCard()
        setupFormatCard()
        setupHelpIcons(findViewById<View>(android.R.id.content))
        if (intent?.getBooleanExtra(EXTRA_START_UPDATE, false) == true) checkForUpdate(manual = true, startRightAway = true)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        if (intent.getBooleanExtra(EXTRA_START_UPDATE, false)) checkForUpdate(manual = true, startRightAway = true)
    }

    // ---------- Formato do texto (Parágrafos / Linha única / Personalizado) ----------

    private fun setupFormatCard() {
        val promptManager = com.voxia.keyboard.data.PromptManager(this)
        val toggle = findViewById<MaterialButtonToggleGroup>(R.id.toggleFormat)
        val box = findViewById<View>(R.id.customPromptBox)
        val etCustom = findViewById<TextInputEditText>(R.id.etCustomPrompt)
        etCustom.setText(promptManager.getCustomPromptText())

        val active = promptManager.getActivePromptId()
        toggle.check(when (active) {
            "default_single_line" -> R.id.btnFormatSingleLine
            com.voxia.keyboard.data.PromptManager.CUSTOM_ID -> R.id.btnFormatCustom
            else -> R.id.btnFormatParagraphs
        })
        box.visibility = if (active == com.voxia.keyboard.data.PromptManager.CUSTOM_ID) View.VISIBLE else View.GONE

        toggle.addOnButtonCheckedListener { _, checkedId, isChecked ->
            if (!isChecked) return@addOnButtonCheckedListener
            when (checkedId) {
                R.id.btnFormatParagraphs -> { promptManager.setActivePromptId("default_paragraphs"); box.visibility = View.GONE }
                R.id.btnFormatSingleLine -> { promptManager.setActivePromptId("default_single_line"); box.visibility = View.GONE }
                R.id.btnFormatCustom -> {
                    box.visibility = View.VISIBLE
                    if (promptManager.getCustomPromptText().isNotBlank()) {
                        promptManager.setActivePromptId(com.voxia.keyboard.data.PromptManager.CUSTOM_ID)
                    } else {
                        etCustom.setText(CUSTOM_PROMPT_EXAMPLE)
                        etCustom.requestFocus()
                    }
                }
            }
        }
        findViewById<MaterialButton>(R.id.btnSaveCustomPrompt).setOnClickListener {
            val text = etCustom.text?.toString()?.trim().orEmpty()
            if (text.isEmpty()) {
                toast("Escreva o seu prompt antes de salvar.")
                return@setOnClickListener
            }
            promptManager.saveCustomPrompt(text) // também deixa o Personalizado ativo
            toast("Prompt salvo. Os próximos ditados usam o modo Personalizado.")
        }
    }

    /**
     * Explicações longas (marcadas com android:tag="help" no layout) ficam escondidas: o título acima
     * ganha um "ⓘ" e, ao tocar, a explicação abre numa janelinha. A tela fica limpa para quem começa.
     */
    private fun setupHelpIcons(view: View) {
        if (view is android.view.ViewGroup) {
            for (i in 0 until view.childCount) setupHelpIcons(view.getChildAt(i))
            return
        }
        if (view !is TextView || view.tag != "help") return
        val helpText = view.text?.toString().orEmpty()
        if (helpText.isBlank()) return
        val parent = view.parent as? android.view.ViewGroup ?: return
        val index = parent.indexOfChild(view)
        val label = (if (index > 0) parent.getChildAt(index - 1) else null) as? TextView
        val show = { title: String ->
            com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(title)
                .setMessage(helpText)
                .setPositiveButton("Entendi", null)
                .show()
            Unit
        }
        if (label != null && label !is android.widget.Button) {
            view.visibility = View.GONE
            val title = label.text.toString()
            label.text = "$title  ⓘ"
            label.setOnClickListener { show(title) }
        } else {
            // Sem título logo acima: a própria explicação vira um link curto
            view.text = "ⓘ Saiba mais"
            view.setTextColor(ContextCompat.getColor(this, R.color.m3_primary))
            view.setOnClickListener { show("Como funciona") }
        }
    }

    // ---------- Atualização com um clique ----------

    private lateinit var tvUpdateStatus: TextView
    private lateinit var btnUpdate: MaterialButton
    private var availableUpdate: UpdateManager.UpdateInfo? = null
    private var updateBusy = false
    /** APK baixado esperando a permissão "instalar apps desta fonte". */
    private var pendingApk: File? = null

    private fun setupUpdateCard() {
        tvUpdateStatus = findViewById(R.id.tvUpdateStatus)
        btnUpdate = findViewById(R.id.btnUpdate)
        val switchAuto = findViewById<MaterialSwitch>(R.id.switchAutoUpdate)
        switchAuto.isChecked = UpdateManager.isAutoCheckEnabled(this)
        switchAuto.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean(UpdateManager.PREF_AUTO, isChecked).apply()
        }
        btnUpdate.setOnClickListener {
            if (availableUpdate != null) startUpdate() else checkForUpdate(manual = true, startRightAway = false)
        }
        renderUpdateCard("Você está na versão ${UpdateManager.currentVersionName(this)}.")
    }

    private fun renderUpdateCard(status: String) {
        tvUpdateStatus.text = status
        btnUpdate.isEnabled = !updateBusy
        btnUpdate.text = when {
            updateBusy -> "Aguarde…"
            availableUpdate != null -> "Atualizar agora"
            else -> "Procurar atualizações"
        }
    }

    private fun checkForUpdate(manual: Boolean, startRightAway: Boolean) {
        if (updateBusy) return
        updateBusy = true
        renderUpdateCard("Procurando versão nova…")
        lifecycleScope.launch {
            val info = UpdateManager.check(this@SettingsActivity)
            updateBusy = false
            availableUpdate = info
            val current = UpdateManager.currentVersionName(this@SettingsActivity)
            if (info == null) {
                renderUpdateCard(if (manual) "Você já está com a versão mais nova ($current)." else "Você está na versão $current.")
            } else {
                val name = info.versionName.ifBlank { "nova" }
                renderUpdateCard("Versão $name disponível (você está na $current).")
                if (startRightAway) startUpdate()
            }
        }
    }

    private fun startUpdate() {
        val info = availableUpdate ?: return
        if (updateBusy) return
        updateBusy = true
        renderUpdateCard("Baixando a versão ${info.versionName}…")
        lifecycleScope.launch {
            val apk = UpdateManager.download(this@SettingsActivity, info)
            updateBusy = false
            if (apk == null) {
                renderUpdateCard("Não foi possível baixar agora. Tente de novo ou baixe pelo navegador.")
                openUpdateInBrowser()
                return@launch
            }
            installOrAskPermission(apk)
        }
    }

    private fun installOrAskPermission(apk: File) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && !packageManager.canRequestPackageInstalls()) {
            pendingApk = apk
            renderUpdateCard("Falta um passo: permita que o VoxIA instale a atualização.")
            com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle("Permitir atualizações")
                .setMessage("Só na primeira vez: na tela que vai abrir, ligue \"Permitir desta fonte\" e volte ao VoxIA. A instalação continua sozinha.")
                .setPositiveButton("Abrir") { _, _ ->
                    try {
                        startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:$packageName")))
                    } catch (e: Exception) {
                        openUpdateInBrowser()
                    }
                }
                .setNegativeButton("Agora não", null)
                .show()
            return
        }
        pendingApk = null
        if (UpdateManager.install(this, apk)) {
            renderUpdateCard("Toque em \"Instalar\" na tela do Android para concluir.")
        } else {
            openUpdateInBrowser()
        }
    }

    private fun openUpdateInBrowser() {
        try {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(UpdateManager.DOWNLOAD_PAGE)))
        } catch (ignored: Exception) {}
    }

    /** Primeira vez, sem chave: explica o passo a passo da Groq antes de tudo. */
    private fun showWelcomeIfNoKey() {
        if (apiKeyStore.get().isNotBlank()) return
        if (prefs.getBoolean(PREF_WELCOME_SHOWN, false)) return
        prefs.edit().putBoolean(PREF_WELCOME_SHOWN, true).apply()

        com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
            .setTitle("Bem-vindo ao VoxIA!")
            .setMessage(
                "Para funcionar, o VoxIA precisa da SUA chave gratuita da Groq (o serviço que transforma a voz em texto). Leva 2 minutos:\n\n" +
                "1. Toque em \"Criar chave\": o site da Groq vai abrir.\n" +
                "2. Entre com sua conta Google (ou crie uma com e-mail). É grátis e não pede cartão.\n" +
                "3. Toque em \"Create API Key\", dê um nome (ex.: VoxIA) e confirme.\n" +
                "4. Copie a chave (começa com gsk_). Ela só aparece uma vez!\n" +
                "5. Volte ao VoxIA, cole no campo \"Chave da Groq\" e toque em Salvar chave.\n\n" +
                "Depois, ative o Teclado e/ou o Balão nesta mesma tela."
            )
            .setPositiveButton("Criar chave") { _, _ -> openGroqKeysPage() }
            .setNegativeButton("Já tenho a chave") { _, _ -> etGroqApiKey.requestFocus() }
            .show()
    }

    private fun openGroqKeysPage() {
        try {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(GROQ_KEYS_URL)))
        } catch (e: Exception) {
            toast("Abra no navegador: $GROQ_KEYS_URL")
        }
    }

    override fun onResume() {
        super.onResume()
        updateStatus()
        renderDictionaryList()
        val apk = pendingApk
        if (apk != null && apk.exists() &&
            (Build.VERSION.SDK_INT < Build.VERSION_CODES.O || packageManager.canRequestPackageInstalls())) {
            installOrAskPermission(apk)
        } else if (availableUpdate == null && UpdateManager.isAutoCheckDue(this)) {
            checkForUpdate(manual = false, startRightAway = false)
        }
    }

    private fun initViews() {
        tvAppVersion = findViewById(R.id.tvAppVersion)
        tvStatusKeyboard = findViewById(R.id.tvStatusKeyboard)
        tvStatusBubble = findViewById(R.id.tvStatusBubble)
        tvStatusKey = findViewById(R.id.tvStatusKey)

        etGroqApiKey = findViewById(R.id.etGroqApiKey)
        btnSaveGroqKey = findViewById(R.id.btnSaveGroqKey)
        btnOpenGroq = findViewById(R.id.btnOpenGroq)

        btnEnableKeyboard = findViewById(R.id.btnEnableKeyboard)
        btnSelectKeyboard = findViewById(R.id.btnSelectKeyboard)

        tvBubbleStatus = findViewById(R.id.tvBubbleStatus)
        switchBubbleVisible = findViewById(R.id.switchBubbleVisible)
        btnEnableBubble = findViewById(R.id.btnEnableBubble)
        btnBubbleRestricted = findViewById(R.id.btnBubbleRestricted)
        bubblePreviewCircle = findViewById(R.id.bubblePreviewCircle)
        layoutBubbleColors = findViewById(R.id.layoutBubbleColors)
        tvBubbleAlpha = findViewById(R.id.tvBubbleAlpha)
        sliderBubbleAlpha = findViewById(R.id.sliderBubbleAlpha)
        toggleBubbleSize = findViewById(R.id.toggleBubbleSize)

        switchSendContext = findViewById(R.id.switchSendContext)
        switchLearnCorrections = findViewById(R.id.switchLearnCorrections)
        switchAutoSend = findViewById(R.id.switchAutoSend)

        etNewDictOriginal = findViewById(R.id.etNewDictOriginal)
        etNewDictReplacement = findViewById(R.id.etNewDictReplacement)
        cbNewDictVocabOnly = findViewById(R.id.cbNewDictVocabOnly)
        btnAddDictEntry = findViewById(R.id.btnAddDictEntry)
        layoutDictEntriesList = findViewById(R.id.layoutDictEntriesList)

        try {
            val version = packageManager.getPackageInfo(packageName, 0).versionName
            tvAppVersion.text = "Ditado por voz com IA · versão $version"
        } catch (ignored: Exception) {}
    }

    private fun loadSavedData() {
        etGroqApiKey.setText(apiKeyStore.get())
        switchAutoSend.isChecked = prefs.getBoolean("auto_send_enabled", false)
        switchSendContext.isChecked = prefs.getBoolean(VoxIABubbleService.PREF_SEND_CONTEXT, VoxIABubbleService.DEFAULT_SEND_CONTEXT)
        switchLearnCorrections.isChecked = prefs.getBoolean(VoxIABubbleService.PREF_LEARN, true)
        switchBubbleVisible.isChecked = prefs.getBoolean(VoxIABubbleService.PREF_ENABLED, true)

        val alpha = prefs.getInt(VoxIABubbleService.PREF_ALPHA, 100).coerceIn(30, 100)
        sliderBubbleAlpha.value = (alpha / 5 * 5).toFloat()
        tvBubbleAlpha.text = "Opacidade quando parado: $alpha%"

        toggleBubbleSize.check(
            when (prefs.getInt(VoxIABubbleService.PREF_SIZE, 1)) {
                0 -> R.id.btnSizeSmall
                2 -> R.id.btnSizeLarge
                else -> R.id.btnSizeMedium
            }
        )
    }

    private fun setupListeners() {
        btnSaveGroqKey.setOnClickListener {
            val key = etGroqApiKey.text?.toString()?.trim() ?: ""
            if (key.isBlank()) {
                toast("Cole a chave da Groq antes de salvar.")
                return@setOnClickListener
            }
            try {
                apiKeyStore.save(key)
                toast("Chave salva.")
                updateStatus()
            } catch (e: Exception) {
                toast("Não foi possível salvar a chave: ${e.message}")
            }
        }

        btnOpenGroq.setOnClickListener { openGroqKeysPage() }

        btnEnableKeyboard.setOnClickListener {
            startActivity(Intent(Settings.ACTION_INPUT_METHOD_SETTINGS))
        }

        btnSelectKeyboard.setOnClickListener {
            val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
            imm.showInputMethodPicker()
        }

        btnEnableBubble.setOnClickListener {
            try {
                startActivity(VoxIABubbleService.accessibilitySettingsIntent(this))
            } catch (e: Exception) {
                startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
            }
        }

        btnBubbleRestricted.setOnClickListener {
            toast("Toque nos três pontinhos ⋮ e em \"Permitir configurações restritas\".")
            startActivity(
                Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", packageName, null))
            )
        }

        switchBubbleVisible.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean(VoxIABubbleService.PREF_ENABLED, isChecked).apply()
            updateStatus()
        }

        sliderBubbleAlpha.addOnChangeListener { _, value, _ ->
            val pct = value.toInt()
            tvBubbleAlpha.text = "Opacidade quando parado: $pct%"
            prefs.edit().putInt(VoxIABubbleService.PREF_ALPHA, pct).apply()
            updateBubblePreview()
        }

        toggleBubbleSize.addOnButtonCheckedListener { _, checkedId, isChecked ->
            if (!isChecked) return@addOnButtonCheckedListener
            val size = when (checkedId) {
                R.id.btnSizeSmall -> 0
                R.id.btnSizeLarge -> 2
                else -> 1
            }
            prefs.edit().putInt(VoxIABubbleService.PREF_SIZE, size).apply()
            updateBubblePreview()
        }

        switchLearnCorrections.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean(VoxIABubbleService.PREF_LEARN, isChecked).apply()
        }
        switchSendContext.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean(VoxIABubbleService.PREF_SEND_CONTEXT, isChecked).apply()
        }

        switchAutoSend.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean("auto_send_enabled", isChecked).apply()
        }

        btnAddDictEntry.setOnClickListener {
            val orig = etNewDictOriginal.text?.toString()?.trim() ?: ""
            val repl = etNewDictReplacement.text?.toString()?.trim() ?: ""
            if (orig.isBlank()) {
                toast("Informe a palavra ou termo falado.")
                return@setOnClickListener
            }
            dictionaryManager.addOrUpdateEntry(null, orig, repl, cbNewDictVocabOnly.isChecked)
            etNewDictOriginal.setText("")
            etNewDictReplacement.setText("")
            cbNewDictVocabOnly.isChecked = false
            toast("\"$orig\" adicionado ao dicionário.")
            renderDictionaryList()
        }
    }

    // ---------- Situação ----------

    private fun updateStatus() {
        val keyboardOn = isKeyboardEnabled()
        val bubbleOn = VoxIABubbleService.isEnabled(this)
        val hasKey = apiKeyStore.get().isNotBlank()

        setPill(tvStatusKeyboard, "Teclado", keyboardOn)
        setPill(tvStatusBubble, "Balão", bubbleOn && VoxIABubbleService.running && switchBubbleVisible.isChecked)
        setPill(tvStatusKey, "Chave", hasKey)

        if (bubbleOn && !VoxIABubbleService.running) {
            // Ligado nas configurações, mas o Android não reconectou (ex.: depois da economia de energia)
            tvBubbleStatus.text = "Parado: desligue e ligue de novo"
            btnEnableBubble.text = "Religar na Acessibilidade"
        } else if (bubbleOn) {
            tvBubbleStatus.text = if (switchBubbleVisible.isChecked) "Ativado" else "Ativado, mas oculto"
            btnEnableBubble.text = "Abrir Acessibilidade"
        } else {
            tvBubbleStatus.text = "Desativado"
            btnEnableBubble.text = "Ativar na Acessibilidade"
        }
    }

    private fun setPill(view: TextView, label: String, ok: Boolean) {
        view.text = if (ok) "● $label" else "○ $label"
        view.setTextColor(ContextCompat.getColor(this, if (ok) R.color.m3_success else R.color.m3_text_muted))
    }

    private fun isKeyboardEnabled(): Boolean {
        val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
        return imm.enabledInputMethodList.any { it.packageName == packageName }
    }

    // ---------- Aparência do balão ----------

    private fun buildColorChoices() {
        layoutBubbleColors.removeAllViews()
        val selected = prefs.getInt(VoxIABubbleService.PREF_COLOR, VoxIABubbleService.BUBBLE_COLORS[0])
        for (color in VoxIABubbleService.BUBBLE_COLORS) {
            val swatch = FrameLayout(this).apply {
                val ring = GradientDrawable().apply {
                    shape = GradientDrawable.OVAL
                    setColor(color)
                    if (color == selected) setStroke(3.dp(), ContextCompat.getColor(context, R.color.m3_text))
                }
                background = ring
                contentDescription = "Cor do balão"
                setOnClickListener {
                    prefs.edit().putInt(VoxIABubbleService.PREF_COLOR, color).apply()
                    buildColorChoices()
                    updateBubblePreview()
                }
            }
            layoutBubbleColors.addView(swatch, LinearLayout.LayoutParams(34.dp(), 34.dp()).apply {
                marginEnd = 12.dp()
            })
        }
    }

    private fun updateBubblePreview() {
        val color = prefs.getInt(VoxIABubbleService.PREF_COLOR, VoxIABubbleService.BUBBLE_COLORS[0])
        val diameter = VoxIABubbleService.bubbleDiameterDp(prefs.getInt(VoxIABubbleService.PREF_SIZE, 1))
        val alpha = prefs.getInt(VoxIABubbleService.PREF_ALPHA, 100).coerceIn(30, 100) / 100f

        bubblePreviewCircle.background = VoxIABubbleService.bubbleBackground(color)
        bubblePreviewCircle.layoutParams = bubblePreviewCircle.layoutParams.apply {
            width = diameter.dp()
            height = diameter.dp()
        }
        (bubblePreviewCircle.parent as View).alpha = alpha
    }

    // ---------- Dicionário ----------

    private fun renderDictionaryList() {
        layoutDictEntriesList.removeAllViews()
        val entries = dictionaryManager.getEntries()

        if (entries.isEmpty()) {
            layoutDictEntriesList.addView(TextView(this).apply {
                text = "Nenhuma palavra cadastrada ainda."
                setTextColor(ContextCompat.getColor(context, R.color.m3_text_muted))
                textSize = 13f
                setPadding(4.dp(), 8.dp(), 4.dp(), 8.dp())
            })
            return
        }

        for (entry in entries) {
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                setBackgroundResource(R.drawable.bg_list_row)
                setPadding(14.dp(), 6.dp(), 4.dp(), 6.dp())
            }

            val texts = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
            texts.addView(TextView(this).apply {
                text = if (entry.isVocabularyOnly) entry.original else "${entry.original}  →  ${entry.replacement}"
                setTextColor(ContextCompat.getColor(context, R.color.m3_text))
                textSize = 14f
                setTypeface(typeface, Typeface.BOLD)
            })
            if (entry.isVocabularyOnly) {
                texts.addView(TextView(this).apply {
                    text = "Vocabulário do Whisper"
                    setTextColor(ContextCompat.getColor(context, R.color.m3_success))
                    textSize = 12f
                })
            }
            row.addView(texts, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))

            row.addView(ImageButton(this).apply {
                setImageResource(R.drawable.ic_delete)
                setBackgroundResource(android.R.color.transparent)
                contentDescription = "Excluir"
                setOnClickListener {
                    dictionaryManager.deleteEntry(entry.id)
                    toast("\"${entry.original}\" removido.")
                    renderDictionaryList()
                }
            }, LinearLayout.LayoutParams(40.dp(), 40.dp()))

            layoutDictEntriesList.addView(row, LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply { bottomMargin = 8.dp() })
        }
    }

    // ---------- Utilidades ----------

    private fun checkPermissions() {
        val missing = mutableListOf<String>()
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            missing.add(Manifest.permission.RECORD_AUDIO)
        }
        // Notificação "Gravando ditado…" do balão (Android 13+)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            missing.add(Manifest.permission.POST_NOTIFICATIONS)
        }
        if (missing.isNotEmpty()) {
            ActivityCompat.requestPermissions(this, missing.toTypedArray(), 101)
        }
    }

    private fun toast(message: String) {
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show()
    }

    private fun Int.dp(): Int = (this * resources.displayMetrics.density).toInt()

    companion object {
        /** Aberto pela notificação "versão nova": já começa a atualizar. */
        const val EXTRA_START_UPDATE = "start_update"
        private const val CUSTOM_PROMPT_EXAMPLE =
            "Corrija ortografia e pontuação e organize o texto em parágrafos por assunto, como um texto bem escrito em português. Mantenha todas as informações ditadas."
        private const val GROQ_KEYS_URL = "https://console.groq.com/keys"
        private const val PREF_WELCOME_SHOWN = "welcome_groq_shown"
    }
}
