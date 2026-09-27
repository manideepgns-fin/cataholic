import AppKit
import ServiceManagement
import SwiftUI

// Cataholic — a chunky desktop cat (or a hundred) for macOS. The cat code is shared with Atlance's menu-bar cat;
// this file is the app around it: a paw in the menu bar, launch at login, and the owner's own double-click link.

@main
struct CataholicApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    var body: some Scene {
        MenuBarExtra("Cataholic", systemImage: "pawprint.fill") {
            Button(CrewPrefs.showCrew ? "Hide the cats" : "Show the cats") { CrewPrefs.setShowCrew(!CrewPrefs.showCrew) }
            Button(CrewPrefs.quiet ? "Wake the cats" : "Quiet mode") { CrewPrefs.setQuiet(!CrewPrefs.quiet) }
            Menu("More cats") {
                ForEach(CrewHerd.sizes, id: \.self) { n in Button(n == 0 ? "None" : "\(n) cats") { CrewHerd.set(n) } }
            }
            Menu("Cat") { ForEach(CatCoat.all, id: \.id) { c in Button(c.name) { CatCoat.set(c); CrewPanel.applyPrefs() } } }
            Button("Zoomies!") { CrewPanel.stopLife(); CrewPanel.zoomies() }
            Button(CataholicLink.url == nil ? "Set double-click link…" : "Change double-click link…") { CataholicLink.ask() }
            Divider()
            Toggle("Launch at login", isOn: Binding(get: { SMAppService.mainApp.status == .enabled },
                                                    set: { on in try? on ? SMAppService.mainApp.register() : SMAppService.mainApp.unregister() }))
            Button("Quit Cataholic") { NSApp.terminate(nil) }
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        // `--render-coats <file.png>`: draw all eight coats (sitting, then napping) into one image for the README, then quit.
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--render-coats"), i + 1 < args.count {
            MainActor.assumeIsolated { CataholicRender.coats(to: args[i + 1]) }
            NSApp.terminate(nil); return
        }
        NSApp.setActivationPolicy(.accessory)
        // A pet does something every minute or so (the Atlance cat, a colleague at work, waits ~6 min).
        CrewModel.Tune.meanWait = 50
        CrewModel.Tune.minGap = 15
        CrewPanel.show()
    }
}

/// What a double-click opens (any URL — a Slack channel, a board, a playlist). None set → zoomies.
@MainActor enum CataholicLink {
    static var url: URL? {
        guard let s = UserDefaults.standard.string(forKey: "cataholicLink"), !s.isEmpty else { return nil }
        return URL(string: s)
    }
    static func ask() {
        let a = NSAlert()
        a.messageText = "Double-click link"
        a.informativeText = "Double-clicking your cat opens this. Leave it empty for zoomies."
        let f = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 24))
        f.stringValue = UserDefaults.standard.string(forKey: "cataholicLink") ?? ""
        f.placeholderString = "https://…"
        a.accessoryView = f
        a.addButton(withTitle: "Save"); a.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        guard a.runModal() == .alertFirstButtonReturn else { return }
        var s = f.stringValue.trimmingCharacters(in: .whitespaces)
        if !s.isEmpty, !s.contains("://") { s = "https://" + s }
        UserDefaults.standard.set(s, forKey: "cataholicLink")
        CrewPanel.applyPrefs()
    }
}

/// Seconds since the last keyboard/mouse input — the cat naps once you've been away 5 minutes.
enum CataholicIdle {
    static var seconds: Double {
        CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)
    }
}

// ── Stand-ins for the Atlance pieces the shared cat code reads (no inbox, no Billing Desk here) ──

struct MailCase: Identifiable { let id: String; let title: String; let status: String; let next: String }

@MainActor final class MailStore: ObservableObject {
    static let shared = MailStore()
    struct AgentNow { var state = ""; var who = ""; var left = 0; var at: Date? = nil }
    @Published private(set) var cases: [MailCase] = []
    @Published private(set) var watchRunning = false
    @Published private(set) var agentNow = AgentNow()
    var signCount: Int { 0 }
    var yourMove: [MailCase] { [] }
}

enum MailClearanceFmt {
    static func splitTitle(_ t: String, fallback: String) -> (String, String) { (t, fallback) }
}

@MainActor enum AtlanceConfig {
    static var billingDeskURL: String { CataholicLink.url?.absoluteString ?? "" }
}

@MainActor enum CataholicRender {
    static func coats(to path: String) {
        let row = { (state: CrewSkinState) in
            HStack(spacing: 14) {
                ForEach(CatCoat.all, id: \.id) { c in
                    CrewCharacter(state: state, badge: "", colors: CrewSkinColors(), pin: false, coatOverride: c)
                }
            }
        }
        let sheet = VStack(spacing: -40) { row(.look); row(.sleeping) }
            .padding(24).background(Color(red: 0.13, green: 0.13, blue: 0.15))
        let r = ImageRenderer(content: sheet)
        r.scale = 2
        guard let img = r.nsImage, let tiff = img.tiffRepresentation,
              let png = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]) else { return }
        try? png.write(to: URL(fileURLWithPath: path))
    }
}
