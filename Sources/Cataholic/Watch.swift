import AppKit
import SwiftUI
import UserNotifications

// Watched links (owner 28 Sep 2026: "paste a link and it reads the link"). The cat reads the titles of the tabs you
// already have open in Chrome — you are logged in there, so the cat never needs a login of its own. A site that has
// something waiting puts the count in its title ("(3) Billing Desk", "Inbox (12) - Gmail"); the cat adds them up,
// holds up the sign, and sends a Mac notification when a count goes up. Clicking one brings that tab forward.

struct Watch: Codable, Equatable, Identifiable {
    var name: String
    var url: String
    var id: String { url }
    /// Tabs match on the address without its ?query / #fragment, so "billing-desk/?find=x" still counts.
    var prefix: String {
        var c = URLComponents(string: url); c?.query = nil; c?.fragment = nil
        return (c?.string ?? url).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
    }
}

@MainActor enum Watcher {
    static let chrome = "com.google.Chrome"
    /// nil = no tab open for it (or Chrome isn't running); a number = what the tab title says (no number = 0).
    private(set) static var counts: [String: Int] = [:]
    private static var timer: Timer?
    private static var primed = false                                   // the first read after launch never notifies
    private static let queue = DispatchQueue(label: "cataholic.watch")   // AppleScript off the main thread

    static var watches: [Watch] {
        get { (UserDefaults.standard.data(forKey: "cataholicWatch")).flatMap { try? JSONDecoder().decode([Watch].self, from: $0) } ?? [] }
        set { UserDefaults.standard.set(try? JSONEncoder().encode(newValue), forKey: "cataholicWatch"); poll() }
    }
    static var total: Int { watches.reduce(0) { $0 + (counts[$1.id] ?? 0) } }

