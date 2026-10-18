package com.voxia.keyboard.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.Gravity
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.Button
import android.widget.CheckBox
import android.widget.ImageButton
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.google.android.material.switchmaterial.SwitchMaterial
import com.google.android.material.textfield.TextInputEditText
import com.voxia.keyboard.R
import com.voxia.keyboard.data.ApiKeyStore
import com.voxia.keyboard.data.DictionaryEntry
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.data.PromptManager
import com.voxia.keyboard.service.VoxIABubbleService

class SettingsActivity : AppCompatActivity() {

    private lateinit var promptManager: PromptManager
    private lateinit var dictionaryManager: DictionaryManager

    private lateinit var btnEnableKeyboard: Button
    private lateinit var btnSelectKeyboard: Button
    private lateinit var switchAutoSend: SwitchMaterial
    private lateinit var etGroqApiKey: TextInputEditText
    private lateinit var btnSaveGroqKey: Button

    // Balão flutuante
    private lateinit var tvBubbleStatus: TextView
    private lateinit var btnEnableBubble: Button
    private lateinit var btnBubbleRestricted: Button
    private lateinit var switchBubbleVisible: SwitchMaterial

    // Dicionário UI
    private lateinit var etNewDictOriginal: TextInputEditText
    private lateinit var etNewDictReplacement: TextInputEditText
    private lateinit var cbNewDictVocabOnly: CheckBox
    private lateinit var btnAddDictEntry: Button
    private lateinit var layoutDictEntriesList: LinearLayout

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_settings)

        promptManager = PromptManager(this)
        dictionaryManager = DictionaryManager(this)

        initViews()
        setupListeners()
        loadSavedData()
        renderDictionaryList()
        checkPermissions()
    }

    private fun initViews() {
        btnEnableKeyboard = findViewById(R.id.btnEnableKeyboard)
        btnSelectKeyboard = findViewById(R.id.btnSelectKeyboard)
        switchAutoSend = findViewById(R.id.switchAutoSend)
        etGroqApiKey = findViewById(R.id.etGroqApiKey)
        btnSaveGroqKey = findViewById(R.id.btnSaveGroqKey)

        tvBubbleStatus = findViewById(R.id.tvBubbleStatus)
        btnEnableBubble = findViewById(R.id.btnEnableBubble)
        btnBubbleRestricted = findViewById(R.id.btnBubbleRestricted)
        switchBubbleVisible = findViewById(R.id.switchBubbleVisible)

        etNewDictOriginal = findViewById(R.id.etNewDictOriginal)
        etNewDictReplacement = findViewById(R.id.etNewDictReplacement)
        cbNewDictVocabOnly = findViewById(R.id.cbNewDictVocabOnly)
        btnAddDictEntry = findViewById(R.id.btnAddDictEntry)
        layoutDictEntriesList = findViewById(R.id.layoutDictEntriesList)
    }

    private fun setupListeners() {
        btnEnableKeyboard.setOnClickListener {
            val intent = Intent(Settings.ACTION_INPUT_METHOD_SETTINGS)
            startActivity(intent)
        }

        btnSelectKeyboard.setOnClickListener {
            val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
            imm.showInputMethodPicker()
        }

        btnEnableBubble.setOnClickListener {
            startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
        }

        btnBubbleRestricted.setOnClickListener {
            startActivity(
                Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", packageName, null))
            )
        }

        switchBubbleVisible.setOnCheckedChangeListener { _, isChecked ->
            getSharedPreferences("voxia_settings", Context.MODE_PRIVATE).edit()
                .putBoolean(VoxIABubbleService.PREF_ENABLED, isChecked)
                .apply()
        }

        switchAutoSend.setOnCheckedChangeListener { _, isChecked ->
            val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
            prefs.edit().putBoolean("auto_send_enabled", isChecked).apply()
        }

        btnSaveGroqKey.setOnClickListener {
            val key = etGroqApiKey.text?.toString()?.trim() ?: ""
            if (key.isBlank()) {
                Toast.makeText(this, "Por favor, insira a chave da Groq.", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            try {
                ApiKeyStore(this).save(key)
                Toast.makeText(this, "✓ Chave Groq salva com sucesso!", Toast.LENGTH_SHORT).show()
            } catch (e: Exception) {
                Toast.makeText(this, "⚠ Não foi possível salvar a chave: ${e.message}", Toast.LENGTH_LONG).show()
            }
        }

        btnAddDictEntry.setOnClickListener {
            val orig = etNewDictOriginal.text?.toString()?.trim() ?: ""
            val repl = etNewDictReplacement.text?.toString()?.trim() ?: ""
            val isVocab = cbNewDictVocabOnly.isChecked

            if (orig.isBlank()) {
                Toast.makeText(this, "Informe a palavra ou termo falado.", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            dictionaryManager.addOrUpdateEntry(null, orig, repl, isVocab)
            etNewDictOriginal.setText("")
            etNewDictReplacement.setText("")
            cbNewDictVocabOnly.isChecked = false

            Toast.makeText(this, "✓ Termo '$orig' adicionado ao Dicionário!", Toast.LENGTH_SHORT).show()
            renderDictionaryList()
        }
    }

    private fun renderDictionaryList() {
        layoutDictEntriesList.removeAllViews()
        val entries = dictionaryManager.getEntries()

        if (entries.isEmpty()) {
            val emptyTv = TextView(this).apply {
                text = "Nenhum termo cadastrado ainda. Adicione uma palavra acima!"
                setTextColor(Color.parseColor("#94A3B8"))
                textSize = 12f
                setPadding(8, 12, 8, 12)
            }
            layoutDictEntriesList.addView(emptyTv)
            return
        }

        for (entry in entries) {
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                setBackgroundResource(R.drawable.bg_key)
                setPadding(16, 12, 16, 12)
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply {
                    setMargins(0, 6, 0, 6)
                }
            }

            val textLayout = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
            }

            val labelTv = TextView(this).apply {
                if (entry.isVocabularyOnly) {
                    text = "${entry.original} (termo técnico Whisper)"
                    setTextColor(Color.parseColor("#10B981"))
                } else {
                    text = "${entry.original} ➔ ${entry.replacement}"
                    setTextColor(Color.parseColor("#F8FAFC"))
                }
                textSize = 13f
                setTypeface(null, Typeface.BOLD)
            }
            textLayout.addView(labelTv)
            row.addView(textLayout)

            val deleteBtn = ImageButton(this).apply {
                setImageResource(R.drawable.ic_delete)
                setBackgroundResource(android.R.color.transparent)
                layoutParams = LinearLayout.LayoutParams(36.dpToPx(), 36.dpToPx())
                contentDescription = "Excluir"
                setOnClickListener {
                    dictionaryManager.deleteEntry(entry.id)
                    Toast.makeText(this@SettingsActivity, "Termo '${entry.original}' removido.", Toast.LENGTH_SHORT).show()
                    renderDictionaryList()
                }
            }
            row.addView(deleteBtn)

            layoutDictEntriesList.addView(row)
        }
    }

    private fun Int.dpToPx(): Int {
        val density = resources.displayMetrics.density
        return (this * density).toInt()
    }

    private fun loadSavedData() {
        val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
        val apiKey = ApiKeyStore(this).get()
        val autoSend = prefs.getBoolean("auto_send_enabled", false)

        etGroqApiKey.setText(apiKey)
        switchAutoSend.isChecked = autoSend
        switchBubbleVisible.isChecked = prefs.getBoolean(VoxIABubbleService.PREF_ENABLED, true)
    }

    override fun onResume() {
        super.onResume()
        updateBubbleStatus()
    }

    private fun updateBubbleStatus() {
        if (VoxIABubbleService.isEnabled(this)) {
            tvBubbleStatus.text = "✓ Balão ativado"
            tvBubbleStatus.setTextColor(Color.parseColor("#10B981"))
            btnEnableBubble.text = "Configurações de Acessibilidade"
        } else {
            tvBubbleStatus.text = "Balão desativado"
            tvBubbleStatus.setTextColor(Color.parseColor("#94A3B8"))
            btnEnableBubble.text = "Ativar Balão (Acessibilidade)"
        }
    }

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
}