import AppKit
import ServiceManagement
import Sparkle
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
            WatchMenu()
            LauncherMenu()
            Divider()
            Button("Check for updates…") { Updates.check() }
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
        // `--render-icon <file.png>`: the 1024×1024 app icon (scripts/make-icon.sh turns it into AppIcon.icns).
        if let i = args.firstIndex(of: "--render-icon"), i + 1 < args.count {
            MainActor.assumeIsolated { CataholicRender.icon(to: args[i + 1]) }
            NSApp.terminate(nil); return
        }
        NSApp.setActivationPolicy(.accessory)
        _ = Updates.controller                       // Sparkle starts checking (daily, in the background)
        CrewPanel.show()
        MainActor.assumeIsolated { Watcher.start() }
    }
    func application(_ application: NSApplication, open urls: [URL]) {
        MainActor.assumeIsolated { urls.forEach(CataholicLinks.handle) }
    }
}

/// Auto-update (owner 29 Sep 2026: "can we get this auto updater also?"). Sparkle reads the feed named in Info.plist
/// (SUFeedURL: the appcast attached to the latest GitHub release), checks once a day, and installs a signed update by itself.
/// Updates are signed with the EdDSA key in the owner's login Keychain (public half: SUPublicEDKey) — see scripts/release.sh.
@MainActor enum Updates {
    private static let delegate = UpdateDelegate()      // the controller keeps only a weak reference
    static let controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: delegate, userDriverDelegate: nil)
    static func check() { controller.checkForUpdates(nil) }
}

/// A downloaded update installs at once and the cat relaunches (~2 s). Left alone, Sparkle waits for the app to quit —
/// and a mascot that lives on your desktop for weeks would never update.
final class UpdateDelegate: NSObject, SPUUpdaterDelegate {
    func updater(_ updater: SPUUpdater, willInstallUpdateOnQuit item: SUAppcastItem,
                 immediateInstallationBlock install: @escaping () -> Void) -> Bool {
        install()
        return true
    }
}

/// The quick launcher (owner 27 Sep 2026: "quick launch double click"): named links and apps, one of them the
/// favourite that a double-click opens. Right-click the cat (or the paw menu) → Quick launch.
struct LaunchItem: Codable, Equatable, Identifiable {
    var name: String
    var target: String                       // a URL, or the path of an .app
    var id: String { name + target }
}

@MainActor enum Launcher {
    static var items: [LaunchItem] {
        get {
            if let d = UserDefaults.standard.data(forKey: "cataholicLaunch"),
               let v = try? JSONDecoder().decode([LaunchItem].self, from: d) { return v }
            // 0.1.0 kept one link — carry it over
            if let s = UserDefaults.standard.string(forKey: "cataholicLink"), !s.isEmpty {
                return [LaunchItem(name: URL(string: s)?.host ?? s, target: s)]
            }
            return []
        }
        set {
            UserDefaults.standard.set(try? JSONEncoder().encode(newValue), forKey: "cataholicLaunch")
            CrewPanel.applyPrefs()
        }
    }
    static var favorite: LaunchItem? {
        let f = UserDefaults.standard.string(forKey: "cataholicFavorite")
        return items.first { $0.id == f } ?? items.first
    }
    static func setFavorite(_ i: LaunchItem) { UserDefaults.standard.set(i.id, forKey: "cataholicFavorite"); CrewPanel.applyPrefs() }
    static func open(_ i: LaunchItem) {
        if i.target.hasPrefix("/") { NSWorkspace.shared.open(URL(fileURLWithPath: i.target)) }
        else if let u = URL(string: i.target) { NSWorkspace.shared.open(u) }
    }
    static func remove(_ i: LaunchItem) { items.removeAll { $0 == i } }

    static func addLink() {
        let a = NSAlert()
        a.messageText = "Add a link"
        a.informativeText = "Anything with a web address: a dashboard, a Slack channel, a playlist."
        let name = NSTextField(frame: NSRect(x: 0, y: 30, width: 320, height: 24)); name.placeholderString = "Name (e.g. Billing Desk)"
        let link = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 24)); link.placeholderString = "https://…"
        let box = NSView(frame: NSRect(x: 0, y: 0, width: 320, height: 54)); box.addSubview(name); box.addSubview(link)
        a.accessoryView = box
        a.addButton(withTitle: "Add"); a.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        guard a.runModal() == .alertFirstButtonReturn else { return }
        var t = link.stringValue.trimmingCharacters(in: .whitespaces)
        guard !t.isEmpty else { return }
        if !t.contains("://") { t = "https://" + t }
        let n = name.stringValue.trimmingCharacters(in: .whitespaces)
        items.append(LaunchItem(name: n.isEmpty ? (URL(string: t)?.host ?? t) : n, target: t))
    }
    static func addApp() {
        let p = NSOpenPanel()
        p.allowedContentTypes = [.application]
        p.directoryURL = URL(fileURLWithPath: "/Applications")
        p.allowsMultipleSelection = true
        NSApp.activate(ignoringOtherApps: true)
        guard p.runModal() == .OK else { return }
        items.append(contentsOf: p.urls.map { LaunchItem(name: $0.deletingPathExtension().lastPathComponent, target: $0.path) })
    }
}

