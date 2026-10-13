import AppKit
import ServiceManagement
import SwiftUI

// CatMenu — the cat's menu as one crafted panel, not a settings form (owner 13 Oct 2026: "make these components
// rather than a plain list … the whole menu", then "redo this"). The same view opens from the paw in the menu bar
// (MenuBarExtra, window style) and from a right-click on the cat (CatMenuPanel, a pop-up beside the pointer).
// Top to bottom, loudest first:
//   1. Header — your cat, big, with its name, the eight coats as a strip of tiny cats beside it, and Zoomies.
//   2. Do something — the 3×3 action grid, the hero of the panel.
//   3. More cats — the herd size, one segmented row.
//   4. Switches — five Control-Center chips (accent = on), each with a short caption.
//   5. Waiting for you | Quick launch — two cards side by side, each with its own + (no "+ Add…" rows).
//   6. Footer — the version, Check for updates and Quit as small fixed-size buttons (the old footer overflowed).
// Nothing in the panel changes its height while it is open (the pop-up is sized once, from fittingSize), so the lists
// sit side by side rather than in tabs. Every value lives where it always did (UserDefaults via its own enum).
struct CatMenuView: View {
    var close: () -> Void = {}
    @ObservedObject private var ui = CatMenuUI.shared
    private var tick: Int { get { ui.tick } nonmutating set { ui.tick = newValue } }

