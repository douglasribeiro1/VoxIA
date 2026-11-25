import AppKit

/// App que estava na frente quando o ditado começou: o texto volta para ele.
public struct TargetContext: Codable {
    public var appName: String?
    public var bundleId: String?
    public var pid: Int32?
}

public enum TargetAppTracker {
    public static func captureActiveContext() -> TargetContext? {
        guard let app = NSWorkspace.shared.frontmostApplication else { return nil }
        return TargetContext(appName: app.localizedName, bundleId: app.bundleIdentifier, pid: app.processIdentifier)
    }

    /// Traz de volta para a frente o app onde o ditado começou (se ainda estiver aberto).
    public static func reactivate(_ context: TargetContext?) {
        guard let pid = context?.pid, let app = NSRunningApplication(processIdentifier: pid) else { return }
        if !app.isActive { app.activate(options: []) }
    }
}
