package com.voxia.keyboard.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import android.util.Log
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class ApiKeyStore(context: Context) {
    private val prefs = context.getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)

    fun get(): String {
        migrateLegacyPlainKey()
        val stored = prefs.getString(PREF_ENCRYPTED, null) ?: return ""
        return try {
            val bytes = Base64.decode(stored, Base64.NO_WRAP)
            val iv = bytes.copyOfRange(0, IV_SIZE)
            val cipherText = bytes.copyOfRange(IV_SIZE, bytes.size)
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), GCMParameterSpec(128, iv))
            String(cipher.doFinal(cipherText), Charsets.UTF_8)
        } catch (e: Exception) {
            // Ex.: dados restaurados de backup em outro aparelho, cuja chave do Keystore não existe aqui.
            Log.w("ApiKeyStore", "Não foi possível descriptografar a chave salva; será preciso informá-la de novo.", e)
            prefs.edit().remove(PREF_ENCRYPTED).apply()
            ""
        }
    }

    fun save(apiKey: String) {
        val clean = apiKey.trim()
        if (clean.isEmpty()) {
            prefs.edit().remove(PREF_ENCRYPTED).remove(PREF_LEGACY_PLAIN).apply()
            return
        }
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey())
        val encrypted = cipher.iv + cipher.doFinal(clean.toByteArray(Charsets.UTF_8))
        prefs.edit()
            .putString(PREF_ENCRYPTED, Base64.encodeToString(encrypted, Base64.NO_WRAP))
            .remove(PREF_LEGACY_PLAIN)
            .apply()
    }

    private fun migrateLegacyPlainKey() {
        val legacy = prefs.getString(PREF_LEGACY_PLAIN, null)
        if (!legacy.isNullOrBlank()) {
            try {
                save(legacy)
            } catch (e: Exception) {
                Log.e("ApiKeyStore", "Falha ao migrar a chave antiga para o armazenamento criptografado", e)
            }
        }
    }

    private fun getOrCreateKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }

        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        generator.init(
            KeyGenParameterSpec.Builder(KEY_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return generator.generateKey()
    }

    companion object {
        private const val ANDROID_KEYSTORE = "AndroidKeyStore"
        private const val KEY_ALIAS = "voxia_groq_api_key"
        private const val TRANSFORMATION = "AES/GCM/NoPadding"
        private const val IV_SIZE = 12
        private const val PREF_ENCRYPTED = "groq_api_key_encrypted"
        private const val PREF_LEGACY_PLAIN = "groq_api_key"
    }
}
