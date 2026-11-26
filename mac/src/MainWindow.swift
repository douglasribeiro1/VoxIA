import AppKit
import SwiftUI

/// Cores do protótipo do Claude Design (as mesmas do Windows e do Android).
enum VoxColors {
    static func hex(_ v: UInt32) -> Color {
        Color(red: Double((v >> 16) & 0xFF) / 255, green: Double((v >> 8) & 0xFF) / 255, blue: Double(v & 0xFF) / 255)
    }
    static let background = hex(0xF4F2EC)
    static let surface = Color.white
    static let surfaceAlt = hex(0xF1EFE8)
    static let border = hex(0xE4E1D8)
    static let borderStrong = hex(0xD5D1C6)
    static let divider = hex(0xEFEDE6)
    static let text = hex(0x16181D)
    static let textSecondary = hex(0x3A3F4A)
    static let muted = hex(0x5E636E)
    static let accent = hex(0x4338CA)
    static let accentDark = hex(0x3730A3)
    static let accentSoft = hex(0xECEAFD)
    static let success = hex(0x1E7B4F)
    static let successSoft = hex(0xE3F4EA)
    static let danger = hex(0xD92D3A)
    static let dangerSoft = hex(0xFBE3E5)
    static let sidebar = hex(0x16181D)
    static let sidebarActive = hex(0x2A2D36)
    static let sidebarCard = hex(0x23262E)
    static let sidebarText = hex(0xC9CBD2)
    static let sidebarMuted = hex(0xA3A7B1)
    static let sidebarIcon = hex(0x8B8F9A)
    static let sidebarIconActive = hex(0xA5B4FC)
    static let live = hex(0x4ADE80)
}

/// Ações que a janela pede ao AppDelegate (as telas que já existem).
struct MainWindowActions {
    var openDictionaryAdd: () -> Void
    var openPending: () -> Void
    var openSettings: () -> Void
    var openShortcuts: () -> Void
    var checkUpdates: () -> Void
}

final class MainWindowModel: ObservableObject {
    @Published var history: [DictationHistoryItem] = []
    @Published var words: [String] = []
    @Published var hasKey = false
    @Published var hotkey = "Option + Espaço"
    @Published var copiedId: String?

    func refresh() {
        history = HistoryRepository.recent(limit: 20)
        var seen = Set<String>()
        words = DictionaryService.getAll()
            .map { $0.replacement.isEmpty ? $0.original : $0.replacement }
            .filter { !$0.isEmpty && seen.insert($0).inserted }
        if words.count > 30 { words = Array(words.prefix(30)) }
        hasKey = !SecureCredentialService.getApiKey().isEmpty
        let h = SettingsService.current.hotkeyDisplay
        hotkey = h.isEmpty ? "Option + Espaço" : h.replacingOccurrences(of: "Space", with: "Espaço")
    }
}

/// Janela principal: barra lateral escura e a página Início. Redimensionável e com tela cheia.
final class MainWindowController: NSWindowController, NSWindowDelegate {
    private let model = MainWindowModel()

    init(actions: MainWindowActions) {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1240, height: 780),
            styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
            backing: .buffered, defer: false)
        window.title = "VoxIA"
        window.minSize = NSSize(width: 1000, height: 600)
        window.collectionBehavior.insert(.fullScreenPrimary)
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        window.delegate = self
        window.contentView = NSHostingView(rootView: MainView(model: model, actions: actions))
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) não usado") }

    func present() {
        model.refresh()
        NSApp.setActivationPolicy(.regular) // ícone no Dock enquanto a janela está aberta
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func windowDidBecomeKey(_ notification: Notification) { model.refresh() }

    func windowWillClose(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory) // volta a ficar só na barra de menus
    }
}

private enum NavItem: String, CaseIterable {
    case home = "Início"
    case dictionary = "Dicionário"
    case pending = "Áudios pendentes"
    case settings = "Configurações"
    case shortcuts = "Atalhos"

    var symbol: String {
        switch self {
        case .home: return "house"
        case .dictionary: return "book"
        case .pending: return "tray"
        case .settings: return "slider.horizontal.3"
        case .shortcuts: return "keyboard"
        }
    }
}

struct MainView: View {
    @ObservedObject var model: MainWindowModel
    let actions: MainWindowActions

