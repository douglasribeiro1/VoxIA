package com.voxia.keyboard.service

import android.Manifest
import android.accessibilityservice.AccessibilityService
import android.annotation.SuppressLint
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.ClipData
import android.content.ClipboardManager
import android.content.ComponentName
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
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.voxia.keyboard.R
import com.voxia.keyboard.audio.AudioRecorder
import com.voxia.keyboard.data.ApiKeyStore
import com.voxia.keyboard.data.CorrectionLearner
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.ui.AddWordActivity
import com.voxia.keyboard.ui.SettingsActivity
import com.voxia.keyboard.update.UpdateManager
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
 * (ou, com um áudio guardado, descartá-lo). Enquanto grava ou processa, um "X" ao lado cancela.
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
    private var cancelView: View? = null
    private var cancelParams: WindowManager.LayoutParams? = null
    private var cancelShown = false
    private var workJob: Job? = null
    private var bubbleShown = false
    private var dragging = false

    private var state = State.IDLE
    private var targetNode: AccessibilityNodeInfo? = null
    private var fieldContext = ""
    private var pulseJob: Job? = null
    private var imeTop = -1 // topo do teclado na tela; -1 = teclado fechado

    private val refreshRunnable = Runnable { refreshBubble() }

    /**
     * Campo onde o último ditado foi inserido, observado por alguns minutos: se a pessoa corrigir
     * à mão uma palavra do ditado, o VoxIA sugere ensiná-la ao dicionário. Só o texto desse campo
     * é lido, só no aparelho, e nada é enviado para a internet.
     */
    private class LearnWatch(
        val node: AccessibilityNodeInfo,
        val packageName: CharSequence?,
        val baseline: String,
        val start: Int,
        val end: Int,
        val until: Long,
        val suggested: MutableSet<String> = mutableSetOf()
    )
    private var learnWatch: LearnWatch? = null
    private val learnRunnable = Runnable { checkCorrections() }
    private var learnNotificationId = 0

    /** Busca de atualização uma vez por dia enquanto o balão está ligado (avisa por notificação). */
    private val updateRunnable = object : Runnable {
        override fun run() {
            if (UpdateManager.isAutoCheckDue(this@VoxIABubbleService) && state != State.RECORDING) {
                scope.launch {
                    UpdateManager.check(this@VoxIABubbleService)?.let { UpdateManager.notifyIfNew(this@VoxIABubbleService, it) }
                }
            }
            handler.postDelayed(this, 60 * 60 * 1000L)
        }
    }

    private val prefsListener = SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
        when (key) {
            PREF_ENABLED -> {
                refreshBubble()
                VoxIABubbleTileService.requestUpdate(this)
            }
            PREF_COLOR, PREF_ALPHA -> setState(state)
            PREF_SIZE -> rebuildBubble()
        }
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
        running = true
        VoxIABubbleTileService.requestUpdate(this)
        handler.postDelayed(updateRunnable, 2 * 60 * 1000L)
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        if (event?.eventType == AccessibilityEvent.TYPE_VIEW_TEXT_CHANGED) {
            // Digitação: só interessa para perceber correções no campo do último ditado
            val watch = learnWatch ?: return
            if (System.currentTimeMillis() > watch.until) {
                learnWatch = null
                return
            }
            if (event.packageName == watch.packageName) {
                handler.removeCallbacks(learnRunnable)
                handler.postDelayed(learnRunnable, LEARN_IDLE_MS)
            }
            return
        }
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
        running = false
        VoxIABubbleTileService.requestUpdate(this)
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
        val inner = dp(bubbleDiameterDp(prefs.getInt(PREF_SIZE, 1)))
        val outer = inner + dp(14)
        val iconSize = inner * 52 / 100

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
        root.addView(icon, FrameLayout.LayoutParams(iconSize, iconSize, Gravity.CENTER))

        val progress = ProgressBar(this).apply {
            isIndeterminate = true
            indeterminateTintList = ColorStateList.valueOf(Color.WHITE)
            elevation = dp(7).toFloat()
            visibility = View.GONE
        }
        root.addView(progress, FrameLayout.LayoutParams(iconSize + dp(2), iconSize + dp(2), Gravity.CENTER))

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

        val cancelSize = dp(34)
        cancelView = ImageView(this).apply {
            setImageResource(R.drawable.ic_close)
            setBackgroundResource(R.drawable.bg_bubble_busy)
            scaleType = ImageView.ScaleType.CENTER_INSIDE
            setPadding(dp(7), dp(7), dp(7), dp(7))
            elevation = dp(6).toFloat()
            contentDescription = "Cancelar ditado"
            setOnClickListener {
                vibrate()
                cancelDictation()
            }
        }
        cancelParams = WindowManager.LayoutParams(
            cancelSize,
            cancelSize,
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
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

    /** Tamanho mudou nas configurações: recria o balão mantendo o estado atual. */
    private fun rebuildBubble() {
        val wasShown = bubbleShown
        hideBubble()
        hideCancel()
        createBubble()
        setState(state)
        if (wasShown) showBubble()
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
        updateCancel()
    }

    private fun hideBubble() {
        val view = bubble ?: return
        if (!bubbleShown) return
        try {
            windowManager.removeView(view)
        } catch (ignored: Exception) {}
        bubbleShown = false
        dragging = false
        hideCancel()
    }

    // ---------- Botão "X" de cancelar, ao lado do balão (lado de dentro da tela) ----------

    private fun updateCancel() {
        val view = cancelView ?: return
        val params = cancelParams ?: return
        val bp = bubbleParams ?: return
        val wanted = bubbleShown && (state == State.RECORDING || state == State.PROCESSING)
        if (!wanted) {
            hideCancel()
            return
        }
        val onRight = bp.x + bp.width / 2 > resources.displayMetrics.widthPixels / 2
        params.x = if (onRight) bp.x - params.width - dp(4) else bp.x + bp.width + dp(4)
        params.y = bp.y + (bp.height - params.height) / 2
        try {
            if (!cancelShown) {
                windowManager.addView(view, params)
                cancelShown = true
            } else {
                windowManager.updateViewLayout(view, params)
            }
        } catch (e: Exception) {
            Log.w("VoxIABubble", "Não foi possível mostrar o botão de cancelar: ${e.message}")
        }
    }

    private fun hideCancel() {
        val view = cancelView ?: return
        if (!cancelShown) return
        try {
            windowManager.removeView(view)
        } catch (ignored: Exception) {}
        cancelShown = false
    }

    private fun updateLayout() {
        try {
            if (bubbleShown) windowManager.updateViewLayout(bubble, bubbleParams)
        } catch (ignored: Exception) {}
        updateCancel()
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
                DictationEngine.discard(pendingAudioFile)
                pendingAudioFile = null
                setState(State.IDLE)
                toast("Áudio guardado descartado.")
            }
            State.RECORDING, State.PROCESSING -> cancelDictation()
            State.IDLE -> {
                val selected = selectedWord()
                if (selected.isNotEmpty()) AddWordActivity.open(this, selected) else openSettings()
            }
        }
    }

    private fun setState(newState: State) {
        state = newState
        val circle = circleView ?: return
        val icon = iconView ?: return
        val progress = progressView ?: return
        when (newState) {
            State.IDLE -> {
                circle.background = bubbleBackground(prefs.getInt(PREF_COLOR, BUBBLE_COLORS[0]))
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
        bubble?.alpha = when (newState) {
            State.IDLE, State.PENDING -> prefs.getInt(PREF_ALPHA, 100).coerceIn(30, 100) / 100f
            else -> 1f
        }
        if (newState != State.RECORDING) stopPulse()
        updateCancel()
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
        dictationEngine.startLive(audioRecorder, apiKeyStore.get(), fieldContext, scope)
        vibrate()
        setState(State.RECORDING)
        startPulse()
    }

    private fun stopDictation() {
        vibrate()
        setState(State.PROCESSING)
        workJob = scope.launch {
            val file = audioRecorder.stopRecording()
            val muted = audioRecorder.lastRecordingWasMuted()
            leaveForeground()

            if (file == null) {
                dictationEngine.cancelLive()
                toast("Fale um pouco mais (áudio vazio).")
                setState(State.IDLE)
                refreshBubble()
                return@launch
            }
            if (muted) {
                dictationEngine.cancelLive()
                file.delete()
                toast("O Android bloqueou o microfone para o balão. Abra o VoxIA e veja a seção do balão.", long = true)
                setState(State.IDLE)
                refreshBubble()
                return@launch
            }

            val prepared = dictationEngine.prepareRecording(file)
            if (prepared == null) {
                toast("Nenhuma fala detectada.")
                setState(State.IDLE)
                refreshBubble()
                return@launch
            }

            pendingAudioFile?.takeIf { it != prepared }?.let { DictationEngine.discard(it) }
            pendingAudioFile = prepared
            processAudio(prepared)
        }
    }

    private fun processAudio(file: File) {
        setState(State.PROCESSING)
        workJob = scope.launch {
            val result = dictationEngine.process(file, apiKeyStore.get(), fieldContext)
            result.onSuccess { text ->
                pendingAudioFile = null
                DictationEngine.discard(file)
                if (text.isBlank()) {
                    toast("Nenhuma fala detectada.")
                } else {
                    insertText("$text ")
                    dictationEngine.lastCorrectionIssue?.let { toast("Texto sem correção da IA: $it.", long = true) }
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
    /** "X" ou toque longo durante o ditado: descarta tudo sem inserir nada. */
    private fun cancelDictation() {
        when (state) {
            State.RECORDING -> {
                dictationEngine.cancelLive()
                audioRecorder.cancelRecording()
                leaveForeground()
            }
            State.PROCESSING -> {
                workJob?.cancel()
                dictationEngine.cancelLive()
                if (audioRecorder.isRecordingNow()) audioRecorder.cancelRecording()
                leaveForeground()
                DictationEngine.discard(pendingAudioFile)
                pendingAudioFile = null
            }
            else -> return
        }
        workJob = null
        fieldContext = ""
        targetNode = null
        setState(State.IDLE)
        toast("Ditado cancelado.")
        refreshBubble()
    }

    private fun readFieldContext(node: AccessibilityNodeInfo?): String {
        if (node == null || node.isPassword) return ""
        if (!prefs.getBoolean(PREF_SEND_CONTEXT, DEFAULT_SEND_CONTEXT)) return ""
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
     * preferido porque não reescreve o resto do campo. Como alguns apps aceitam o pedido de colar
     * e não fazem nada (ou recusam), o resultado é conferido; se o campo não mudou, o texto é
     * escrito diretamente na posição do cursor.
     */
    private suspend fun insertText(text: String) {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val previousClip = try { clipboard.primaryClip } catch (e: Exception) { null }
        clipboard.setPrimaryClip(ClipData.newPlainText("VoxIA", text))

        val node = targetNode?.takeIf { it.refresh() } ?: findFocus(AccessibilityNodeInfo.FOCUS_INPUT)
        var inserted = false
        if (node != null && !node.isPassword) {
            if (!node.isFocused) node.performAction(AccessibilityNodeInfo.ACTION_FOCUS)
            val before = currentText(node)
            delay(120) // dá tempo de o app perceber que a área de transferência mudou

            inserted = pasteAndCheck(node, before)
            if (!inserted) {
                delay(250)
                inserted = changedSince(node, before) || pasteAndCheck(node, before)
            }
            if (!inserted) {
                inserted = changedSince(node, before) || setTextAtCursor(node, before, text)
                if (inserted) Log.i("VoxIABubble", "Colar não funcionou neste app; texto escrito direto no campo.")
            }
        }

        if (inserted && node != null) startLearnWatch(node, text)
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

    private fun startLearnWatch(node: AccessibilityNodeInfo, inserted: String) {
        learnWatch = null
        handler.removeCallbacks(learnRunnable)
        if (!prefs.getBoolean(PREF_LEARN, true) || node.isPassword) return
        try {
            node.refresh()
            val after = currentText(node)
            // O ditado ocupa o trecho onde ele aparece no campo (procurado perto do cursor)
            val cursor = node.textSelectionStart.takeIf { it in 0..after.length } ?: after.length
            val body = inserted.trimEnd()
            var start = after.lastIndexOf(body, startIndex = maxOf(0, cursor - body.length))
            if (start < 0) start = after.lastIndexOf(body)
            if (start < 0 || body.isEmpty()) return
            learnWatch = LearnWatch(
                node, node.packageName, after, start, start + body.length,
                System.currentTimeMillis() + LEARN_WINDOW_MS
            )
        } catch (e: Exception) {
            Log.w("VoxIABubble", "Não foi possível observar correções: ${e.message}")
        }
    }

    private fun checkCorrections() {
        val watch = learnWatch ?: return
        try {
            if (!watch.node.refresh() || watch.node.isPassword) {
                learnWatch = null
                return
            }
            val now = currentText(watch.node)
            val found = CorrectionLearner.find(watch.baseline, now, watch.start, watch.end)
            if (found.isEmpty()) return
            val known = DictionaryManager(this).getEntries()
            for (c in found) {
                val key = c.original.lowercase() + "→" + c.replacement.lowercase()
                if (!watch.suggested.add(key)) continue
                val already = known.any {
                    it.original.equals(c.original, ignoreCase = true) &&
                        (it.isVocabularyOnly || it.replacement.equals(c.replacement, ignoreCase = true))
                }
                if (!already) suggestLearning(c)
            }
        } catch (e: Exception) {
            Log.w("VoxIABubble", "Falha ao conferir correções: ${e.message}")
        }
    }

    private fun suggestLearning(c: CorrectionLearner.Correction) {
        val manager = NotificationManagerCompat.from(this)
        val allowed = manager.areNotificationsEnabled() && (Build.VERSION.SDK_INT < 33 ||
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED)
        if (!allowed) {
            toast("Dica: para o VoxIA aprender \"${c.replacement}\", toque e segure no balão com a palavra selecionada.", long = true)
            return
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(LEARN_CHANNEL_ID, "Sugestões do dicionário", NotificationManager.IMPORTANCE_DEFAULT)
            channel.setShowBadge(false)
            (getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(channel)
        }
        val id = LEARN_NOTIFICATION_BASE + (learnNotificationId++ % 20)
        val tap = PendingIntent.getActivity(
            this, id, AddWordActivity.intentFor(this, c.original, c.replacement),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val notification = NotificationCompat.Builder(this, LEARN_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_book)
            .setContentTitle("Ensinar ao VoxIA?")
            .setContentText("Você corrigiu \"${c.original}\" para \"${c.replacement}\". Toque para salvar no dicionário.")
            .setStyle(NotificationCompat.BigTextStyle().bigText(
                "Você corrigiu \"${c.original}\" para \"${c.replacement}\". Toque para salvar no dicionário e o VoxIA acertar das próximas vezes."
            ))
            .setContentIntent(tap)
            .setAutoCancel(true)
            .setTimeoutAfter(LEARN_WINDOW_MS * 2)
            .build()
        try {
            manager.notify(id, notification)
        } catch (e: SecurityException) {
            Log.w("VoxIABubble", "Notificação de sugestão bloqueada: ${e.message}")
        }
    }

    private suspend fun pasteAndCheck(node: AccessibilityNodeInfo, before: String): Boolean {
        if (!node.performAction(AccessibilityNodeInfo.ACTION_PASTE)) return false
        delay(200)
        return changedSince(node, before)
    }

    private fun changedSince(node: AccessibilityNodeInfo, before: String): Boolean {
        node.refresh()
        return currentText(node) != before
    }

    /** Monta o texto do campo com o ditado na posição do cursor (substituindo a seleção, se houver). */
    private fun setTextAtCursor(node: AccessibilityNodeInfo, current: String, text: String): Boolean {
        var start = node.textSelectionStart
        var end = node.textSelectionEnd
        if (start !in 0..current.length || end !in 0..current.length) {
            start = current.length
            end = current.length
        }
        if (start > end) start = end.also { end = start }

        val args = Bundle().apply {
            putCharSequence(
                AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE,
                current.substring(0, start) + text + current.substring(end)
            )
        }
        if (!node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)) return false

        val cursor = start + text.length
        node.performAction(AccessibilityNodeInfo.ACTION_SET_SELECTION, Bundle().apply {
            putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_START_INT, cursor)
            putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_END_INT, cursor)
        })
        return true
    }

    /** Texto real do campo ("" quando só mostra a dica cinza de campo vazio). */
    private fun currentText(node: AccessibilityNodeInfo): String {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && node.isShowingHintText) return ""
        return node.text?.toString() ?: ""
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

    /** Trecho selecionado no campo em foco (nunca de campo de senha); vazio se não há seleção. */
    private fun selectedWord(): String {
        val node = findFocus(AccessibilityNodeInfo.FOCUS_INPUT) ?: return ""
        if (node.isPassword) return ""
        val text = currentText(node)
        val start = minOf(node.textSelectionStart, node.textSelectionEnd)
        val end = maxOf(node.textSelectionStart, node.textSelectionEnd)
        if (start < 0 || end > text.length || start == end) return ""
        return text.substring(start, end).trim()
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
        const val PREF_COLOR = "bubble_color"
        const val PREF_ALPHA = "bubble_alpha"   // 30..100 (%), só quando parado
        const val PREF_SIZE = "bubble_size"     // 0 pequeno, 1 médio, 2 grande

        /** Cores oferecidas para o balão parado (gravando é sempre vermelho; processando, âmbar). */
        val BUBBLE_COLORS = intArrayOf(
            0xFF4338CA.toInt(), // índigo (padrão)
            0xFF0EA5E9.toInt(), // azul
            0xFF10B981.toInt(), // verde
            0xFF8B5CF6.toInt(), // violeta
            0xFFEC4899.toInt(), // rosa
            0xFF475569.toInt()  // grafite
        )

        fun bubbleDiameterDp(size: Int): Int = when (size) {
            0 -> 42
            2 -> 58
            else -> 50
        }

        /** Círculo com leve degradê na cor escolhida; usado no balão e na prévia das configurações. */
        fun bubbleBackground(color: Int): android.graphics.drawable.GradientDrawable {
            val darker = Color.rgb(
                (Color.red(color) * 0.8f).toInt(),
                (Color.green(color) * 0.8f).toInt(),
                (Color.blue(color) * 0.8f).toInt()
            )
            return android.graphics.drawable.GradientDrawable(
                android.graphics.drawable.GradientDrawable.Orientation.TL_BR,
                intArrayOf(color, darker)
            ).apply { shape = android.graphics.drawable.GradientDrawable.OVAL }
        }
        /** Compartilhada com o teclado: enviar o texto já escrito no campo como contexto. */
        // Chave nova (v2) para que todos comecem com o envio desligado, mesmo quem já usava a versão antiga.
        const val PREF_SEND_CONTEXT = "send_field_context_v2"
        const val DEFAULT_SEND_CONTEXT = false
        private const val PREF_SIDE = "bubble_side"
        private const val PREF_Y = "bubble_y"
        private const val PREF_PENDING = "bubble_pending_audio_path"
        private const val CHANNEL_ID = "voxia_bubble_recording"
        /** Sugerir ensinar ao dicionário as palavras que a pessoa corrige depois do ditado. */
        const val PREF_LEARN = "learn_corrections"
        private const val LEARN_CHANNEL_ID = "voxia_learn_suggestions"
        private const val LEARN_NOTIFICATION_BASE = 4200
        private const val LEARN_WINDOW_MS = 5 * 60 * 1000L // observa o campo por 5 minutos
        private const val LEARN_IDLE_MS = 3000L            // confere 3 s depois de a pessoa parar de digitar
        private const val NOTIFICATION_ID = 4101

        /** Se o serviço está de fato rodando neste processo (ligado na Acessibilidade e conectado). */
        @Volatile
        var running = false
            private set

        /**
         * Abre direto a chave do balão na Acessibilidade. Se o aparelho não tiver essa tela,
         * abre a lista de Acessibilidade já destacando o VoxIA.
         */
        fun accessibilitySettingsIntent(context: Context): Intent {
            val component = ComponentName(context, VoxIABubbleService::class.java).flattenToString()
            val details = Intent(ACTION_ACCESSIBILITY_DETAILS)
                .putExtra(Intent.EXTRA_COMPONENT_NAME, component)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            if (details.resolveActivity(context.packageManager) != null) return details
            return Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)
                .putExtra(":settings:fragment_args_key", component)
                .putExtra(":settings:show_fragment_args", Bundle().apply {
                    putString(":settings:fragment_args_key", component)
                })
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }

        private const val ACTION_ACCESSIBILITY_DETAILS = "android.settings.ACCESSIBILITY_DETAILS_SETTINGS"

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
