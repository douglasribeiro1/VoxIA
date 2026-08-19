import Foundation
import AppKit

public class GlobalHotkeyService {
    public static var onHotkeyPressed: (() -> Void)?
    public static var onDictionaryHotkeyPressed: (() -> Void)?
    private static var globalMonitor: Any?
    private static var localMonitor: Any?
    private static var lastTriggerTime = Date.distantPast
    private static var lastDictTriggerTime = Date.distantPast

    public static func start() {
        stop()

        // Global monitor for when other apps are active
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { event in
            handleEvent(event)
        }

        // Local monitor for when VoxIA is active
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { event in
            handleEvent(event)
            return event
        }

        Logger.info("macOS Global Hotkey Monitor initialized (Option + Space & Control + Option + D).")
    }

    public static func stop() {
        if let monitor = globalMonitor {
            NSEvent.removeMonitor(monitor)
            globalMonitor = nil
        }
        if let monitor = localMonitor {
            NSEvent.removeMonitor(monitor)
            localMonitor = nil
        }
    }

    private static func handleEvent(_ event: NSEvent) {
        guard event.type == .keyDown else { return }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)

        // 1. Check for Dictionary Quick Add Hotkey: Option + Shift + W (Key code 13 = 'W') or Control + Option + W / D
        let isOptionShiftW = flags.contains([.option, .shift]) && (event.keyCode == 13 || event.keyCode == 2)
        let isCtrlOptionW = flags.contains([.control, .option]) && (event.keyCode == 13 || event.keyCode == 2)

        if isOptionShiftW || isCtrlOptionW {
            let now = Date()
            if now.timeIntervalSince(lastDictTriggerTime) > 0.4 {
                lastDictTriggerTime = now
                DispatchQueue.main.async {
                    onDictionaryHotkeyPressed?()
                }
            }
            return
        }

        // 2. Check for Dictation Hotkey: Option + Space (Key code 49 = Space) or Cmd + Option
        let isOptionSpace = flags.contains(.option) && event.keyCode == 49
        let isCmdOption = flags.contains([.command, .option])

        if isOptionSpace || isCmdOption {
            let now = Date()
            if now.timeIntervalSince(lastTriggerTime) > 0.35 {
                lastTriggerTime = now
                DispatchQueue.main.async {
                    onHotkeyPressed?()
                }
            }
        }
    }
}
