import Foundation
import AppKit
import ApplicationServices

public class TextInsertionService {
    @discardableResult
    public static func insertTextAtCursor(_ text: String) -> Bool {
        guard !text.isEmpty else { return false }

        // 1. Copy text to macOS NSPasteboard
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)

        // Give pasteboard system time to register
        usleep(60000) // 60ms

        // 2. Simulate Cmd + V keypress at active cursor location (Preserves existing text!)
        let src = CGEventSource(stateID: .hidSystemState)
        let vKeyCode: CGKeyCode = 0x09 // 'V' key on macOS

        let cmdDown = CGEvent(keyboardEventSource: src, virtualKey: vKeyCode, keyDown: true)
        cmdDown?.flags = .maskCommand

        let cmdUp = CGEvent(keyboardEventSource: src, virtualKey: vKeyCode, keyDown: false)
        cmdUp?.flags = .maskCommand

        cmdDown?.post(tap: .cghidEventTap)
        usleep(10000)
        cmdUp?.post(tap: .cghidEventTap)

        Logger.info("macOS Text insertion succeeded via Cmd+V paste at active cursor position.")
        return true
    }

    @discardableResult
    public static func insertText(_ text: String, targetCtx: TargetContext? = nil) -> Bool {
        return insertTextAtCursor(text)
    }
}
