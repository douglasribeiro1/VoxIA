package com.voxia.keyboard.update

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.FileProvider
import androidx.core.content.pm.PackageInfoCompat
import com.google.gson.Gson
import com.google.gson.JsonObject
import com.voxia.keyboard.R
import com.voxia.keyboard.ui.SettingsActivity
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.TimeUnit

/**
 * Atualização com um clique. O GitHub Actions publica, junto com o APK, um version-android.json
 * com o versionCode (número do build) e o SHA-256 do APK. Se o número for maior que o deste app,
 * o VoxIA baixa o APK, confere o SHA-256 e abre a tela de instalação do Android (que sempre pede
 * para a pessoa tocar em "Instalar"). O APK é assinado com a mesma chave, então instala por cima
 * mantendo chave da Groq, dicionário e configurações.
 */
object UpdateManager {
    private const val VERSION_URL = "https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/version-android.json"
    const val DOWNLOAD_PAGE = "https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/VoxIA-Android.apk"
    const val PREF_AUTO = "auto_update_check"
    private const val PREF_LAST_CHECK = "last_update_check_ms"
    private const val PREF_NOTIFIED = "update_notified_code"
    private const val CHECK_EVERY_MS = 20 * 60 * 60 * 1000L // no máximo a cada 20 horas
    private const val CHANNEL_ID = "voxia_updates"
    private const val NOTIFICATION_ID = 4301

    data class UpdateInfo(val versionCode: Long, val versionName: String, val url: String, val sha256: String)

    private val client = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(120, TimeUnit.SECONDS)
        .build()

    private fun prefs(context: Context) = context.getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)

    fun currentVersionCode(context: Context): Long = try {
        PackageInfoCompat.getLongVersionCode(context.packageManager.getPackageInfo(context.packageName, 0))
    } catch (e: Exception) {
        0L
    }

    fun currentVersionName(context: Context): String = try {
        context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: ""
    } catch (e: Exception) {
        ""
    }

    fun isAutoCheckEnabled(context: Context) = prefs(context).getBoolean(PREF_AUTO, true)

    fun isAutoCheckDue(context: Context): Boolean {
        if (!isAutoCheckEnabled(context)) return false
        val last = prefs(context).getLong(PREF_LAST_CHECK, 0L)
        return System.currentTimeMillis() - last >= CHECK_EVERY_MS
    }

    /** Versão nova disponível, ou null (já atualizado ou sem internet). */
    suspend fun check(context: Context): UpdateInfo? = withContext(Dispatchers.IO) {
        try {
            val request = Request.Builder().url(VERSION_URL).header("Cache-Control", "no-cache").build()
            val body = client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return@withContext null
                response.body?.string() ?: return@withContext null
            }
            val json = Gson().fromJson(body, JsonObject::class.java)
            val info = UpdateInfo(
                versionCode = json.get("versionCode").asLong,
                versionName = json.get("versionName")?.asString ?: "",
                url = json.get("url").asString,
                sha256 = json.get("sha256").asString.trim().lowercase()
            )
            prefs(context).edit().putLong(PREF_LAST_CHECK, System.currentTimeMillis()).apply()
            val current = currentVersionCode(context)
            Log.i("UpdateManager", "Busca de atualização: instalado $current, publicado ${info.versionCode}.")
            if (info.versionCode > current && info.sha256.length == 64) info else null
        } catch (e: Exception) {
            Log.w("UpdateManager", "Busca de atualização falhou: ${e.message}")
            null
        }
    }

    /** Baixa o APK e confere o SHA-256. Devolve o arquivo, ou null se falhar. */
    suspend fun download(context: Context, info: UpdateInfo): File? = withContext(Dispatchers.IO) {
        val dir = File(context.cacheDir, "updates").apply { mkdirs() }
        dir.listFiles()?.forEach { it.delete() } // APKs de atualizações anteriores
        val file = File(dir, "VoxIA-${info.versionCode}.apk")
        try {
            val digest = MessageDigest.getInstance("SHA-256")
            client.newCall(Request.Builder().url(info.url).build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext null
                val input = response.body?.byteStream() ?: return@withContext null
                file.outputStream().use { out ->
                    val buffer = ByteArray(64 * 1024)
                    while (true) {
                        val n = input.read(buffer)
                        if (n < 0) break
                        digest.update(buffer, 0, n)
                        out.write(buffer, 0, n)
                    }
                }
            }
            val sha = digest.digest().joinToString("") { "%02x".format(it) }
            if (sha != info.sha256) {
                Log.e("UpdateManager", "APK baixado não confere com o SHA-256 publicado; descartado.")
                file.delete()
                return@withContext null
            }
            file
        } catch (e: Exception) {
            Log.w("UpdateManager", "Download da atualização falhou: ${e.message}")
            file.delete()
            null
        }
    }

    /** Abre a tela de instalação do Android para o APK baixado. */
    fun install(context: Context, apk: File): Boolean = try {
        val uri = FileProvider.getUriForFile(context, context.packageName + ".fileprovider", apk)
        val intent = Intent(Intent.ACTION_VIEW)
            .setDataAndType(uri, "application/vnd.android.package-archive")
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(intent)
        true
    } catch (e: Exception) {
        Log.e("UpdateManager", "Não foi possível abrir o instalador", e)
        false
    }

    /** Notificação "versão nova" (uma vez por versão). Tocar abre o VoxIA já atualizando. */
    fun notifyIfNew(context: Context, info: UpdateInfo) {
        val p = prefs(context)
        if (p.getLong(PREF_NOTIFIED, 0L) >= info.versionCode) return
        val manager = NotificationManagerCompat.from(context)
        if (!manager.areNotificationsEnabled()) return
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(CHANNEL_ID, "Atualizações do VoxIA", NotificationManager.IMPORTANCE_DEFAULT)
            channel.setShowBadge(false)
            (context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(channel)
        }
        val tap = PendingIntent.getActivity(
            context, NOTIFICATION_ID,
            Intent(context, SettingsActivity::class.java)
                .putExtra(SettingsActivity.EXTRA_START_UPDATE, true)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val version = if (info.versionName.isNotBlank()) " ${info.versionName}" else ""
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_refresh)
            .setContentTitle("Versão nova do VoxIA$version")
            .setContentText("Toque para atualizar. Sua chave e seu dicionário continuam iguais.")
            .setContentIntent(tap)
            .setAutoCancel(true)
            .build()
        try {
            manager.notify(NOTIFICATION_ID, notification)
            p.edit().putLong(PREF_NOTIFIED, info.versionCode).apply()
        } catch (e: SecurityException) {
            Log.w("UpdateManager", "Notificação de atualização bloqueada: ${e.message}")
        }
    }
}
