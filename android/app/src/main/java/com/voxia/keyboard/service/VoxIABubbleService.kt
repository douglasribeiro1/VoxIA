package com.voxia.keyboard.service

import android.Manifest
import android.accessibilityservice.AccessibilityService
import android.annotation.SuppressLint
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.content.res.ColorStateList
import android.content.res.Configuration
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.Rect
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.provider.Settings
import android.util.Log
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import android.view.accessibility.AccessibilityWindowInfo
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.Toast
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.voxia.keyboard.R
import com.voxia.keyboard.audio.AudioRecorder
import com.voxia.keyboard.data.ApiKeyStore
import com.voxia.keyboard.ui.SettingsActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.File
import kotlin.math.hypot

/**
 * Balão flutuante de ditado, no estilo do Wispr Flow: aparece quando um teclado qualquer
 * (Gboard, Samsung...) está aberto numa caixa de texto e some quando o teclado fecha.
 * Toque = gravar / parar. Arrastar = mudar de lugar. Toque longo = abrir o VoxIA
 * (ou, com um áudio guardado, descartá-lo).
 */
class VoxIABubbleService : AccessibilityService() {

    private enum class State { IDLE, RECORDING, PROCESSING, PENDING }

    private val scope = CoroutineScope(Dispatchers.Main + SupervisorJob())
    private val handler = Handler(Looper.getMainLooper())

    private lateinit var windowManager: WindowManager
    private lateinit var prefs: SharedPreferences
    private lateinit var audioRecorder: AudioRecorder
    private lateinit var dictationEngine: DictationEngine
    private lateinit var apiKeyStore: ApiKeyStore

    private var bubble: FrameLayout? = null
    private var bubbleParams: WindowManager.LayoutParams? = null
    private var circleView: View? = null
    private var iconView: ImageView? = null
    private var progressView: ProgressBar? = null
    private var pulseView: View? = null
    private var bubbleShown = false
    private var dragging = false

    private var state = State.IDLE
    private var targetNode: AccessibilityNodeInfo? = null
    private var fieldContext = ""
    private var pulseJob: Job? = null
    private var imeTop = -1 // topo do teclado na tela; -1 = teclado fechado

    private val refreshRunnable = Runnable { refreshBubble() }

