package com.voxia.keyboard.ui

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.provider.Settings
import android.view.WindowManager
import android.view.inputmethod.EditorInfo
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.google.android.material.button.MaterialButton
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import com.voxia.keyboard.R
import com.voxia.keyboard.data.DictionaryManager

/**
 * Janelinha por cima do app em uso para cadastrar uma palavra no dicionário.
 * Aberta pelo botão de livro do teclado ou pelo toque longo no balão; a palavra
 * selecionada no texto já vem preenchida em "como sai errado".
 */
class AddWordActivity : AppCompatActivity() {

    private lateinit var etOriginal: TextInputEditText
    private lateinit var etReplacement: TextInputEditText
    private lateinit var tilReplacement: TextInputLayout
    private lateinit var cbVocabOnly: MaterialCheckBox

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_add_word)
        setFinishOnTouchOutside(true)

        etOriginal = findViewById(R.id.etAddWordOriginal)
        etReplacement = findViewById(R.id.etAddWordReplacement)
        tilReplacement = findViewById(R.id.tilAddWordReplacement)
        cbVocabOnly = findViewById(R.id.cbAddWordVocabOnly)

        // O teclado VoxIA não tem letras: avisa como digitar a forma certa
        val currentIme = Settings.Secure.getString(contentResolver, Settings.Secure.DEFAULT_INPUT_METHOD) ?: ""
        if (currentIme.startsWith(packageName)) {
            findViewById<TextView>(R.id.tvAddWordHint).text =
                "Dite a forma certa no campo ou toque no globo do teclado para digitar com outro teclado."
        }

        val selected = intent.getStringExtra(EXTRA_ORIGINAL)?.trim().orEmpty()
        etOriginal.setText(selected)
        if (selected.isNotEmpty()) etReplacement.requestFocus() else etOriginal.requestFocus()
        window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_VISIBLE)

        cbVocabOnly.setOnCheckedChangeListener { _, checked ->
            tilReplacement.isEnabled = !checked
            tilReplacement.hint = if (checked) "Não usado: só ensina o termo" else "Como deve ficar"
        }
        etReplacement.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_DONE) { save(); true } else false
        }
        findViewById<MaterialButton>(R.id.btnAddWordCancel).setOnClickListener { finish() }
        findViewById<MaterialButton>(R.id.btnAddWordSave).setOnClickListener { save() }
    }

    private fun save() {
        val original = etOriginal.text?.toString()?.trim().orEmpty()
        val vocabOnly = cbVocabOnly.isChecked
        val replacement = if (vocabOnly) "" else etReplacement.text?.toString()?.trim().orEmpty()

        if (original.isEmpty()) {
            etOriginal.error = "Escreva como a palavra sai"
            return
        }
        if (!vocabOnly && replacement.isEmpty()) {
            etReplacement.error = "Escreva como deve ficar"
            return
        }

        DictionaryManager(this).addOrUpdateEntry(null, original, replacement, vocabOnly)
        val msg = if (vocabOnly) "\"$original\" adicionado ao vocabulário." else "Pronto: \"$original\" → \"$replacement\"."
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
        finish()
    }

    companion object {
        private const val EXTRA_ORIGINAL = "original"
        private const val MAX_SELECTION = 80

        fun open(context: Context, selectedText: String) {
            val text = selectedText.trim().take(MAX_SELECTION)
            val intent = Intent(context, AddWordActivity::class.java)
                .putExtra(EXTRA_ORIGINAL, text)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
            try {
                context.startActivity(intent)
            } catch (e: Exception) {
                Toast.makeText(context, "Não foi possível abrir o dicionário.", Toast.LENGTH_SHORT).show()
            }
        }
    }
}