    var body: some View {
        let _ = ui.tick
        VStack(alignment: .leading, spacing: 14) {
            header
            VStack(alignment: .leading, spacing: 6) {
                Heading("Do something")
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 5), count: 3), spacing: 5) {
                    ForEach(0..<CrewModel.ringActions.count, id: \.self) { i in
                        let (action, name, symbol) = CrewModel.ringActions[i]
                        Tile(symbol: symbol, title: name) {
                            guard let m = CrewPanel.currentModel() else { return }
                            close()
                            CrewSound.forAction(action); m.ringTap(action)
                        }
                    }
                }
            }
            VStack(alignment: .leading, spacing: 6) {
                Heading("More cats")
                Picker("", selection: Binding(get: { CrewHerd.count }, set: { CrewHerd.set($0); tick += 1 })) {
                    ForEach(CrewHerd.sizes, id: \.self) { n in Text(n == 0 ? "None" : "\(n)").tag(n) }
                }
                .pickerStyle(.segmented).tint(Color.catAccent).labelsHidden().controlSize(.small)
            }
            HStack(spacing: 4) {
                Chip("Show cats", help: "Show the cats", symbol: "pawprint.fill", on: CrewPrefs.showCrew) { CrewPrefs.setShowCrew($0) }
                Chip("Sounds", help: "Cat sounds", symbol: "speaker.wave.2.fill", on: CrewSound.isOn) { CrewSound.setEnabled($0) }
                Chip("Quiet", help: "Quiet mode", symbol: "moon.fill", on: CrewPrefs.quiet) { CrewPrefs.setQuiet($0) }
                Chip("Grid", help: "Window grid", symbol: "squareshape.split.3x3", on: WindowGuides.isOn) { WindowGuides.setOn($0) }
                Chip("At login", help: "Launch at login", symbol: "power", on: SMAppService.mainApp.status == .enabled) { on in
                    try? on ? SMAppService.mainApp.register() : SMAppService.mainApp.unregister()
                }
            }
            HStack(alignment: .top, spacing: 8) {
                waiting
                launch
            }
            .fixedSize(horizontal: false, vertical: true)
            footer
        }
        .padding(14)
        .frame(width: 300)
    }

    // MARK: Sections

    private var header: some View {
        let coat = CatCoat.current
        return HStack(alignment: .center, spacing: 10) {
            CatPicture(coat: coat, scale: 0.46)
                .frame(width: 46, height: 52)
                .background(Circle().fill(Color.catAccent.opacity(0.14)).frame(width: 54, height: 54))
                .frame(width: 54, height: 54)
            VStack(alignment: .leading, spacing: 5) {
                HStack(alignment: .firstTextBaseline) {
                    Text(coat.name).font(.system(size: 15, weight: .semibold))
                    Spacer(minLength: 4)
                    ZoomiesButton {
                        close()
                        CrewPanel.currentModel()?.stopAction(); CrewPanel.stopLife(); CrewSound.meow(); CrewPanel.zoomies()
                    }
                }
                HStack(spacing: 2) {
                    ForEach(CatCoat.all, id: \.id) { c in
                        CoatDot(coat: c, selected: c == coat) {
                            CatCoat.set(c); CrewPanel.applyPrefs(); CrewPanel.currentModel()?.pet(); tick += 1
                        }
                    }
                }
            }
        }
    }

    private var waiting: some View {
        Card(title: "Waiting for you", addHelp: "Watch a Chrome tab…", add: { close(); Watcher.add() }) {
            if Watcher.watches.isEmpty {
                Empty(text: "Watch a tab's count")
            }
            ForEach(Watcher.watches) { w in
                let n = Watcher.counts[w.id]
                Row(key: "watch-" + w.id, title: w.name, help: n == nil ? "Tab not open" : nil, trailing: {
                    if let n, n > 0 {
                        Text("\(n)").font(.system(size: 10, weight: .bold)).foregroundStyle(.white)
                            .padding(.horizontal, 5).frame(minWidth: 16, minHeight: 15)
                            .background(Capsule().fill(Color.catAccent))
                    } else {
                        Text(n == nil ? "off" : "0").font(.system(size: 10)).foregroundStyle(.tertiary)
                    }
                }) { close(); Watcher.open(w) }
                .contextMenu { Button("Stop watching") { Watcher.remove(w); tick += 1 } }
            }
        }
    }

    private var launch: some View {
        let fav = Launcher.favorite
        return Card(title: "Quick launch", addHelp: "Add a link or an app", menu: [
            ("Add a link…", { close(); Launcher.addLink() }),
            ("Add an app…", { close(); Launcher.addApp() }),
        ]) {
            if Launcher.items.isEmpty {
                Empty(text: "Links and apps")
            }
            ForEach(Launcher.items) { i in
                Row(key: "launch-" + i.id, title: i.name, icon: LaunchIcon(item: i),
                    help: i == fav ? "Opens on a double-click on the cat" : nil, trailing: {
                    if i == fav { Image(systemName: "star.fill").font(.system(size: 9)).foregroundStyle(Color.catAccent) }
                }) { close(); Launcher.open(i) }
                .contextMenu {
                    if i != fav { Button("Open on double-click") { Launcher.setFavorite(i); tick += 1 } }
                    Button("Remove") { Launcher.remove(i); tick += 1 }
                }
            }
        }
    }

    private var footer: some View {
        HStack(spacing: 4) {
            Text("Cataholic " + (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""))
                .font(.system(size: 10)).foregroundStyle(.tertiary).lineLimit(1).fixedSize()
            Spacer(minLength: 4)
            FooterButton(title: "Check for updates", symbol: "arrow.down.circle") { close(); Updates.check() }
            FooterButton(title: "Quit", symbol: "power") { NSApp.terminate(nil) }
        }
        .padding(.top, 8)
        .overlay(alignment: .top) { Divider() }
    }
}

/// What the menu redraws on: a tick after any pref change (the prefs are plain statics) and the hovered control.
/// One shared object instead of @State — this project builds with plain swiftc, where SwiftUI's macros aren't available.
@MainActor final class CatMenuUI: ObservableObject {
    static let shared = CatMenuUI()
    @Published var tick = 0
    @Published var hovered: String?
    func hover(_ key: String, _ on: Bool) { if on { hovered = key } else if hovered == key { hovered = nil } }
}

/// The quiet section label.
private struct Heading: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View { Text(text).font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary) }
}

/// A cat drawn small: the character draws at zoom × its 46×62 box, so scale it and clip to the box it lands in.
private struct CatPicture: View {
    let coat: CatCoat, scale: CGFloat
    var body: some View {
        CrewCharacter(state: .look, badge: "", colors: CrewSkinColors(), pin: false, coatOverride: coat)
            .frame(width: 46 * CrewLayout.zoom, height: 62 * CrewLayout.zoom)
            .scaleEffect(scale)
            .frame(width: 46 * CrewLayout.zoom * scale, height: 62 * CrewLayout.zoom * scale)
    }
}