    static func start() {
        UNUserNotificationCenter.current().delegate = NotifyDelegate.shared
        if !watches.isEmpty { UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in } }
        poll()
        timer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { _ in Task { @MainActor in poll() } }
    }

    static func poll() {
        let ws = watches
        guard !ws.isEmpty else { counts = [:]; MailStore.shared.setWatchSign([]); return }
        guard NSRunningApplication.runningApplications(withBundleIdentifier: chrome).first != nil else {
            apply(ws, tabs: []); return                                   // never launch Chrome just to look
        }
        queue.async {
            let tabs = readTabs()
            Task { @MainActor in apply(ws, tabs: tabs) }
        }
    }

    private static func apply(_ ws: [Watch], tabs: [(url: String, title: String)]) {
        var next: [String: Int] = [:]
        for w in ws {
            let mine = tabs.filter { $0.url.hasPrefix(w.prefix) }
            if !mine.isEmpty { next[w.id] = mine.map { count(in: $0.title) }.max() ?? 0 }
        }
        let grew = ws.filter { (next[$0.id] ?? 0) > (counts[$0.id] ?? 0) }
        counts = next
        if primed { for w in grew { notify(w, count: next[w.id] ?? 0) } }
        primed = true
        // Biggest first, so the whisper names the one that just went up.
        let rows = ws.map { ($0.name, next[$0.id] ?? 0) }.filter { $0.1 > 0 }
            .sorted { a, b in grew.contains { $0.name == a.0 } && !grew.contains { $0.name == b.0 } }
        MailStore.shared.setWatchSign(rows)
    }

    /// "(3) Billing Desk" → 3 · "Inbox (12) - Gmail" → 12 · "(99+) Slack" → 99 · no number → 0.
    static func count(in title: String) -> Int {
        guard let r = title.range(of: #"\((\d+)\+?\)"#, options: .regularExpression) else { return 0 }
        return Int(title[r].filter(\.isNumber)) ?? 0
    }

    nonisolated private static func readTabs() -> [(url: String, title: String)] {
        let src = """
        tell application id "com.google.Chrome"
          set out to ""
          repeat with w in windows
            repeat with t in tabs of w
              set out to out & (URL of t) & (character id 9) & (title of t) & (character id 10)
            end repeat
          end repeat
          return out
        end tell
        """
        var err: NSDictionary?
        let s = NSAppleScript(source: src)?.executeAndReturnError(&err).stringValue ?? ""
        return s.split(separator: "\n").compactMap { line in
            let p = line.split(separator: "\t", maxSplits: 1, omittingEmptySubsequences: false)
            return p.count == 2 ? (String(p[0]), String(p[1])) : nil
        }
    }

    /// Bring the watched tab forward in Chrome — or open it there if it isn't open.
    static func open(_ w: Watch) {
        let q = { (s: String) in s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") }
        let src = """
        tell application id "com.google.Chrome"
          repeat with w in windows
            set i to 0
            repeat with t in tabs of w
              set i to i + 1
              if URL of t starts with "\(q(w.prefix))" then
                set active tab index of w to i
                set index of w to 1
                activate
                return
              end if
            end repeat
          end repeat
          open location "\(q(w.url))"
          activate
        end tell
        """
        queue.async {
            var err: NSDictionary?
            NSAppleScript(source: src)?.executeAndReturnError(&err)
            if err != nil, let u = URL(string: w.url) { DispatchQueue.main.async { NSWorkspace.shared.open(u) } }   // no Chrome
            DispatchQueue.main.asyncAfter(deadline: .now() + 3) { poll() }
        }
    }

    static func remove(_ w: Watch) { watches.removeAll { $0 == w } }

    static func add() {
        let a = NSAlert()
        a.messageText = "Watch a link"
        a.informativeText = "Paste the address of a page you use in Chrome. When its tab shows a number — like “(3) Billing Desk” or “Inbox (12)” — your cat holds it up and tells you when it goes up."
        let name = NSTextField(frame: NSRect(x: 0, y: 30, width: 320, height: 24)); name.placeholderString = "Name (e.g. Billing Desk)"
        let link = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 24)); link.placeholderString = "https://…"
        let box = NSView(frame: NSRect(x: 0, y: 0, width: 320, height: 54)); box.addSubview(name); box.addSubview(link)
        a.accessoryView = box
        a.addButton(withTitle: "Watch"); a.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        guard a.runModal() == .alertFirstButtonReturn else { return }
        var t = link.stringValue.trimmingCharacters(in: .whitespaces)
        guard !t.isEmpty else { return }
        if !t.contains("://") { t = "https://" + t }
        let n = name.stringValue.trimmingCharacters(in: .whitespaces)
        let w = Watch(name: n.isEmpty ? (URL(string: t)?.host ?? t) : n, url: t)
        if !watches.contains(where: { $0.id == w.id }) { watches.append(w) }
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        open(w)                                      // opens it in Chrome; the first read asks "control Google Chrome?"
    }

    private static func notify(_ w: Watch, count: Int) {
        let c = UNMutableNotificationContent()
        c.title = w.name
        c.body = count == 1 ? "1 waiting for you" : "\(count) waiting for you"
        c.sound = .default
        c.userInfo = ["watch": w.id]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "watch-\(w.id)", content: c, trigger: nil))
    }
}

/// Clicking a notification brings that tab forward; banners show even though the cat is the "front" app.
final class NotifyDelegate: NSObject, UNUserNotificationCenterDelegate {
    static let shared = NotifyDelegate()
    func userNotificationCenter(_ c: UNUserNotificationCenter, willPresent n: UNNotification,
                                withCompletionHandler done: @escaping (UNNotificationPresentationOptions) -> Void) {
        done([.banner, .sound])
    }
    func userNotificationCenter(_ c: UNUserNotificationCenter, didReceive r: UNNotificationResponse,
                                withCompletionHandler done: @escaping () -> Void) {
        let id = r.notification.request.content.userInfo["watch"] as? String
        Task { @MainActor in
            if let w = Watcher.watches.first(where: { $0.id == id }) { Watcher.open(w) }
            done()
        }
    }
}

/// "Waiting for you" — in the cat's right-click menu and the paw menu.
struct WatchMenu: View {
    var body: some View {
        let ws = Watcher.watches, total = Watcher.total
        Menu(total > 0 ? "Waiting for you: \(total)" : "Waiting for you") {
            ForEach(ws) { w in
                Button {
                    Watcher.open(w)
                } label: {
                    switch Watcher.counts[w.id] {
                    case .none: Text("\(w.name) — open it in Chrome")
                    case .some(0): Text("\(w.name) — nothing waiting")
                    case .some(let n): Text("\(w.name) — \(n)")
                    }
                }
            }
            if !ws.isEmpty { Divider() }
            Button("Watch a link…") { Watcher.add() }
            if !ws.isEmpty {
                Menu("Stop watching") { ForEach(ws) { w in Button(w.name) { Watcher.remove(w) } } }
            }
        }
    }
}
