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
        private const val GROQ_KEYS_URL = "https://console.groq.com/keys"
        private const val PREF_WELCOME_SHOWN = "welcome_groq_shown"
    }
}
