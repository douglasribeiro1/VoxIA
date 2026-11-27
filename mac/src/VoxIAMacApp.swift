import Foundation
import AppKit
import AVFoundation
import ApplicationServices

@main
struct VoxIAMacMain {
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory) // só na barra de menus, sem ícone no Dock
        app.run()
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var trayMenu: NSMenu!
    private var recordItem: NSMenuItem!
    private var updateItem: NSMenuItem!
    private let audioRecorder = AudioRecorderService()
    private let hud = FloatingHudOverlay()
    private var activeTargetContext: TargetContext?
    private var live: LiveTranscriber?
    private var finishing = false
    private var pendingUpdate: UpdateService.UpdateInfo?
    private var updateTimer: Timer?
    private var mainWindow: MainWindowController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        SettingsService.load()
        PendingAudioRepository.loadQueue()
        HistoryRepository.load()
        DictionaryService.load()
        MicrophoneLevelService.restoreAfterCrash()

        setupStatusItem()
        setupGlobalHotkey()
        RetryService.onCorrectionWarning = { [weak self] reason in
            self?.showNotification(title: "Texto colado sem a correção da IA",
                                   text: "Foi usado o texto da transcrição, sem revisão nem formatação. Motivo: \(reason).")
        }
        RetryService.start()
        requestPermissions()
        startUpdateChecks()

        Logger.info("VoxIA macOS (build \(UpdateService.currentBuild)) initialized in the menu bar.")
    }

    func applicationWillTerminate(_ notification: Notification) {
        MicrophoneLevelService.restore()
        GlobalHotkeyService.stop()
    }

    // MARK: - Permissões

    private func requestPermissions() {
        AVCaptureDevice.requestAccess(for: .audio) { granted in
            if !granted { Logger.warning("Microphone permission denied.") }
        }
        // Acessibilidade: necessária para o atalho global e para colar o texto no app ativo
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        if !AXIsProcessTrustedWithOptions(options) {
            Logger.warning("Accessibility permission not granted yet.")
        }
        if SecureCredentialService.getApiKey().isEmpty {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in self?.openSettings() }
        }
    }

    // MARK: - Menu

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            button.title = "🎙"
            button.font = NSFont.systemFont(ofSize: 14)
        }

        trayMenu = NSMenu()
        let build = UpdateService.currentBuild
        let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
        let versionItem = NSMenuItem(title: build > 0 ? "VoxIA \(version) (build \(build))" : "VoxIA (compilado à mão)", action: nil, keyEquivalent: "")
        versionItem.isEnabled = false
        trayMenu.addItem(versionItem)
        trayMenu.addItem(NSMenuItem.separator())
        trayMenu.addItem(NSMenuItem(title: "Abrir o VoxIA", action: #selector(openMainWindow), keyEquivalent: "o"))
        recordItem = NSMenuItem(title: "🎙 Iniciar ditado (Option + Espaço)", action: #selector(toggleDictation), keyEquivalent: "")
        trayMenu.addItem(recordItem)
        trayMenu.addItem(NSMenuItem(title: "📖 Dicionário de palavras (Option + Shift + W)", action: #selector(openDictionary), keyEquivalent: ""))
        trayMenu.addItem(NSMenuItem.separator())
        trayMenu.addItem(NSMenuItem(title: "📋 Áudios pendentes", action: #selector(openPendingQueue), keyEquivalent: ""))
        trayMenu.addItem(NSMenuItem.separator())
        trayMenu.addItem(NSMenuItem(title: "⌨ Guia de atalhos…", action: #selector(showShortcutsGuide), keyEquivalent: ""))
        trayMenu.addItem(NSMenuItem(title: "⚙ Configurações…", action: #selector(openSettings), keyEquivalent: ","))
        updateItem = NSMenuItem(title: "Procurar atualizações", action: #selector(onUpdateMenu), keyEquivalent: "")
        trayMenu.addItem(updateItem)
        trayMenu.addItem(NSMenuItem.separator())
        trayMenu.addItem(NSMenuItem(title: "⏻ Sair do VoxIA", action: #selector(quitApp), keyEquivalent: "q"))
        for item in trayMenu.items { item.target = self }

        statusItem.menu = trayMenu
    }

    private func setupGlobalHotkey() {
        GlobalHotkeyService.onHotkeyPressed = { [weak self] in self?.toggleDictation() }
        GlobalHotkeyService.onDictionaryHotkeyPressed = { [weak self] in self?.quickAddDictionaryFromSelection() }
        GlobalHotkeyService.start()
    }

    // MARK: - Ditado

    @objc private func toggleDictation() {
        if audioRecorder.isRecording {
            stopAndProcessDictation()
        } else {
            startDictation()
        }
    }

    private func startDictation() {
        if finishing { return }
        guard !SecureCredentialService.getApiKey().isEmpty else {
            showNotification(title: "Chave da Groq ausente", text: "Cole sua chave da Groq em Configurações.")
            openSettings()
            return
        }
        activeTargetContext = TargetAppTracker.captureActiveContext()
        MicrophoneLevelService.boost()
        do {
            try audioRecorder.startRecording()
        } catch {
            MicrophoneLevelService.restore()
            hud.showError(error.localizedDescription)
            showNotification(title: "Erro ao gravar", text: error.localizedDescription)
            return
        }
        live?.cancel()
        live = LiveTranscriber(recorder: audioRecorder)
        live?.start()
        recordItem.title = "⏹ Parar e enviar (Option + Espaço)"
        hud.showRecording()
    }

    private func stopAndProcessDictation() {
        let duration = audioRecorder.recordingDuration
        let pcm = audioRecorder.stopCapture()
        MicrophoneLevelService.restore()
        recordItem.title = "🎙 Iniciar ditado (Option + Espaço)"
        hud.showProcessing()
        finishing = true
        let liveTranscriber = live
        live = nil
        let target = activeTargetContext

        Task { [weak self] in
            let partial = await liveTranscriber?.finish()
            let start = min(pcm.count, partial?.committedSamples ?? 0)
            let tail = Array(pcm[start...])
            let prefix = partial?.text ?? ""
            let prep = AudioPreprocessor.process(tail)

            await MainActor.run {
                guard let self = self else { return }
                self.finishing = false
                if prep.silent && prefix.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Logger.info("Recording had no speech; not sent to Whisper.")
                    self.hud.showError("Nenhuma fala")
                    return
                }
                let audioId = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
                let fileURL = AppPathService.audioQueueDirectory.appendingPathComponent("rec_\(audioId).wav")
                do {
                    try WavFile.data(prep.silent ? [] : prep.pcm).write(to: fileURL)
                } catch {
                    self.hud.showError("Falha ao salvar o áudio")
                    return
                }
                // Salva na fila ANTES de enviar: se a internet falhar, nada se perde
                let item = PendingAudioRepository.addRecording(
                    filePath: fileURL.path, durationSeconds: duration, targetContext: target,
                    prefixText: prefix.isEmpty ? nil : prefix, audioIsSilent: prep.silent, speechFrames: prep.speechFrames)
                RetryService.processItem(item) { success in
                    if success { self.hud.showSuccess() } else { self.hud.showError("Falha — áudio guardado") }
                }
            }
        }
    }

    // MARK: - Dicionário

    @objc private func quickAddDictionaryFromSelection() {
        let pasteboard = NSPasteboard.general
        let oldChangeCount = pasteboard.changeCount

        let src = CGEventSource(stateID: .hidSystemState)
        let cDown = CGEvent(keyboardEventSource: src, virtualKey: 0x08, keyDown: true)
        let cUp = CGEvent(keyboardEventSource: src, virtualKey: 0x08, keyDown: false)
        cDown?.flags = .maskCommand
        cUp?.flags = .maskCommand
        cDown?.post(tap: .cghidEventTap)
        cUp?.post(tap: .cghidEventTap)

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in
            var selectedText = ""
            if pasteboard.changeCount != oldChangeCount, let copied = pasteboard.string(forType: .string) {
                selectedText = copied.trimmingCharacters(in: .whitespacesAndNewlines)
            }
            self?.promptAddDictionaryEntry(prefilled: selectedText)
        }
    }

    private func promptAddDictionaryEntry(prefilled: String = "") {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "📖 Ensinar uma palavra ao VoxIA"
        alert.informativeText = "Como a palavra sai errada e como deve ficar. A forma correta também ajuda o reconhecimento de voz."

        let container = NSView(frame: NSRect(x: 0, y: 0, width: 320, height: 80))
        let lblOrig = NSTextField(labelWithString: "Como sai errado:")
        lblOrig.frame = NSRect(x: 0, y: 58, width: 320, height: 18)
        let txtOrig = NSTextField(frame: NSRect(x: 0, y: 38, width: 320, height: 22))
        txtOrig.stringValue = prefilled
        let lblRepl = NSTextField(labelWithString: "Como deve ficar:")
        lblRepl.frame = NSRect(x: 0, y: 20, width: 320, height: 18)
        let txtRepl = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 22))
        txtRepl.stringValue = prefilled
        for v in [lblOrig, txtOrig, lblRepl, txtRepl] { container.addSubview(v) }

        alert.accessoryView = container
        alert.addButton(withTitle: "Salvar")
        alert.addButton(withTitle: "Cancelar")
        if alert.runModal() == .alertFirstButtonReturn {
            let orig = txtOrig.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            let repl = txtRepl.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            if !orig.isEmpty || !repl.isEmpty {
                DictionaryService.addOrUpdate(original: orig, replacement: repl)
                showNotification(title: "Dicionário VoxIA", text: "Salvo: \(orig) → \(repl.isEmpty ? orig : repl)")
            }
        }
    }

    @objc private func openDictionary() {
        NSApp.activate(ignoringOtherApps: true)
        let entries = DictionaryService.getAll()
        let alert = NSAlert()
        alert.messageText = "📖 Dicionário (\(entries.count) palavras)"
        if entries.isEmpty {
            alert.informativeText = "Nenhuma palavra cadastrada. Selecione uma palavra em qualquer app e aperte Option + Shift + W."
        } else {
            let list = entries.prefix(20).map { "• \($0.original) → \($0.replacement)" }.joined(separator: "\n")
            alert.informativeText = list + (entries.count > 20 ? "\n… e mais \(entries.count - 20)." : "")
        }
        alert.addButton(withTitle: "Adicionar palavra")
        alert.addButton(withTitle: "Fechar")
        if alert.runModal() == .alertFirstButtonReturn { promptAddDictionaryEntry() }
    }

    // MARK: - Janela principal

    @objc private func openMainWindow() {
        if mainWindow == nil {
            mainWindow = MainWindowController(actions: MainWindowActions(
                openDictionaryAdd: { [weak self] in self?.promptAddDictionaryEntry() },
                openPending: { [weak self] in self?.openPendingQueue() },
                openSettings: { [weak self] in self?.openSettings() },
                openShortcuts: { [weak self] in self?.showShortcutsGuide() },
                checkUpdates: { [weak self] in self?.onUpdateMenu() }
            ))
        }
        mainWindow?.present()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { openMainWindow() }
        return true
    }

    // MARK: - Outras telas

    @objc private func showShortcutsGuide() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "⌨ Atalhos do VoxIA"
        alert.informativeText = """
        🎙 Ditar: Option + Espaço (aperte, fale e aperte de novo)
        📖 Ensinar palavra: selecione a palavra e aperte Option + Shift + W
        ⚙ Menu: ícone 🎙 na barra de menus, perto do relógio
        """
        alert.addButton(withTitle: "Entendi")
        alert.runModal()
    }

    @objc private func openPendingQueue() {
        NSApp.activate(ignoringOtherApps: true)
        let pending = PendingAudioRepository.getAll()
        let alert = NSAlert()
        alert.messageText = "Áudios pendentes (\(pending.count))"
        alert.informativeText = pending.isEmpty ? "Nenhum áudio esperando envio." : pending.map { "\($0.createdAt) — \($0.status.rawValue)" }.joined(separator: "\n")
        alert.addButton(withTitle: pending.isEmpty ? "Fechar" : "Reenviar agora")
        if !pending.isEmpty { alert.addButton(withTitle: "Fechar") }
        if alert.runModal() == .alertFirstButtonReturn, let first = PendingAudioRepository.getPendingItems().first {
            hud.showProcessing()
            RetryService.processItem(first) { [weak self] ok in
                if ok { self?.hud.showSuccess() } else { self?.hud.showError("Ainda sem conexão") }
            }
        }
    }

    @objc private func openSettings() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Configurações do VoxIA"
        alert.informativeText = """
        Cole sua chave da Groq (começa com gsk_). Não tem? É grátis e leva 2 minutos: clique em "Criar chave na Groq", entre com sua conta Google, clique em "Create API Key" e copie a chave (ela só aparece uma vez).
        """

        let stack = NSStackView(frame: NSRect(x: 0, y: 0, width: 420, height: 470))
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 8
        let keyField = NSSecureTextField(frame: NSRect(x: 0, y: 0, width: 340, height: 24))
        keyField.stringValue = SecureCredentialService.getApiKey()
        keyField.placeholderString = "gsk_..."
        let boost = NSButton(checkboxWithTitle: "Microfone no máximo durante o ditado (baixa sozinho se saturar)", target: nil, action: nil)
        boost.state = SettingsService.current.boostMicrophoneDuringDictation ? .on : .off
        let updates = NSButton(checkboxWithTitle: "Procurar atualizações automaticamente", target: nil, action: nil)
        updates.state = SettingsService.current.autoCheckUpdates ? .on : .off
        let copy = NSButton(checkboxWithTitle: "Deixar o texto também na área de transferência", target: nil, action: nil)
        copy.state = SettingsService.current.autoCopyToClipboard ? .on : .off
        let formatLabel = NSTextField(labelWithString: "Formato do texto:")
        let format = NSPopUpButton(frame: NSRect(x: 0, y: 0, width: 200, height: 26), pullsDown: false)
        format.addItems(withTitles: ["Parágrafos", "Linha única", "Personalizado"])
        format.selectItem(withTitle: SettingsService.current.selectedPromptPreset)
        // Prompt Personalizado: caixa de texto de várias linhas (Enter quebra linha, não fecha a janela)
        let customLabel = NSTextField(labelWithString: "Prompt personalizado (usado só no formato Personalizado):")
        let customScroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 420, height: 220))
        customScroll.hasVerticalScroller = true
        customScroll.borderType = .bezelBorder
        let customField = NSTextView(frame: customScroll.bounds)
        customField.isRichText = false
        customField.isAutomaticQuoteSubstitutionEnabled = false
        customField.isAutomaticDashSubstitutionEnabled = false
        customField.font = NSFont.systemFont(ofSize: 12)
        customField.autoresizingMask = [.width]
        customField.isVerticallyResizable = true
        customField.textContainer?.widthTracksTextView = true
        customField.string = SettingsService.current.customPromptText
        customField.toolTip = "Escreva como a IA deve revisar, organizar e formatar o ditado. A IA continua proibida de responder perguntas ou inventar conteúdo."
        customScroll.documentView = customField
        for v in [keyField, boost, updates, copy, formatLabel, format, customLabel, customScroll] { stack.addArrangedSubview(v) }
        customScroll.widthAnchor.constraint(equalToConstant: 420).isActive = true
        customScroll.heightAnchor.constraint(equalToConstant: 220).isActive = true
        keyField.widthAnchor.constraint(equalToConstant: 420).isActive = true

        alert.accessoryView = stack
        alert.addButton(withTitle: "Salvar")
        alert.addButton(withTitle: "Cancelar")
        alert.addButton(withTitle: "Criar chave na Groq")

        let response = alert.runModal()
        if response == .alertFirstButtonReturn {
            SecureCredentialService.saveApiKey(keyField.stringValue)
            SettingsService.current.boostMicrophoneDuringDictation = boost.state == .on
            SettingsService.current.autoCheckUpdates = updates.state == .on
            SettingsService.current.autoCopyToClipboard = copy.state == .on
            let custom = customField.string.trimmingCharacters(in: .whitespacesAndNewlines)
            var preset = format.titleOfSelectedItem ?? "Parágrafos"
            if preset == "Personalizado" && custom.isEmpty { preset = "Parágrafos" }
            SettingsService.current.selectedPromptPreset = preset
            SettingsService.current.customPromptText = custom
            SettingsService.save()
        } else if response == .alertThirdButtonReturn, let url = URL(string: "https://console.groq.com/keys") {
            NSWorkspace.shared.open(url)
        }
    }

    // MARK: - Atualizações

    private func startUpdateChecks() {
        // Primeira busca 1 minuto depois de abrir; depois confere a cada hora se já passou um dia
        updateTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: false) { [weak self] _ in
            self?.autoCheck()
            self?.updateTimer = Timer.scheduledTimer(withTimeInterval: 3600, repeats: true) { [weak self] _ in self?.autoCheck() }
        }
    }

    private func autoCheck() {
        guard pendingUpdate == nil, UpdateService.isAutoCheckDue else { return }
        Task { [weak self] in
            if let info = await UpdateService.check() {
                await MainActor.run { self?.onUpdateFound(info) }
            }
        }
    }

    private func onUpdateFound(_ info: UpdateService.UpdateInfo) {
        pendingUpdate = info
        updateItem.title = "⬆ Atualizar o VoxIA (versão nova)"
        showNotification(title: "Versão nova do VoxIA", text: "Abra o menu 🎙 e clique em \"Atualizar o VoxIA\".")
    }

    @objc private func onUpdateMenu() {
        if pendingUpdate != nil {
            promptUpdate()
            return
        }
        guard UpdateService.isSupported else {
            NSApp.activate(ignoringOtherApps: true)
            let alert = NSAlert()
            alert.messageText = "Atualização automática indisponível"
            alert.informativeText = "Esta cópia foi compilada à mão. Baixe a versão nova pelo link do GitHub."
            alert.runModal()
            return
        }
        Task { [weak self] in
            let info = await UpdateService.check()
            await MainActor.run {
                guard let self = self else { return }
                if let info = info {
                    self.onUpdateFound(info)
                    self.promptUpdate()
                } else {
                    NSApp.activate(ignoringOtherApps: true)
                    let alert = NSAlert()
                    alert.messageText = "Você já está com a versão mais nova (build \(UpdateService.currentBuild))."
                    alert.runModal()
                }
            }
        }
    }

    private func promptUpdate() {
        guard let info = pendingUpdate else { return }
        if audioRecorder.isRecording || finishing {
            showNotification(title: "VoxIA", text: "Termine o ditado em andamento e depois atualize.")
            return
        }
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Atualizar o VoxIA?"
        alert.informativeText = "O VoxIA fecha e abre de novo sozinho em alguns segundos. Sua chave, dicionário e configurações continuam iguais. Depois da atualização, o macOS pode pedir de novo a permissão de Acessibilidade."
        alert.addButton(withTitle: "Atualizar agora")
        alert.addButton(withTitle: "Depois")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        updateItem.title = "Atualizando…"
        updateItem.isEnabled = false
        Task { [weak self] in
            let restarting = await UpdateService.apply(info)
            await MainActor.run {
                if restarting {
                    NSApplication.shared.terminate(nil)
                } else {
                    self?.updateItem.title = "⬆ Atualizar o VoxIA (versão nova)"
                    self?.updateItem.isEnabled = true
                }
            }
        }
    }

    // MARK: - Utilidades

    private func showNotification(title: String, text: String) {
        let notification = NSUserNotification()
        notification.title = title
        notification.informativeText = text
        NSUserNotificationCenter.default.deliver(notification)
    }

    @objc private func quitApp() {
        MicrophoneLevelService.restore()
        GlobalHotkeyService.stop()
        NSApplication.shared.terminate(nil)
    }
}
