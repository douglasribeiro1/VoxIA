import Foundation
import AppKit

public class FloatingHudOverlay: NSPanel {
    private let iconLabel = NSTextField(labelWithString: "🎙")
    private var containerView: NSView!
    private var hideTimer: Timer?

    public init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 46, height: 46),
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered,
                   defer: false)

        self.isFloatingPanel = true
        self.level = .floating
        self.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        self.backgroundColor = .clear
        self.isOpaque = false
        self.hasShadow = true

        containerView = NSView(frame: NSRect(x: 0, y: 0, width: 46, height: 46))
        containerView.wantsLayer = true
        containerView.layer?.cornerRadius = 23
        containerView.layer?.backgroundColor = NSColor(red: 0.08, green: 0.08, blue: 0.10, alpha: 0.95).cgColor
        containerView.layer?.borderWidth = 2.0
        containerView.layer?.borderColor = NSColor(red: 1.0, green: 0.35, blue: 0.37, alpha: 0.6).cgColor
        containerView.layer?.masksToBounds = true

        iconLabel.frame = NSRect(x: 0, y: 8, width: 46, height: 30)
        iconLabel.font = NSFont.systemFont(ofSize: 18)
        iconLabel.alignment = .center
        iconLabel.textColor = NSColor(red: 1.0, green: 0.35, blue: 0.37, alpha: 1.0)

        containerView.addSubview(iconLabel)
        self.contentView = containerView
    }

    public func showRecording() {
        DispatchQueue.main.async {
            self.hideTimer?.invalidate()
            self.iconLabel.stringValue = "🎙"
            self.iconLabel.textColor = NSColor(red: 1.0, green: 0.35, blue: 0.37, alpha: 1.0)
            self.containerView.layer?.borderColor = NSColor(red: 1.0, green: 0.35, blue: 0.37, alpha: 0.8).cgColor
            self.positionBottomRight()
            self.orderFront(nil)
        }
    }

    public func showProcessing() {
        DispatchQueue.main.async {
            self.hideTimer?.invalidate()
            self.iconLabel.stringValue = "⏳"
            self.iconLabel.textColor = NSColor(red: 1.0, green: 0.75, blue: 0.0, alpha: 1.0)
            self.containerView.layer?.borderColor = NSColor(red: 1.0, green: 0.75, blue: 0.0, alpha: 0.8).cgColor
            self.positionBottomRight()
            self.orderFront(nil)
        }
    }

    public func showSuccess() {
        DispatchQueue.main.async {
            self.iconLabel.stringValue = "✓"
            self.iconLabel.textColor = NSColor(red: 0.15, green: 0.75, blue: 0.3, alpha: 1.0)
            self.containerView.layer?.borderColor = NSColor(red: 0.15, green: 0.75, blue: 0.3, alpha: 0.8).cgColor
            self.positionBottomRight()
            self.orderFront(nil)

            self.hideTimer?.invalidate()
            self.hideTimer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: false) { _ in
                self.orderOut(nil)
            }
        }
    }

    public func showError(_ msg: String) {
        DispatchQueue.main.async {
            self.iconLabel.stringValue = "⚠"
            self.iconLabel.textColor = NSColor(red: 0.9, green: 0.2, blue: 0.2, alpha: 1.0)
            self.containerView.layer?.borderColor = NSColor(red: 0.9, green: 0.2, blue: 0.2, alpha: 0.8).cgColor
            self.positionBottomRight()
            self.orderFront(nil)

            self.hideTimer?.invalidate()
            self.hideTimer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: false) { _ in
                self.orderOut(nil)
            }
        }
    }

    private func positionBottomRight() {
        if let screen = NSScreen.main {
            let visibleFrame = screen.visibleFrame
            let x = visibleFrame.maxX - 65
            let y = visibleFrame.minY + 25
            self.setFrameOrigin(NSPoint(x: x, y: y))
        }
    }
}