/// An action: an icon over a short label, lighter on hover.
private struct Tile: View {
    let symbol: String, title: String, action: () -> Void
    private var key: String { "tile-" + title }
    @ObservedObject private var ui = CatMenuUI.shared
    private var hover: Bool { ui.hovered == key }
    var body: some View {
        Button(action: action) {
            VStack(spacing: 4) {
                Image(systemName: symbol).font(.system(size: 15)).frame(height: 18)
                    .foregroundStyle(hover ? Color.catAccent : Color.primary)
                Text(title).font(.system(size: 11)).lineLimit(1).minimumScaleFactor(0.8)
            }
            .frame(maxWidth: .infinity).padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(Color.primary.opacity(hover ? 0.12 : 0.055)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain).onHover { ui.hover(key, $0) }
    }
}

/// One coat in the header strip: a tiny cat, ringed in the accent when it is the one on your screen.
private struct CoatDot: View {
    let coat: CatCoat, selected: Bool, action: () -> Void
    private var key: String { "coat-" + coat.id }
    @ObservedObject private var ui = CatMenuUI.shared
    private var hover: Bool { ui.hovered == key }
    var body: some View {
        Button(action: action) {
            CatPicture(coat: coat, scale: 0.19)
                .frame(width: 21, height: 23).clipped()
                .frame(width: 23, height: 25)
                .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.primary.opacity(hover ? 0.12 : 0)))
                .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Color.catAccent, lineWidth: selected ? 1.5 : 0))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain).onHover { ui.hover(key, $0) }
        .help(coat.name)
    }
}

/// The one loud button in the header.
private struct ZoomiesButton: View {
    let action: () -> Void
    @ObservedObject private var ui = CatMenuUI.shared
    private var hover: Bool { ui.hovered == "zoomies" }
    var body: some View {
        Button(action: action) {
            Label("Zoomies!", systemImage: "hare.fill").font(.system(size: 11, weight: .semibold)).lineLimit(1).fixedSize()
                .foregroundStyle(Color.catAccent)
                .padding(.vertical, 4).padding(.horizontal, 9)
                .background(Capsule().fill(Color.catAccent.opacity(hover ? 0.26 : 0.15)))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain).onHover { ui.hover("zoomies", $0) }
        .help("Make every cat run around")
    }
}

/// A switch as a Control-Center chip: an accent circle when on, a quiet one when off, a short caption beneath.
private struct Chip: View {
    let title: String, help: String, symbol: String, on: Bool, set: (Bool) -> Void
    @ObservedObject private var ui = CatMenuUI.shared
    private var hover: Bool { ui.hovered == "chip-" + title }
    init(_ title: String, help: String, symbol: String, on: Bool, set: @escaping (Bool) -> Void) {
        self.title = title; self.help = help; self.symbol = symbol; self.on = on; self.set = set
    }
    var body: some View {
        Button { set(!on); ui.tick += 1 } label: {
            VStack(spacing: 4) {
                Image(systemName: symbol).font(.system(size: 13, weight: .medium))
                    .foregroundStyle(on ? Color.white : Color.primary.opacity(0.75))
                    .frame(width: 32, height: 32)
                    .background(Circle().fill(on ? Color.catAccent : Color.primary.opacity(hover ? 0.16 : 0.08)))
                Text(title).font(.system(size: 10)).foregroundStyle(on ? .primary : .secondary).lineLimit(1).fixedSize()
            }
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain).onHover { ui.hover("chip-" + title, $0) }
        .help(help + (on ? " — on" : " — off"))
    }
}

