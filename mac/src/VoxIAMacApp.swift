import Foundation
import AppKit

class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var trayMenu: NSMenu!
    private var audioRecorder = AudioRecorderService()
    private var hud = FloatingHudOverlay()
    private var activeTargetContext: TargetContext?

    func applicationDidFinishLaunching(_ notification: Notification) {
        SettingsService.load()
        PendingAudioRepository.loadQueue()
        HistoryRepository.load()
        DictionaryService.load()

        setupStatusItem()
        setupGlobalHotkey()
        RetryService.start()

        Logger.info("VoxIA macOS App initialized and resident in System Menu Bar.")
    }

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            button.title = "🎙"
            button.font = NSFont.systemFont(ofSize: 14)
        }

        trayMenu = NSMenu()
        trayMenu.addItem(NSMenuItem(title: "🎙 Iniciar Gravação (Option + Space)", action: #selector(toggleDictation), keyEquivalent: ""))
        trayMenu.addItem(NSMenuItem(title: "📖 Dicionário de Palavras (Option + Shift + W)", action: #selector(openDictionary), keyEquivalent: ""))
        trayMenu.addItem(NSMenuItem.separator())
        trayMenu.addItem(NSMenuItem(title: "📋 Áudios Pendentes", action: #selector(openPendingQueue), keyEquivalent: ""))
        trayMenu.addItem(NSMenuItem(title: "📜 Histórico & Estatísticas", action: #selector(openHistory), keyEquivalent: ""))
        trayMenu.addItem(NSMenuItem.separator())
        trayMenu.addItem(NSMenuItem(title: "⌨ Guia de Atalhos...", action: #selector(showShortcutsGuide), keyEquivalent: ""))
        trayMenu.addItem(NSMenuItem(title: "⚙ Configurações", action: #selector(openSettings), keyEquivalent: ","))
        trayMenu.addItem(NSMenuItem.separator())
        trayMenu.addItem(NSMenuItem(title: "⏻ Sair do VoxIA", action: #selector(quitApp), keyEquivalent: "q"))

        statusItem.menu = trayMenu
    }

    private func setupGlobalHotkey() {
        GlobalHotkeyService.onHotkeyPressed = { [weak self] in
            self?.toggleDictation()
        }
        GlobalHotkeyService.onDictionaryHotkeyPressed = { [weak self] in
            self?.quickAddDictionaryFromSelection()
        }
        GlobalHotkeyService.start()
    }

    @objc private func toggleDictation() {
        if audioRecorder.isRecording {
            stopAndProcessDictation()
        } else {
            startDictation()
        }
    }

    private func startDictation() {
        let apiKey = SecureCredentialService.getApiKey()
        guard !apiKey.isEmpty else {
            showNotification(title: "Groq API Key Ausente", text: "Configure sua API Key da Groq em Configurações.")
            openSettings()
            return
        }

        do {
            _ = try audioRecorder.startRecording()
            activeTargetContext = TargetAppTracker.captureActiveContext()
            hud.showRecording()
        } catch {
            hud.showError(error.localizedDescription)
        }
    }

    private func stopAndProcessDictation() {
        let duration = audioRecorder.recordingDuration
        guard let fileURL = audioRecorder.stopRecording() else {
            hud.showError("Erro no áudio")
            return
        }

        hud.showProcessing()

        // Save recording to persistent queue BEFORE upload attempt
        let queueItem = PendingAudioRepository.addRecording(filePath: fileURL.path, durationSeconds: duration, targetContext: activeTargetContext)

        RetryService.processItem(queueItem) { [weak self] success in
            DispatchQueue.main.async {
                if success {
                    self?.hud.showSuccess()
                } else {
                    self?.hud.showError("Falha — áudio salvo")
                }
            }
        }
    }

    @objc private func quickAddDictionaryFromSelection() {
        // Attempt to copy current selection in active Mac app
        let pasteboard = NSPasteboard.general
        let oldChangeCount = pasteboard.changeCount

        // Simulate Cmd + C
        let src = CGEventSource(stateID: .hidSystemState)
        let cmdDown = CGEvent(keyboardEventSource: src, virtualKey: 0x37, keyDown: true)
        let cDown = CGEvent(keyboardEventSource: src, virtualKey: 0x08, keyDown: true)
        let cUp = CGEvent(keyboardEventSource: src, virtualKey: 0x08, keyDown: false)
        let cmdUp = CGEvent(keyboardEventSource: src, virtualKey: 0x37, keyDown: false)

        cDown?.flags = .maskCommand
        cUp?.flags = .maskCommand

        cmdDown?.post(tap: .cghidEventTap)
        cDown?.post(tap: .cghidEventTap)
        cUp?.post(tap: .cghidEventTap)
        cmdUp?.post(tap: .cghidEventTap)

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { [weak self] in
            var selectedText = ""
            if pasteboard.changeCount != oldChangeCount, let copied = pasteboard.string(forType: .string) {
                selectedText = copied.trimmingCharacters(in: .whitespacesAndNewlines)
            }
            self?.promptAddDictionaryEntry(prefilled: selectedText)
        }
    }

    private func promptAddDictionaryEntry(prefilled: String = "") {
        let alert = NSAlert()
        alert.messageText = "📖 Adicionar ao Dicionário do VoxIA"
        alert.informativeText = "Defina a palavra original e sua substituição correta:\n(Atalho rápido: Control + Option + D)"

        let container = NSView(frame: NSRect(x: 0, y: 0, width: 320, height: 80))

        let lblOrig = NSTextField(labelWithString: "Palavra / Erro (De):")
        lblOrig.frame = NSRect(x: 0, y: 56, width: 320, height: 18)
        lblOrig.font = NSFont.boldSystemFont(ofSize: 11)

        let txtOrig = NSTextField(frame: NSRect(x: 0, y: 36, width: 320, height: 22))
        txtOrig.stringValue = prefilled

        let lblRepl = NSTextField(labelWithString: "Substituição Correta (Para):")
        lblRepl.frame = NSRect(x: 0, y: 18, width: 320, height: 18)
        lblRepl.font = NSFont.boldSystemFont(ofSize: 11)

        let txtRepl = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 22))
        txtRepl.stringValue = prefilled

        container.addSubview(lblOrig)
        container.addSubview(txtOrig)
        container.addSubview(lblRepl)
        container.addSubview(txtRepl)

        alert.accessoryView = container
        alert.addButton(withTitle: "Salvar Regra")
        alert.addButton(withTitle: "Cancelar")

        if alert.runModal() == .alertFirstButtonReturn {
            let orig = txtOrig.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            let repl = txtRepl.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)

            if !orig.isEmpty || !repl.isEmpty {
                DictionaryService.addOrUpdate(original: orig, replacement: repl)
                showNotification(title: "Dicionário VoxIA", text: "Regra salva: '\(orig)' ➔ '\(repl.isEmpty ? orig : repl)'")
            }
        }
    }

    @objc private func openDictionary() {
        let entries = DictionaryService.getAll()
        let alert = NSAlert()
        alert.messageText = "📖 Dicionário de Palavras & Substituições (\(entries.count))"

        if entries.isEmpty {
            alert.informativeText = "Nenhuma palavra cadastrada no dicionário.\n\nVocê pode cadastrar selecionando palavras e usando Control + Option + D."
        } else {
            let listStr = entries.prefix(20).map { "• \($0.original) ➔ \($0.replacement)" }.joined(separator: "\n")
            alert.informativeText = "Regras ativas no motor local:\n\n" + listStr + (entries.count > 20 ? "\n... e mais \(entries.count - 20) palavras." : "")
        }

        alert.addButton(withTitle: "➕ Adicionar Palavra")
        alert.addButton(withTitle: "Limpar Tudo")
        alert.addButton(withTitle: "Fechar")

        let resp = alert.runModal()
        if resp == .alertFirstButtonReturn {
            promptAddDictionaryEntry()
        } else if resp == .alertSecondButtonReturn {
            DictionaryService.clear()
            showNotification(title: "Dicionário VoxIA", text: "Todas as palavras foram apagadas.")
        }
    }

    @objc private func showShortcutsGuide() {
        let alert = NSAlert()
        alert.messageText = "⌨ Guia de Atalhos Globais — VoxIA macOS"
        alert.informativeText = """
        🎙 Ditado por Voz:
           • Atalho Padrão: Option + Space (ou Cmd + Option)

        📖 Adicionar ao Dicionário:
           • Atalho Rápido: Option + Shift + W (⌥ + ⇧ + W)
           • Como usar: Selecione a palavra em qualquer app (Safari, Word, Pages, etc.) e aperte o atalho.

        ⚙ Menu e Configurações:
           • Clique no ícone 🎙 na Barra de Menus superior ao lado do relógio.
        """
        alert.addButton(withTitle: "Entendi")
        alert.runModal()
    }

    @objc private func openPendingQueue() {
        let pending = PendingAudioRepository.getAll()
        let alert = NSAlert()
        alert.messageText = "Áudios Pendentes (\(pending.count))"
        alert.informativeText = pending.isEmpty ? "Nenhum áudio pendente na fila." : pending.map { "\($0.createdAt) - \($0.status.rawValue)" }.joined(separator: "\n")
        alert.runModal()
    }

    @objc private func openHistory() {
        let alert = NSAlert()
        alert.messageText = "Histórico de Transcrições"
        alert.informativeText = "Histórico ativo salvo em ~/Library/Application Support/VoxIA/history.json"
        alert.runModal()
    }

    @objc private func openSettings() {
        let alert = NSAlert()
        alert.messageText = "Configurações — VoxIA macOS"
        alert.informativeText = """
        Cole sua chave da Groq (começa com gsk_).

        Não tem chave? É grátis e leva 2 minutos:
        1. Clique em "Criar chave na Groq" e entre com sua conta Google (ou e-mail).
        2. Clique em "Create API Key", dê um nome (ex.: VoxIA) e copie a chave. Ela só aparece uma vez.
        3. Volte aqui, cole a chave e clique em Salvar.
        """

        let input = NSSecureTextField(frame: NSRect(x: 0, y: 0, width: 300, height: 24))
        input.stringValue = SecureCredentialService.getApiKey()
        alert.accessoryView = input
        alert.addButton(withTitle: "Salvar")
        alert.addButton(withTitle: "Cancelar")
        alert.addButton(withTitle: "Criar chave na Groq")

        let response = alert.runModal()
        if response == .alertFirstButtonReturn {
            SecureCredentialService.saveApiKey(input.stringValue)
        } else if response == .alertThirdButtonReturn {
            if let url = URL(string: "https://console.groq.com/keys") {
                NSWorkspace.shared.open(url)
            }
        }
    }

    private func showNotification(title: String, text: String) {
        let notification = NSUserNotification()
        notification.title = title
        notification.informativeText = text
        NSUserNotificationCenter.default.deliver(notification)
    }

    @objc private func quitApp() {
        GlobalHotkeyService.stop()
        NSApplication.shared.terminate(nil)
    }
}

// Main Entry Point
let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
