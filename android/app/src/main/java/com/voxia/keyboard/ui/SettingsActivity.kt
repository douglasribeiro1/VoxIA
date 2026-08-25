package com.voxia.keyboard.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Bundle
import android.provider.Settings
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import com.voxia.keyboard.R
import com.voxia.keyboard.api.SupabaseClient
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.data.PromptManager
import kotlinx.coroutines.launch

class SettingsActivity : AppCompatActivity() {

    private lateinit var supabaseClient: SupabaseClient
    private lateinit var promptManager: PromptManager
    private lateinit var dictionaryManager: DictionaryManager

    private lateinit var btnEnableKeyboard: Button
    private lateinit var btnSelectKeyboard: Button
    private lateinit var etGroqApiKey: TextInputEditText
    private lateinit var btnSaveGroqKey: Button

    private lateinit var tvSyncStatus: TextView
    private lateinit var tilEmail: TextInputLayout
    private lateinit var tilPassword: TextInputLayout
    private lateinit var etSupabaseEmail: TextInputEditText
    private lateinit var etSupabasePassword: TextInputEditText
    private lateinit var btnLoginSync: Button
    private lateinit var btnSyncNow: Button
    private lateinit var btnLogout: Button
    private lateinit var tvSyncCounts: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_settings)

        supabaseClient = SupabaseClient(this)
        promptManager = PromptManager(this)
        dictionaryManager = DictionaryManager(this)

        initViews()
        setupListeners()
        loadSavedData()
        checkPermissions()
    }

    private fun initViews() {
        btnEnableKeyboard = findViewById(R.id.btnEnableKeyboard)
        btnSelectKeyboard = findViewById(R.id.btnSelectKeyboard)
        etGroqApiKey = findViewById(R.id.etGroqApiKey)
        btnSaveGroqKey = findViewById(R.id.btnSaveGroqKey)

        tvSyncStatus = findViewById(R.id.tvSyncStatus)
        tilEmail = findViewById(R.id.tilEmail)
        tilPassword = findViewById(R.id.tilPassword)
        etSupabaseEmail = findViewById(R.id.etSupabaseEmail)
        etSupabasePassword = findViewById(R.id.etSupabasePassword)
        btnLoginSync = findViewById(R.id.btnLoginSync)
        btnSyncNow = findViewById(R.id.btnSyncNow)
        btnLogout = findViewById(R.id.btnLogout)
        tvSyncCounts = findViewById(R.id.tvSyncCounts)
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

        btnSaveGroqKey.setOnClickListener {
            val key = etGroqApiKey.text?.toString()?.trim() ?: ""
            if (key.isBlank()) {
                Toast.makeText(this, "Por favor, insira a chave da Groq.", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
            prefs.edit().putString("groq_api_key", key).apply()
            Toast.makeText(this, "✓ Groq API Key salva com sucesso!", Toast.LENGTH_SHORT).show()
        }

        btnLoginSync.setOnClickListener {
            val email = etSupabaseEmail.text?.toString()?.trim() ?: ""
            val password = etSupabasePassword.text?.toString() ?: ""

            if (email.isBlank() || password.isBlank()) {
                Toast.makeText(this, "Preencha e-mail e senha.", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            btnLoginSync.isEnabled = false
            tvSyncStatus.text = "Conectando ao Supabase..."

            lifecycleScope.launch {
                val loginResult = supabaseClient.login(email, password)
                loginResult.onSuccess {
                    tvSyncStatus.text = "Sincronizando dados..."
                    val syncResult = supabaseClient.syncFromCloud()
                    syncResult.onSuccess { (prompts, dict) ->
                        Toast.makeText(this@SettingsActivity, "✓ Conectado e sincronizado com sucesso!", Toast.LENGTH_LONG).show()
                        updateAuthUI()
                    }.onFailure { error ->
                        Toast.makeText(this@SettingsActivity, "Login OK, erro na sincronização: ${error.message}", Toast.LENGTH_LONG).show()
                        updateAuthUI()
                    }
                }.onFailure { error ->
                    btnLoginSync.isEnabled = true
                    tvSyncStatus.text = "Falha no login: ${error.message}"
                    Toast.makeText(this@SettingsActivity, "Erro: ${error.message}", Toast.LENGTH_LONG).show()
                }
            }
        }

        btnSyncNow.setOnClickListener {
            btnSyncNow.isEnabled = false
            tvSyncStatus.text = "Sincronizando agora..."
            lifecycleScope.launch {
                val syncResult = supabaseClient.syncFromCloud()
                btnSyncNow.isEnabled = true
                syncResult.onSuccess { (prompts, dict) ->
                    Toast.makeText(this@SettingsActivity, "✓ Sincronização concluída! ($prompts prompts, $dict palavras)", Toast.LENGTH_SHORT).show()
                    updateAuthUI()
                }.onFailure { error ->
                    Toast.makeText(this@SettingsActivity, "Erro: ${error.message}", Toast.LENGTH_SHORT).show()
                    updateAuthUI()
                }
            }
        }

        btnLogout.setOnClickListener {
            supabaseClient.logout()
            Toast.makeText(this, "Desconectado.", Toast.LENGTH_SHORT).show()
            updateAuthUI()
        }
    }

    private fun loadSavedData() {
        val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
        val apiKey = prefs.getString("groq_api_key", "") ?: ""
        etGroqApiKey.setText(apiKey)

        updateAuthUI()
    }

    private fun updateAuthUI() {
        val isLoggedIn = supabaseClient.isLoggedIn()
        val email = supabaseClient.getUserEmail()

        if (isLoggedIn) {
            tvSyncStatus.text = "Status: Conectado como $email"
            tilEmail.visibility = View.GONE
            tilPassword.visibility = View.GONE
            btnLoginSync.visibility = View.GONE
            btnSyncNow.visibility = View.VISIBLE
            btnLogout.visibility = View.VISIBLE
        } else {
            tvSyncStatus.text = "Status: Não autenticado"
            tilEmail.visibility = View.VISIBLE
            tilPassword.visibility = View.VISIBLE
            btnLoginSync.visibility = View.VISIBLE
            btnLoginSync.isEnabled = true
            btnSyncNow.visibility = View.GONE
            btnLogout.visibility = View.GONE
        }

        val promptCount = promptManager.getPrompts().size
        val dictCount = dictionaryManager.getEntries().size
        tvSyncCounts.text = "Prompts disponíveis: $promptCount | Dicionário: $dictCount termos"
    }

    private fun checkPermissions() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            ActivityCompat.requestPermissions(this, arrayOf(Manifest.permission.RECORD_AUDIO), 101)
        }
    }
}