/// A small rounded card: a heading with its own + (a button, or a menu when there is a choice), then rows.
private struct Card<Content: View>: View {
    let title: String, addHelp: String
    var add: (() -> Void)? = nil
    var menu: [(String, () -> Void)] = []
    @ViewBuilder let content: () -> Content
    @ObservedObject private var ui = CatMenuUI.shared
    private var hover: Bool { ui.hovered == "add-" + title }
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 4) {
                Heading(title).lineLimit(1)
                Spacer(minLength: 0)
                plus.help(addHelp).onHover { ui.hover("add-" + title, $0) }
            }
            .padding(.leading, 6).padding(.bottom, 2)
            content()
            Spacer(minLength: 0)
        }
        .padding(6)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.045)))
    }
    @ViewBuilder private var plus: some View {
        let glyph = Image(systemName: "plus").font(.system(size: 10, weight: .semibold))
            .foregroundStyle(hover ? Color.catAccent : Color.secondary)
            .frame(width: 20, height: 18)
            .background(Circle().fill(Color.primary.opacity(hover ? 0.10 : 0)))
            .contentShape(Rectangle())
        Button {
            if menu.isEmpty { add?() } else { PopMenu.show(menu) }
        } label: { glyph }.buttonStyle(.plain)
    }
}

/// A plain AppKit menu at the pointer, for a + that offers a choice (a SwiftUI Menu label ignores the + styling).
@MainActor private final class PopMenu: NSObject {
    private let actions: [() -> Void]
    private init(_ actions: [() -> Void]) { self.actions = actions }
    @objc private func run(_ item: NSMenuItem) { actions[item.tag]() }
    static func show(_ items: [(String, () -> Void)]) {
        let target = PopMenu(items.map { $0.1 }), menu = NSMenu()
        for (i, it) in items.enumerated() {
            let m = NSMenuItem(title: it.0, action: #selector(run(_:)), keyEquivalent: "")
            m.target = target; m.tag = i; menu.addItem(m)
        }
        menu.popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)   // modal: target lives until it returns
    }
}

/// The quiet line a card shows while it has nothing in it.
private struct Empty: View {
    let text: String
    var body: some View {
        Text(text).font(.system(size: 11)).foregroundStyle(.tertiary).lineLimit(2)
            .padding(.horizontal, 6).padding(.vertical, 4)
    }
}

/// A clickable row inside a card: optional icon, a title, and something small on the right.
private struct Row<Trailing: View>: View {
    let key: String, title: String
    var icon: LaunchIcon? = nil
    var help: String? = nil
    @ViewBuilder let trailing: () -> Trailing
    let action: () -> Void
    @ObservedObject private var ui = CatMenuUI.shared
    private var hover: Bool { ui.hovered == key }
    var body: some View {
        Button(action: action) {
            HStack(spacing: 6) {
                if let icon { icon }
                Text(title).font(.system(size: 12)).lineLimit(1).truncationMode(.tail)
                Spacer(minLength: 2)
                trailing()
            }
            .padding(.vertical, 4).padding(.horizontal, 6)
            .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.primary.opacity(hover ? 0.10 : 0)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain).onHover { ui.hover(key, $0) }
        .help(help ?? title)
    }
}

/// A launch item's icon: the real app icon for an app, a globe for a link.
private struct LaunchIcon: View {
    let item: LaunchItem
    var body: some View {
        if item.target.hasPrefix("/") {
            Image(nsImage: NSWorkspace.shared.icon(forFile: item.target)).resizable().frame(width: 15, height: 15)
        } else {
            Image(systemName: "globe").font(.system(size: 11)).foregroundStyle(.secondary).frame(width: 15, height: 15)
        }
    }
}

/// A small, fixed-size footer button: an icon and a short label.
private struct FooterButton: View {
    let title: String, symbol: String, action: () -> Void
    private var key: String { "foot-" + title }
    @ObservedObject private var ui = CatMenuUI.shared
    private var hover: Bool { ui.hovered == key }
    var body: some View {
        Button(action: action) {
            Label(title, systemImage: symbol).font(.system(size: 11)).lineLimit(1).fixedSize()
                .foregroundStyle(hover ? .primary : .secondary)
                .padding(.vertical, 4).padding(.horizontal, 7)
                .background(Capsule().fill(Color.primary.opacity(hover ? 0.10 : 0)))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain).onHover { ui.hover(key, $0) }
    }
}

