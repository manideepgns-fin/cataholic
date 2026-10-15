import AppKit
import ServiceManagement
import Sparkle
import SwiftUI

// Cataholic — a chunky desktop cat (or a hundred) for macOS. The cat code is shared with Atlance's menu-bar cat;
// this file is the app around it: a paw in the menu bar, launch at login, updates and links. A double-click on the
// cat opens Find my cat (FindMyCat.swift).

@main
struct CataholicApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    var body: some Scene {
        MenuBarExtra("Cataholic", systemImage: "pawprint.fill") { CatMenuView() }
            .menuBarExtraStyle(.window)
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
        // `--check-find [N]`: the Find my cat fairness self-check (no window, no sound); exits 0 only if every layout is fair.
        if let i = args.firstIndex(of: "--check-find") {
            let n = i + 1 < args.count ? Int(args[i + 1]) ?? 200 : 200
            let code = MainActor.assumeIsolated { FindCheck.run(n) }
            exit(code)
        }
        // `--check-odds`: every lookalike's wrong detail is visible at the smallest size (FindOddsCheck); exits 0 only if all are.
        if args.contains("--check-odds") {
            exit(MainActor.assumeIsolated { FindOddsCheck.run() })
        }
        NSApp.setActivationPolicy(.accessory)
        _ = Updates.controller                       // Sparkle starts checking (daily, in the background)
        CrewPanel.show()
        MainActor.assumeIsolated {
            CatMenuPanel.install()
            if WindowGuides.isOn { WindowGuides.start() }
        }
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
    func setSign(count: Int, note: String) { link = (max(0, count), note); rebuild() }
    private func rebuild() {
        let rows = link.count > 0 ? [(link.note, link.count)] : []
        note = rows.first?.0 ?? ""
        cases = rows.flatMap { r in (0..<min(r.1, 999)).map { MailCase(id: "\(r.0)-\($0)", title: r.0, status: "Needs you", next: "") } }
    }
}

enum MailClearanceFmt {
    static func splitTitle(_ t: String, fallback: String) -> (String, String) { (t, fallback) }
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