    private val prefsListener = SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
        if (key == PREF_ENABLED) refreshBubble()
    }

    private var pendingAudioFile: File?
        get() = prefs.getString(PREF_PENDING, null)?.let { File(it) }?.takeIf { it.exists() }
        set(value) {
            prefs.edit().putString(PREF_PENDING, value?.absolutePath).apply()
        }

    override fun onServiceConnected() {
        super.onServiceConnected()
        windowManager = getSystemService(Context.WINDOW_SERVICE) as WindowManager
        prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
        prefs.registerOnSharedPreferenceChangeListener(prefsListener)
        audioRecorder = AudioRecorder(this)
        dictationEngine = DictationEngine(this)
        apiKeyStore = ApiKeyStore(this)
        createBubble()
        setState(if (pendingAudioFile != null) State.PENDING else State.IDLE)
        refreshBubble()
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        // O teclado abre/fecha com animação: espera assentar antes de decidir
        handler.removeCallbacks(refreshRunnable)
        handler.postDelayed(refreshRunnable, 150)
    }

    override fun onInterrupt() {}

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        if (bubbleShown) {
            positionBubble()
            updateLayout()
        }
    }

    override fun onDestroy() {
        handler.removeCallbacksAndMessages(null)
        if (::prefs.isInitialized) prefs.unregisterOnSharedPreferenceChangeListener(prefsListener)
        if (::audioRecorder.isInitialized && audioRecorder.isRecordingNow()) audioRecorder.cancelRecording()
        leaveForeground()
        hideBubble()
        scope.cancel()
        super.onDestroy()
    }

    // ---------- Quando mostrar ----------

    private fun refreshBubble() {
        if (bubble == null) return
        imeTop = findImeTop()
        val wanted = when (state) {
            State.RECORDING, State.PROCESSING -> true
            State.IDLE, State.PENDING -> prefs.getBoolean(PREF_ENABLED, true) &&
                imeTop >= 0 &&
                !isVoxiaKeyboardActive() &&
                findFocus(AccessibilityNodeInfo.FOCUS_INPUT)?.isPassword != true
        }
        if (wanted) showBubble() else hideBubble()
    }

    private fun findImeTop(): Int {
        return try {
            val rect = Rect()
            for (w in windows) {
                if (w.type == AccessibilityWindowInfo.TYPE_INPUT_METHOD) {
                    w.getBoundsInScreen(rect)
                    if (rect.height() > 0) return rect.top
                }
            }
            -1
        } catch (e: Exception) {
            -1
        }
    }

    /** O teclado VoxIA já tem o próprio microfone; o balão ali só atrapalharia. */
    private fun isVoxiaKeyboardActive(): Boolean {
        val current = Settings.Secure.getString(contentResolver, Settings.Secure.DEFAULT_INPUT_METHOD) ?: return false
        return current.startsWith("$packageName/")
    }

    // ---------- Balão ----------

    @SuppressLint("ClickableViewAccessibility")
    private fun createBubble() {
        val outer = dp(64)
        val inner = dp(50)

        val root = FrameLayout(this)

        val pulse = View(this).apply {
            setBackgroundResource(R.drawable.bg_mic_pulse)
            visibility = View.INVISIBLE
        }
        root.addView(pulse, FrameLayout.LayoutParams(outer, outer, Gravity.CENTER))

        val circle = View(this).apply {
            elevation = dp(6).toFloat()
        }
        root.addView(circle, FrameLayout.LayoutParams(inner, inner, Gravity.CENTER))

        val icon = ImageView(this).apply {
            elevation = dp(7).toFloat()
            scaleType = ImageView.ScaleType.CENTER_INSIDE
        }
        root.addView(icon, FrameLayout.LayoutParams(dp(26), dp(26), Gravity.CENTER))

        val progress = ProgressBar(this).apply {
            isIndeterminate = true
            indeterminateTintList = ColorStateList.valueOf(Color.WHITE)
            elevation = dp(7).toFloat()
            visibility = View.GONE
        }
        root.addView(progress, FrameLayout.LayoutParams(dp(28), dp(28), Gravity.CENTER))

        root.contentDescription = "Ditado VoxIA"
        root.setOnTouchListener(BubbleTouchListener())

        bubbleParams = WindowManager.LayoutParams(
            outer,
            outer,
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            // Nunca pega o foco: o teclado e o cursor continuam no campo de texto
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.TOP or Gravity.START
        }

        bubble = root
        pulseView = pulse
        circleView = circle
        iconView = icon
        progressView = progress
    }

    private fun showBubble() {
        val view = bubble ?: return
        if (!dragging) positionBubble()
        try {
            if (!bubbleShown) {
                windowManager.addView(view, bubbleParams)
                bubbleShown = true
            } else {
                updateLayout()
            }
        } catch (e: Exception) {
            Log.e("VoxIABubble", "Não foi possível mostrar o balão", e)
        }
    }

    private fun hideBubble() {
        val view = bubble ?: return
        if (!bubbleShown) return
        try {
            windowManager.removeView(view)
        } catch (ignored: Exception) {}
        bubbleShown = false
        dragging = false
    }

    private fun updateLayout() {
        try {
            if (bubbleShown) windowManager.updateViewLayout(bubble, bubbleParams)
        } catch (ignored: Exception) {}
    }

    /** Encostado na borda escolhida, na altura salva, mas sempre acima do teclado. */
    private fun positionBubble() {
        val params = bubbleParams ?: return
        val dm = resources.displayMetrics
        val size = params.width
        val margin = dp(4)

        params.x = if (prefs.getInt(PREF_SIDE, 1) == 1) dm.widthPixels - size - margin else margin

        val bottomLimit = if (imeTop > 0) imeTop - size - dp(6) else dm.heightPixels - size - dp(96)
        var y = prefs.getInt(PREF_Y, -1)
        if (y < 0 || y > bottomLimit) y = bottomLimit
        params.y = y.coerceAtLeast(dp(32))
    }

    private inner class BubbleTouchListener : View.OnTouchListener {
        private var downX = 0f
        private var downY = 0f
        private var startX = 0
        private var startY = 0
        private var longPressed = false
        private val slop = ViewConfiguration.get(this@VoxIABubbleService).scaledTouchSlop
        private val longPress = Runnable {
            longPressed = true
            vibrate()
            onBubbleLongPress()
        }

        override fun onTouch(v: View, e: MotionEvent): Boolean {
            val params = bubbleParams ?: return false
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = e.rawX
                    downY = e.rawY
                    startX = params.x
                    startY = params.y
                    dragging = false
                    longPressed = false
                    handler.postDelayed(longPress, ViewConfiguration.getLongPressTimeout().toLong() + 150)
                }
                MotionEvent.ACTION_MOVE -> {
                    val dx = e.rawX - downX
                    val dy = e.rawY - downY
                    if (!dragging && !longPressed && hypot(dx, dy) > slop) {
                        dragging = true
                        handler.removeCallbacks(longPress)
                    }
                    if (dragging) {
                        params.x = startX + dx.toInt()
                        params.y = startY + dy.toInt()
                        updateLayout()
                    }
                }
                MotionEvent.ACTION_UP -> {
                    handler.removeCallbacks(longPress)
                    if (dragging) {
                        val side = if (params.x + params.width / 2 > resources.displayMetrics.widthPixels / 2) 1 else 0
                        prefs.edit().putInt(PREF_SIDE, side).putInt(PREF_Y, params.y).apply()
                        dragging = false
                        positionBubble()
                        updateLayout()
                    } else if (!longPressed) {
                        onBubbleTap()
                    }
                }
                MotionEvent.ACTION_CANCEL -> {
                    handler.removeCallbacks(longPress)
                    dragging = false
                }
            }
            return true
        }
    }

    private fun onBubbleTap() {
        when (state) {
            State.IDLE -> startDictation()
            State.RECORDING -> stopDictation()
            State.PROCESSING -> toast("Processando o ditado…")
            State.PENDING -> pendingAudioFile?.let { processAudio(it) } ?: setState(State.IDLE)
        }
    }

    private fun onBubbleLongPress() {
        when (state) {
            State.PENDING -> {
                pendingAudioFile?.delete()
                pendingAudioFile = null
                setState(State.IDLE)
                toast("Áudio guardado descartado.")
            }
            State.IDLE -> openSettings()
            else -> {}
        }
    }

    private fun setState(newState: State) {
        state = newState
        val circle = circleView ?: return
        val icon = iconView ?: return
        val progress = progressView ?: return
        when (newState) {
            State.IDLE -> {
                circle.setBackgroundResource(R.drawable.bg_mic_button)
                icon.setImageResource(R.drawable.ic_mic)
                icon.visibility = View.VISIBLE
                progress.visibility = View.GONE
            }
            State.RECORDING -> {
                circle.setBackgroundResource(R.drawable.bg_mic_recording)
                icon.setImageResource(R.drawable.ic_stop)
                icon.visibility = View.VISIBLE
                progress.visibility = View.GONE
            }
            State.PROCESSING -> {
                circle.setBackgroundResource(R.drawable.bg_bubble_busy)
                icon.visibility = View.GONE
                progress.visibility = View.VISIBLE
            }
            State.PENDING -> {
                circle.setBackgroundResource(R.drawable.bg_bubble_busy)
                icon.setImageResource(R.drawable.ic_refresh)
                icon.visibility = View.VISIBLE
                progress.visibility = View.GONE
            }
        }
        if (newState != State.RECORDING) stopPulse()
    }

    // ---------- Ditado ----------

    private fun startDictation() {
        if (apiKeyStore.get().isBlank()) {
            toast("Configure a chave da Groq no app VoxIA.")
            openSettings()
            return
        }
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            toast("Permita o uso do microfone no app VoxIA.")
            openSettings()
            return
        }

        val focus = findFocus(AccessibilityNodeInfo.FOCUS_INPUT)
        targetNode = focus
        fieldContext = readFieldContext(focus)

        enterForeground()
        if (!audioRecorder.startRecording()) {
            leaveForeground()
            toast("⚠ Erro ao acessar o microfone.")
            return
        }
        vibrate()
        setState(State.RECORDING)
        startPulse()
    }

    private fun stopDictation() {
        vibrate()
        setState(State.PROCESSING)
        scope.launch {
            val file = audioRecorder.stopRecording()
            val muted = audioRecorder.lastRecordingWasMuted()
            leaveForeground()

            if (file == null) {
                toast("Fale um pouco mais (áudio vazio).")
                setState(State.IDLE)
                refreshBubble()
                return@launch
            }
            if (muted) {
                file.delete()
                toast("O Android bloqueou o microfone para o balão. Abra o VoxIA e veja a seção do balão.", long = true)
                setState(State.IDLE)
                refreshBubble()
                return@launch
            }

            pendingAudioFile?.takeIf { it != file }?.delete()
            pendingAudioFile = file
            processAudio(file)
        }
    }

    private fun processAudio(file: File) {
        setState(State.PROCESSING)
        scope.launch {
            val result = dictationEngine.process(file, apiKeyStore.get(), fieldContext)
            result.onSuccess { text ->
                pendingAudioFile = null
                try { file.delete() } catch (ignored: Exception) {}
                if (text.isBlank()) {
                    toast("Nenhuma fala detectada.")
                } else {
                    insertText("$text ")
                }
                fieldContext = ""
                targetNode = null
                setState(State.IDLE)
            }.onFailure { error ->
                Log.e("VoxIABubble", "Falha ao processar o ditado", error)
                setState(State.PENDING)
                toast("Sem conexão: o áudio foi guardado. Toque no balão para reenviar.", long = true)
            }
            refreshBubble()
        }
    }

    /** Até 400 caracteres antes do cursor, para ajudar a grafia. Campos de senha nunca são lidos. */
    private fun readFieldContext(node: AccessibilityNodeInfo?): String {
        if (node == null || node.isPassword) return ""
        return try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && node.isShowingHintText) return ""
            val text = node.text?.toString() ?: return ""
            val cursor = node.textSelectionStart
            val before = if (cursor in 0..text.length) text.substring(0, cursor) else text
            before.takeLast(400).replace(Regex("""\s+"""), " ").trim()
        } catch (e: Exception) {
            ""
        }
    }

    /**
     * Cola no cursor do campo onde o ditado começou (ou no campo com foco agora). Colar é o caminho
     * mais seguro: não reescreve o resto do campo, então nunca apaga o que já estava escrito.
     */
    private fun insertText(text: String) {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val previousClip = try { clipboard.primaryClip } catch (e: Exception) { null }
        clipboard.setPrimaryClip(ClipData.newPlainText("VoxIA", text))

        val node = targetNode?.takeIf { it.refresh() } ?: findFocus(AccessibilityNodeInfo.FOCUS_INPUT)
        var inserted = false
        if (node != null && !node.isPassword) {
            if (!node.isFocused) node.performAction(AccessibilityNodeInfo.ACTION_FOCUS)
            inserted = node.performAction(AccessibilityNodeInfo.ACTION_PASTE)
            if (!inserted && isFieldEmpty(node)) {
                // Campo vazio: escrever o texto inteiro não arrisca apagar nada
                val args = Bundle().apply {
                    putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, text)
                }
                inserted = node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)
            }
        }

        if (inserted) {
            // Devolve à área de transferência o que estava lá antes, quando o Android deixa ler
            if (previousClip != null) {
                handler.postDelayed({
                    try { clipboard.setPrimaryClip(previousClip) } catch (ignored: Exception) {}
                }, 800)
            }
        } else {
            toast("Texto copiado. Toque e segure no campo e escolha Colar.", long = true)
        }
    }

    private fun isFieldEmpty(node: AccessibilityNodeInfo): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && node.isShowingHintText) return true
        return node.text.isNullOrEmpty()
    }

    // ---------- Primeiro plano durante a gravação ----------

    /**
     * Enquanto grava, o serviço fica em primeiro plano (com notificação) para o Android
     * não silenciar o microfone de um app que não está na tela.
     */
    private fun enterForeground() {
        try {
            val notification = buildRecordingNotification()
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE)
            } else {
                startForeground(NOTIFICATION_ID, notification)
            }
        } catch (e: Exception) {
            Log.w("VoxIABubble", "Não foi possível entrar em primeiro plano: ${e.message}")
        }
    }

    private fun leaveForeground() {
        try {
            stopForeground(STOP_FOREGROUND_REMOVE)
        } catch (ignored: Exception) {}
    }

    private fun buildRecordingNotification(): Notification {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            val channel = NotificationChannel(CHANNEL_ID, "Gravação do balão", NotificationManager.IMPORTANCE_LOW)
            channel.setShowBadge(false)
            manager.createNotificationChannel(channel)
        }
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_mic)
            .setContentTitle("VoxIA")
            .setContentText("Gravando ditado…")
            .setOngoing(true)
            .setSilent(true)
            .build()
    }

    // ---------- Utilidades ----------

    private fun startPulse() {
        val pulse = pulseView ?: return
        pulseJob?.cancel()
        pulse.visibility = View.VISIBLE
        pulseJob = scope.launch {
            var smoothed = 0f
            while (isActive && audioRecorder.isRecordingNow()) {
                val level = audioRecorder.currentLevel()
                // Sobe rápido e desce devagar, para o anel não "piscar"
                smoothed = if (level > smoothed) smoothed + (level - smoothed) * 0.6f else smoothed * 0.85f
                val scale = 0.82f + smoothed * 0.18f
                pulse.scaleX = scale
                pulse.scaleY = scale
                pulse.alpha = 0.35f + smoothed * 0.65f
                delay(40)
            }
        }
    }

    private fun stopPulse() {
        pulseJob?.cancel()
        pulseJob = null
        pulseView?.apply {
            visibility = View.INVISIBLE
            scaleX = 1f
            scaleY = 1f
        }
    }

    private fun openSettings() {
        try {
            startActivity(Intent(this, SettingsActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        } catch (e: Exception) {
            Log.w("VoxIABubble", "Não foi possível abrir as configurações: ${e.message}")
        }
    }

    private fun toast(message: String, long: Boolean = false) {
        Toast.makeText(this, message, if (long) Toast.LENGTH_LONG else Toast.LENGTH_SHORT).show()
    }

    private fun vibrate() {
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                val vm = getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager
                vm?.defaultVibrator?.vibrate(VibrationEffect.createOneShot(35, VibrationEffect.DEFAULT_AMPLITUDE))
            } else {
                @Suppress("DEPRECATION")
                val vibrator = getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
                vibrator?.vibrate(VibrationEffect.createOneShot(35, VibrationEffect.DEFAULT_AMPLITUDE))
            }
        } catch (ignored: Exception) {}
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    companion object {
        const val PREF_ENABLED = "bubble_enabled"
        private const val PREF_SIDE = "bubble_side"
        private const val PREF_Y = "bubble_y"
        private const val PREF_PENDING = "bubble_pending_audio_path"
        private const val CHANNEL_ID = "voxia_bubble_recording"
        private const val NOTIFICATION_ID = 4101

        /** Se o serviço de acessibilidade do balão está ligado nas configurações do Android. */
        fun isEnabled(context: Context): Boolean {
            val enabled = Settings.Secure.getString(
                context.contentResolver,
                Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES
            ) ?: return false
            return enabled.split(':').any {
                val cn = android.content.ComponentName.unflattenFromString(it)
                cn != null && cn.packageName == context.packageName && cn.className == VoxIABubbleService::class.java.name
            }
        }
    }
}
