import Foundation
import AppKit
import CryptoKit

/// Atualização com um clique. O GitHub Actions publica, junto com o VoxIA-Mac.zip, um version-mac.json
/// com o número do build e o SHA-256 do .zip. Se o número for maior que o deste app (CFBundleVersion),
/// o VoxIA baixa o .zip, confere o SHA-256, troca o VoxIA.app e reabre. Se a pasta não deixar gravar,
/// abre o link de download no navegador.
public enum UpdateService {
    private static let versionURL = URL(string: "https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/version-mac.json")!
    public static let downloadURL = URL(string: "https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/VoxIA-Mac.zip")!

    public struct UpdateInfo {
        public let build: Int
        public let url: URL
        public let sha256: String
    }

    public static var currentBuild: Int {
        return Int(Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "0") ?? 0
    }

    /// Só se atualiza o VoxIA.app que veio do GitHub Actions (compilado à mão tem build 0).
    public static var isSupported: Bool {
        return currentBuild > 0 && Bundle.main.bundleURL.pathExtension == "app"
    }

    public static var isAutoCheckDue: Bool {
        guard isSupported, SettingsService.current.autoCheckUpdates else { return false }
        return Date().timeIntervalSince1970 - SettingsService.current.lastUpdateCheck >= 20 * 3600
    }

    public static func check() async -> UpdateInfo? {
        guard isSupported else { return nil }
        do {
            var request = URLRequest(url: versionURL)
            request.cachePolicy = .reloadIgnoringLocalCacheData
            request.timeoutInterval = 20
            let (data, _) = try await URLSession.shared.data(for: request)
            guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let build = json["build"] as? Int,
                  let urlString = json["url"] as? String, let url = URL(string: urlString),
                  let sha = (json["sha256"] as? String)?.lowercased(), sha.count == 64 else { return nil }
            await MainActor.run {
                SettingsService.current.lastUpdateCheck = Date().timeIntervalSince1970
                SettingsService.save()
            }
            Logger.info("Update check: this build \(currentBuild), published build \(build).")
            return build > currentBuild ? UpdateInfo(build: build, url: url, sha256: sha) : nil
        } catch {
            Logger.warning("Update check failed: \(error.localizedDescription)")
            return nil
        }
    }

    /// Baixa, confere e troca o VoxIA.app. Devolve true quando o app deve fechar (a versão nova
    /// abre sozinha um segundo depois).
    public static func apply(_ info: UpdateInfo) async -> Bool {
        let fm = FileManager.default
        let appURL = Bundle.main.bundleURL
        let work = fm.temporaryDirectory.appendingPathComponent("voxia-update-\(info.build)")
        do {
            try? fm.removeItem(at: work)
            try fm.createDirectory(at: work, withIntermediateDirectories: true)
            let (data, _) = try await URLSession.shared.data(from: info.url)
            let sha = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
            guard sha == info.sha256 else {
                Logger.error("Update download failed the SHA-256 check; not installed.")
                return false
            }
            let zip = work.appendingPathComponent("VoxIA-Mac.zip")
            try data.write(to: zip)
            guard run("/usr/bin/ditto", ["-x", "-k", zip.path, work.path]) == 0 else { return false }
            let newApp = work.appendingPathComponent("VoxIA.app")
            guard fm.fileExists(atPath: newApp.path) else { return false }
            _ = run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", newApp.path])

            guard fm.isWritableFile(atPath: appURL.deletingLastPathComponent().path) else {
                await MainActor.run { offerManualDownload() }
                return false
            }
            _ = try fm.replaceItemAt(appURL, withItemAt: newApp)
            // Reabre a versão nova depois que esta fechar
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/bin/sh")
            process.arguments = ["-c", "sleep 1; /usr/bin/open \"\(appURL.path)\""]
            try process.run()
            Logger.info("Updated to build \(info.build); restarting.")
            return true
        } catch {
            Logger.error("Update failed: \(error.localizedDescription)")
            await MainActor.run { offerManualDownload() }
            return false
        }
    }

    private static func offerManualDownload() {
        let alert = NSAlert()
        alert.messageText = "Não foi possível instalar a atualização"
        alert.informativeText = "Quer abrir o link para baixar a versão nova manualmente?"
        alert.addButton(withTitle: "Abrir link")
        alert.addButton(withTitle: "Agora não")
        if alert.runModal() == .alertFirstButtonReturn {
            NSWorkspace.shared.open(downloadURL)
        }
    }

    @discardableResult
    private static func run(_ path: String, _ args: [String]) -> Int32 {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = args
        do {
            try process.run()
            process.waitUntilExit()
            return process.terminationStatus
        } catch {
            return -1
        }
    }
}