/// The launcher as a menu — the same items in the cat's right-click menu and the paw menu.
struct LauncherMenu: View {
    var body: some View {
        let items = Launcher.items, fav = Launcher.favorite
        Menu("Quick launch") {
            ForEach(items) { i in Button(i == fav ? "★ \(i.name)" : i.name) { Launcher.open(i) } }
            if !items.isEmpty { Divider() }
            Button("Add a link…") { Launcher.addLink() }
            Button("Add an app…") { Launcher.addApp() }
            if items.count > 1 {
                Menu("Double-click opens") {
                    ForEach(items) { i in Button(i == fav ? "✓ \(i.name)" : i.name) { Launcher.setFavorite(i) } }
                }
            }
            if !items.isEmpty {
                Menu("Remove") { ForEach(items) { i in Button(i.name) { Launcher.remove(i) } } }
            }
        }
    }
}

/// `cataholic://` links — any app, script or CI job can talk to your cat:
///   cataholic://sign?count=3&note=Grafterr   → holds up a sign with 3 (and whispers the note when it goes up)
///   cataholic://sign?count=0                 → puts the sign down
///   cataholic://zoomies                      → zoomies
@MainActor enum CataholicLinks {
    static func handle(_ url: URL) {
        guard url.scheme == "cataholic" else { return }
        let q = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
        func v(_ k: String) -> String? { q.first { $0.name == k }?.value }
        switch url.host ?? "" {
        case "sign": MailStore.shared.setSign(count: Int(v("count") ?? "") ?? 0, note: v("note") ?? "")
        case "zoomies": CrewPanel.stopLife(); CrewPanel.zoomies()
        default: break
        }
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

/// The sign the cat holds (set by `cataholic://sign`). Shaped like Atlance's mail store — the shared cat code reads
/// `yourMove` / `signCount` for its "needs you" pose and the whisper — but it only ever holds what a link told it.
@MainActor final class MailStore: ObservableObject {
    static let shared = MailStore()
    struct AgentNow { var state = ""; var who = ""; var left = 0; var at: Date? = nil }
    @Published private(set) var cases: [MailCase] = []
    @Published private(set) var watchRunning = false
    @Published private(set) var agentNow = AgentNow()
    private(set) var note = ""
    var signCount: Int { cases.count }
    var yourMove: [MailCase] { cases }
    private var link = (count: 0, note: "")          // from a cataholic://sign link
    private var watched: [(String, Int)] = []         // from Chrome tabs (Watcher), the one that just grew first
    func setSign(count: Int, note: String) { link = (max(0, count), note); rebuild() }
    func setWatchSign(_ rows: [(String, Int)]) { watched = rows; rebuild() }
    private func rebuild() {
        let rows = watched + (link.count > 0 ? [(link.note, link.count)] : [])
        note = rows.first?.0 ?? ""
        cases = rows.flatMap { r in (0..<min(r.1, 999)).map { MailCase(id: "\(r.0)-\($0)", title: r.0, status: "Needs you", next: "") } }
    }
}

enum MailClearanceFmt {
    static func splitTitle(_ t: String, fallback: String) -> (String, String) { (t, fallback) }
}

@MainActor enum AtlanceConfig {
    static var billingDeskURL: String { "" }        // unused: the double-click goes through Launcher
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
    /// The app icon: Ginger in her glasses on a peach tile, drawn at 1024 px. Laid out in points on a 224-pt canvas
    /// (Apple's grid: an 824/1024 tile) and rendered at ×(1024/224), so the cat is drawn crisp, never scaled up.
    static func icon(to path: String) {
        let k: CGFloat = 1024 / 224
        let tile = RoundedRectangle(cornerRadius: 40, style: .continuous)
        let art = ZStack {
            tile.fill(LinearGradient(colors: [Color(red: 1.0, green: 0.90, blue: 0.76), Color(red: 0.99, green: 0.78, blue: 0.58)],
                                     startPoint: .top, endPoint: .bottom))
                .frame(width: 180, height: 180)
                .shadow(color: .black.opacity(0.22), radius: 5, y: 3)
            Ellipse().fill(Color(red: 0.80, green: 0.50, blue: 0.28).opacity(0.28))
                .frame(width: 124, height: 14).offset(y: 70)
            CrewCharacter(state: .look, badge: "", colors: CrewSkinColors(), pin: false, coatOverride: .ginger)
                .scaleEffect(1.42)
                .offset(y: 0)
        }
        .frame(width: 224, height: 224)
        let r = ImageRenderer(content: art)
        r.scale = k
        guard let img = r.nsImage, let tiff = img.tiffRepresentation,
              let png = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]) else { return }
        try? png.write(to: URL(fileURLWithPath: path))
    }
}