    var body: some View {
        HStack(spacing: 0) {
            sidebar
            GeometryReader { geo in
                HStack(alignment: .top, spacing: 18) {
                    VStack(alignment: .leading, spacing: 18) {
                        header
                        howTo(wide: geo.size.width > 900)
                        historyCard
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                    VStack(spacing: 18) {
                        statusCard
                        dictionaryCard
                    }
                    .frame(width: 300)
                    .frame(maxHeight: .infinity, alignment: .top)
                }
                .padding(EdgeInsets(top: 34, leading: 32, bottom: 24, trailing: 28))
            }
            .background(VoxColors.background)
        }
        .foregroundColor(VoxColors.text)
        .font(.system(size: 14))
    }

    // MARK: Barra lateral

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text("VoxIA").font(.system(size: 24, weight: .bold)).foregroundColor(.white)
                Text("ditado por voz").font(.system(size: 12)).foregroundColor(VoxColors.sidebarMuted)
            }
            .padding(EdgeInsets(top: 34, leading: 10, bottom: 22, trailing: 10))

            ForEach(NavItem.allCases, id: \.self) { item in
                Button(action: { navigate(item) }) {
                    HStack(spacing: 12) {
                        Image(systemName: item.symbol)
                            .frame(width: 20)
                            .foregroundColor(item == .home ? VoxColors.sidebarIconActive : VoxColors.sidebarIcon)
                        Text(item.rawValue)
                            .font(.system(size: 14, weight: item == .home ? .semibold : .medium))
                            .foregroundColor(item == .home ? .white : VoxColors.sidebarText)
                        Spacer()
                    }
                    .padding(.horizontal, 12)
                    .frame(height: 44)
                    .background(RoundedRectangle(cornerRadius: 10).fill(item == .home ? VoxColors.sidebarActive : Color.clear))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }

            Spacer()

            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 8) {
                    Circle().fill(VoxColors.live).frame(width: 8, height: 8)
                    Text("Pronto para ditar").font(.system(size: 13, weight: .medium)).foregroundColor(VoxColors.hex(0xE9E7E0))
                }
                Text("Rodando na barra de menus. Pode fechar esta janela.")
                    .font(.system(size: 12)).foregroundColor(VoxColors.sidebarMuted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 12).fill(VoxColors.sidebarCard))
        }
        .padding(EdgeInsets(top: 0, leading: 14, bottom: 20, trailing: 14))
        .frame(width: 232)
        .frame(maxHeight: .infinity)
        .background(VoxColors.sidebar)
    }

    private func navigate(_ item: NavItem) {
        switch item {
        case .home: model.refresh()
        case .dictionary: actions.openDictionaryAdd(); model.refresh()
        case .pending: actions.openPending()
        case .settings: actions.openSettings(); model.refresh()
        case .shortcuts: actions.openShortcuts()
        }
    }

    // MARK: Página Início

    private var header: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Início").font(.system(size: 32, weight: .bold))
            Text("O VoxIA fica na barra de menus e dita direto no programa que você estiver usando.")
                .foregroundColor(VoxColors.muted)
        }
    }

    private func card<Content: View>(@ViewBuilder _ content: () -> Content) -> some View {
        content()
            .background(RoundedRectangle(cornerRadius: 18).fill(VoxColors.surface))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(VoxColors.border, lineWidth: 1))
    }

    private func howTo(wide: Bool) -> some View {
        card {
            HStack(alignment: .center, spacing: 28) {
                VStack(alignment: .leading, spacing: 14) {
                    Text("COMO DITAR").font(.system(size: 11, weight: .semibold)).kerning(0.9).foregroundColor(VoxColors.accentDark)
                    Text("Aperte \(model.hotkey) em qualquer programa e fale.")
                        .font(.system(size: 26, weight: .bold)).fixedSize(horizontal: false, vertical: true)
                    Text("Word, WhatsApp, e-mail, navegador. Aperte de novo e o texto aparece escrito no lugar, com pontuação e ortografia corrigidas.")
                        .foregroundColor(VoxColors.muted).fixedSize(horizontal: false, vertical: true)
                    VStack(alignment: .leading, spacing: 8) {
                        shortcutRow(model.hotkey, "Começar e terminar o ditado")
                        shortcutRow("Esc", "Cancelar a gravação")
                        shortcutRow("Option + Shift + W", "Ensinar a palavra selecionada")
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                if wide { illustration }
            }
            .padding(EdgeInsets(top: 22, leading: 24, bottom: 22, trailing: 24))
        }
    }

    private func shortcutRow(_ key: String, _ text: String) -> some View {
        HStack(spacing: 12) {
            Text(key)
                .font(.system(size: 12, weight: .semibold))
                .padding(.horizontal, 9).padding(.vertical, 3)
                .frame(minWidth: 40)
                .background(RoundedRectangle(cornerRadius: 6).fill(VoxColors.background))
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(VoxColors.borderStrong, lineWidth: 1))
            Text(text).font(.system(size: 13)).foregroundColor(VoxColors.textSecondary)
        }
    }

    private var illustration: some View {
        ZStack(alignment: .bottom) {
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 5) {
                    ForEach(0..<3) { _ in Circle().fill(VoxColors.hex(0xC9C5B8)).frame(width: 7, height: 7) }
                    Text("Documento.docx").font(.system(size: 11)).foregroundColor(VoxColors.muted).padding(.leading, 8)
                    Spacer()
                }
                .padding(.horizontal, 10).frame(height: 26).background(VoxColors.hex(0xE9E7E0))
                VStack(alignment: .leading, spacing: 9) {
                    bar(0.70, VoxColors.borderStrong)
                    bar(0.92, VoxColors.borderStrong)
                    bar(0.55, VoxColors.borderStrong)
                    HStack(spacing: 2) {
                        bar(0.38, VoxColors.accent)
                        Rectangle().fill(VoxColors.accent).frame(width: 2, height: 16)
                    }
                }
                .padding(EdgeInsets(top: 14, leading: 16, bottom: 0, trailing: 16))
                Spacer()
            }
            HStack(spacing: 8) {
                Circle().fill(VoxColors.hex(0xF0525E)).frame(width: 8, height: 8)
                HStack(spacing: 2) {
                    ForEach(Array([6, 12, 8, 15, 7, 12, 5].enumerated()), id: \.offset) { pair in
                        RoundedRectangle(cornerRadius: 2).fill(VoxColors.sidebarIconActive).frame(width: 3, height: CGFloat(pair.element))
                    }
                }
                Text("00:18").font(.system(size: 12, weight: .semibold)).foregroundColor(.white)
            }
            .padding(.horizontal, 14).frame(height: 34)
            .background(Capsule().fill(VoxColors.sidebar))
            .padding(.bottom, 16)
        }
        .frame(width: 252, height: 200)
        .background(VoxColors.background)
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(VoxColors.border, lineWidth: 1))
        .accessibilityHidden(true)
    }

    private func bar(_ fraction: CGFloat, _ color: Color) -> some View {
        GeometryReader { g in
            RoundedRectangle(cornerRadius: 4).fill(color).frame(width: g.size.width * fraction, height: 8)
        }
        .frame(height: 8)
    }

    private var historyCard: some View {
        card {
            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Últimos ditados").font(.system(size: 17, weight: .semibold))
                    Spacer()
                    Text(model.copiedId != nil ? "Texto copiado" : "Guardados neste Mac")
                        .font(.system(size: 12))
                        .foregroundColor(model.copiedId != nil ? VoxColors.success : VoxColors.muted)
                }
                .padding(.bottom, 8)
                if model.history.isEmpty {
                    Divider().background(VoxColors.divider)
                    Text("Nenhum ditado ainda. Aperte \(model.hotkey) em qualquer programa para começar.")
                        .foregroundColor(VoxColors.muted).padding(.vertical, 18)
                } else {
                    ScrollView {
                        VStack(spacing: 0) {
                            ForEach(model.history, id: \.id) { row in historyRow(row) }
                        }
                    }
                }
                Spacer(minLength: 0)
            }
            .padding(EdgeInsets(top: 16, leading: 24, bottom: 4, trailing: 24))
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
    }

    private static let timeFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm"
        return f
    }()

    private func historyRow(_ row: DictationHistoryItem) -> some View {
        VStack(spacing: 0) {
            Rectangle().fill(VoxColors.divider).frame(height: 1)
            HStack(spacing: 14) {
                Text(MainView.timeFormatter.string(from: row.timestamp))
                    .font(.system(size: 13).monospacedDigit()).foregroundColor(VoxColors.muted).frame(width: 44, alignment: .leading)
                Text(row.finalText.replacingOccurrences(of: "\n", with: " "))
                    .lineLimit(1).truncationMode(.tail)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if !row.targetProcess.isEmpty {
                    Text(row.targetProcess)
                        .font(.system(size: 12, weight: .medium)).foregroundColor(VoxColors.textSecondary)
                        .lineLimit(1)
                        .padding(.horizontal, 10).padding(.vertical, 3)
                        .background(Capsule().fill(VoxColors.surfaceAlt))
                }
                Button(action: { copy(row) }) {
                    Image(systemName: "doc.on.doc").foregroundColor(VoxColors.muted).frame(width: 36, height: 36)
                }
                .buttonStyle(.plain)
                .help("Copiar texto")
                .accessibilityLabel("Copiar texto")
            }
            .frame(height: 54)
        }
    }

    private func copy(_ row: DictationHistoryItem) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(row.finalText, forType: .string)
        let m = model
        m.copiedId = row.id
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.8) { m.copiedId = nil }
    }

    private var statusCard: some View {
        card {
            VStack(alignment: .leading, spacing: 0) {
                Text(model.hasKey ? "Tudo certo" : "Falta a chave").font(.system(size: 17, weight: .semibold)).padding(.bottom, 6)
                statusRow("key", "Chave de API", model.hasKey ? "Conectada" : "Cole a chave nas Configurações", model.hasKey, action: actions.openSettings)
                statusRow("mic", "Microfone", "Sobe a 100% só durante o ditado", true, action: nil)
                statusRow("arrow.triangle.2.circlepath", "Atualizações", "Clique para procurar uma versão nova", true, action: actions.checkUpdates)
            }
            .padding(EdgeInsets(top: 16, leading: 18, bottom: 8, trailing: 18))
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func statusRow(_ symbol: String, _ label: String, _ detail: String, _ ok: Bool, action: (() -> Void)?) -> some View {
        Button(action: { action?() }) {
            VStack(spacing: 0) {
                Rectangle().fill(VoxColors.divider).frame(height: 1)
                HStack(spacing: 12) {
                    Image(systemName: symbol)
                        .font(.system(size: 14, weight: .medium))
                        .foregroundColor(ok ? VoxColors.success : VoxColors.danger)
                        .frame(width: 32, height: 32)
                        .background(RoundedRectangle(cornerRadius: 10).fill(ok ? VoxColors.successSoft : VoxColors.dangerSoft))
                    VStack(alignment: .leading, spacing: 1) {
                        Text(label).font(.system(size: 13, weight: .medium)).foregroundColor(VoxColors.text)
                        Text(detail).font(.system(size: 12)).foregroundColor(VoxColors.muted).lineLimit(1)
                    }
                    Spacer()
                }
                .padding(.vertical, 10)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(action == nil)
    }

    private var dictionaryCard: some View {
        card {
            VStack(alignment: .leading, spacing: 12) {
                Text("Dicionário").font(.system(size: 17, weight: .semibold))
                Text("Palavras que você ensinou. O VoxIA passa a escrevê-las certo.")
                    .font(.system(size: 12)).foregroundColor(VoxColors.muted).fixedSize(horizontal: false, vertical: true)
                if model.words.isEmpty {
                    Text("Nenhuma palavra ainda.").foregroundColor(VoxColors.muted)
                } else {
                    ScrollView {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 80), spacing: 8, alignment: .leading)], alignment: .leading, spacing: 8) {
                            ForEach(model.words, id: \.self) { w in
                                Text(w)
                                    .font(.system(size: 13, weight: .medium)).foregroundColor(VoxColors.accentDark)
                                    .lineLimit(1)
                                    .padding(.horizontal, 12).padding(.vertical, 5)
                                    .background(Capsule().fill(VoxColors.accentSoft))
                            }
                        }
                    }
                }
                Spacer(minLength: 0)
                Button(action: { actions.openDictionaryAdd(); model.refresh() }) {
                    HStack(spacing: 8) {
                        Image(systemName: "plus")
                        Text("Adicionar palavra").font(.system(size: 13, weight: .semibold))
                    }
                    .foregroundColor(VoxColors.accentDark)
                    .frame(maxWidth: .infinity).frame(height: 44)
                    .background(RoundedRectangle(cornerRadius: 12).fill(VoxColors.hex(0xFAF9FF)))
                    .overlay(RoundedRectangle(cornerRadius: 12).stroke(VoxColors.hex(0xA9A3E6), style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            .padding(18)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
    }
}