/// The right-click pop-up: CatMenuView in a borderless panel beside the pointer, closed by a click anywhere else,
/// by Esc, or by picking something.
@MainActor
enum CatMenuPanel {
    private static var panel: KeyPanel?
    private static var monitors: [Any] = []

    /// A right-click on your cat opens the panel (installed once at launch; herd cats don't take clicks).
    static func install() {
        _ = NSEvent.addLocalMonitorForEvents(matching: .rightMouseDown) { e in
            guard e.window is CrewNSPanel else { return e }
            MainActor.assumeIsolated { show(at: NSEvent.mouseLocation) }
            return nil
        }
    }

    static func show(at point: NSPoint) {
        close()
        let host = NSHostingView(rootView: CatMenuView(close: { close() }))
        let size = host.fittingSize
        let p = KeyPanel(contentRect: NSRect(origin: .zero, size: size),
                         styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false; p.backgroundColor = .clear; p.hasShadow = true
        p.level = .popUpMenu
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        let glass = NSVisualEffectView(frame: NSRect(origin: .zero, size: size))   // sized first: autoresizing adds the growth
        glass.material = .menu; glass.state = .active; glass.blendingMode = .behindWindow
        glass.wantsLayer = true; glass.layer?.cornerRadius = 12; glass.layer?.masksToBounds = true
        host.frame = NSRect(origin: .zero, size: size); host.autoresizingMask = [.width, .height]
        glass.addSubview(host)
        p.contentView = glass
        // Beside the pointer, kept on the screen it is on.
        let vis = (NSScreen.screens.first { $0.frame.contains(point) } ?? NSScreen.main)?.visibleFrame ?? .zero
        var o = NSPoint(x: point.x + 8, y: point.y - size.height)
        if o.x + size.width > vis.maxX { o.x = point.x - size.width - 8 }
        o.x = max(vis.minX + 4, o.x); o.y = min(max(vis.minY + 4, o.y), vis.maxY - size.height - 4)
        p.setFrameOrigin(o)
        p.makeKeyAndOrderFront(nil)
        panel = p
        monitors = [
            NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { _ in
                MainActor.assumeIsolated { close() }
            } as Any,
            NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .keyDown]) { e in
                MainActor.assumeIsolated { () -> NSEvent? in
                    if e.type == .keyDown { if e.keyCode == 53 { close(); return nil } }      // Esc
                    else if e.window !== panel && !(e.window is CrewNSPanel) { close() }
                    return e
                }
            } as Any,
        ]
    }

    static func close() {
        monitors.forEach(NSEvent.removeMonitor); monitors = []
        panel?.orderOut(nil); panel = nil
    }
}

/// A borderless panel that can take key (so Esc and the controls work) without activating the app.
final class KeyPanel: NSPanel {
    override var canBecomeKey: Bool { true }
}

/// The panel takes its colour from the cat you picked (owner 13 Oct 2026: "based on the cat I select can the theme
/// change?"). One accent per coat, drawn from its fur, deep enough that a white glyph on it reads in light and dark.
extension CatCoat {
    var accent: Color {
        switch id {
        case "smokey": return Color(red: 0.42, green: 0.50, blue: 0.66)     // slate
        case "patches": return Color(red: 0.86, green: 0.44, blue: 0.28)    // calico terracotta
        case "tux": return Color(red: 0.28, green: 0.47, blue: 0.82)        // tuxedo blue
        case "butter": return Color(red: 0.84, green: 0.62, blue: 0.14)     // butter gold
        case "cocoa": return Color(red: 0.60, green: 0.39, blue: 0.25)      // cocoa
        case "snow": return Color(red: 0.36, green: 0.66, blue: 0.86)       // ice blue
        case "lilac": return Color(red: 0.60, green: 0.47, blue: 0.84)      // lilac
        default: return Color(red: 0.95, green: 0.55, blue: 0.20)           // ginger orange
        }
    }
}

extension Color {
    static var catAccent: Color { CatCoat.current.accent }
}
