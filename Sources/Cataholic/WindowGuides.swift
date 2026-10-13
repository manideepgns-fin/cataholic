import AppKit

// WindowGuides — a grid while you move a window (owner 13 Oct 2026: "when apps are moved … it shows grids on the
// screen, once you properly place within the lines there is a small happy vibration").
// Drag any window (any app, the Raycast bar too) → dashed lines fade in: a square grid fitted to that screen (centre lines stronger) always,
// plus the edges of other windows once the moving window comes within `near` of them. An edge or the centre of the
// moving window landing on a line lights it up and taps the trackpad once (Force Touch trackpads; a mouse gets the light).
// No permission needed: a global monitor sees mouse drags, CGWindowList gives every window's bounds (titles would need
// Screen Recording — we never read them). Coordinates: y DOWN from the top of the menu-bar display, as CGWindowList.
@MainActor
enum WindowGuides {
    static var isOn: Bool { UserDefaults.standard.bool(forKey: "windowGuides") }
    static func setOn(_ on: Bool) { UserDefaults.standard.set(on, forKey: "windowGuides"); on ? start() : stop() }

    static let near: CGFloat = 60        // a neighbour's edge shows up once we're this close
    static let snap: CGFloat = 4         // ≤ this from a line counts as "on" it

    private static var monitor: Any?
    private static var target: (id: CGWindowID, start: CGRect)?
    private static var moving = false
    private static var others: [CGRect] = []
    private static var lit: Set<Guide> = []
    private static var overlay: GuideOverlay?

    static func start() {
        guard monitor == nil else { return }
        monitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .leftMouseDragged, .leftMouseUp]) { e in
            let type = e.type
            MainActor.assumeIsolated {
                switch type {
                case .leftMouseDown: begin()
                case .leftMouseDragged: drag()
                default: end()
                }
            }
        }
    }
    static func stop() {
        if let m = monitor { NSEvent.removeMonitor(m) }
        monitor = nil; end()
    }

    /// The mouse in top-space (NSEvent gives y UP from the bottom of the menu-bar display).
    private static var mouse: CGPoint {
        let p = NSEvent.mouseLocation, h = NSScreen.screens.first?.frame.height ?? 0
        return CGPoint(x: p.x, y: h - p.y)
    }

    /// Every window on screen, front to back, below the Dock/menu-bar layers (so Raycast's panel counts).
    private static func onScreen() -> [(id: CGWindowID, rect: CGRect, layer: Int)] {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
                as? [[String: Any]] else { return [] }
        let me = Int(ProcessInfo.processInfo.processIdentifier)
        return list.compactMap { w in
            guard let layer = w[kCGWindowLayer as String] as? Int, layer >= 0, layer < 20,
                  (w[kCGWindowOwnerPID as String] as? Int) != me,
                  (w[kCGWindowAlpha as String] as? Double ?? 1) > 0.1,
                  let b = w[kCGWindowBounds as String] as? [String: CGFloat],
                  let id = w[kCGWindowNumber as String] as? Int else { return nil }
            let r = CGRect(x: b["X"] ?? 0, y: b["Y"] ?? 0, width: b["Width"] ?? 0, height: b["Height"] ?? 0)
            return r.width >= 120 && r.height >= 40 ? (CGWindowID(id), r, layer) : nil
        }
    }
    private static func bounds(_ id: CGWindowID) -> CGRect? {
        guard let d = (CGWindowListCopyWindowInfo(.optionIncludingWindow, id) as? [[String: Any]])?.first,
              let b = d[kCGWindowBounds as String] as? [String: CGFloat] else { return nil }
        return CGRect(x: b["X"] ?? 0, y: b["Y"] ?? 0, width: b["Width"] ?? 0, height: b["Height"] ?? 0)
    }

    private static func begin() {
        end()
        let p = mouse, wins = onScreen()
        guard let hit = wins.first(where: { $0.rect.contains(p) }) else { return }
        target = (hit.id, hit.rect)
        others = wins.filter { $0.id != hit.id && $0.layer == 0 }.map(\.rect)
    }

    private static func drag() {
        guard let t = target, let r = bounds(t.id) else { return }
        // A move keeps the size; a resize (or a drag inside the window — text, a slider) does not move the origin.
        guard r.size == t.start.size, r.origin != t.start.origin else { return }
        let screen = NSScreen.screens.first { topRect($0).contains(CGPoint(x: r.midX, y: r.midY)) } ?? NSScreen.main
        guard let screen else { return }
        if !moving { moving = true; lit = [] }
        let s = topRect(screen)
        // Always: a square grid over the whole display, sized from its current resolution (System Settings → Displays).
        var guides = squares(s)
        // As you get close: the edges of the other windows on this screen.
        let xs = [r.minX, r.midX, r.maxX], ys = [r.minY, r.midY, r.maxY]
        for o in others where o.intersects(s) {
            for x in [o.minX, o.maxX] where xs.contains(where: { abs($0 - x) <= near }) {
                guides.append(Guide(vertical: true, at: x, smart: true))
            }
            for y in [o.minY, o.maxY] where ys.contains(where: { abs($0 - y) <= near }) {
                guides.append(Guide(vertical: false, at: y, smart: true))
            }
        }
        let on = Set(guides.filter { g in (g.vertical ? xs : ys).contains { abs($0 - g.at) <= snap } })
        if !on.subtracting(lit).isEmpty {
            NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
        }
        lit = on
        let o = overlay ?? GuideOverlay()
        overlay = o
        o.show(on: screen, top: s, guides: Array(Set(guides)), lit: on, moving: r)
    }

    private static func end() {
        target = nil; moving = false; lit = []
        overlay?.fadeOut()
    }

    /// A chessboard built out from the centre (owner 13 Oct 2026: "there are centre lines and on that the squares need
    /// to be built"): the two centre lines first, then squares stepping out from them. The square is the half-width
    /// split into whole steps of about 220 pt, so the columns end exactly on the left and right edges; rows step up and
    /// down from the centre by the same amount and the outer ones are cut by the screen. Whole screen, at its current
    /// resolution: 1512 × 982 (14″ MacBook Pro) → 252 pt squares, 1920 × 1080 → 240 pt.
    static func squares(_ v: CGRect) -> [Guide] {
        let cx = v.midX, cy = v.midY
        let side = (v.width / 2) / max(1, (v.width / 2 / 220).rounded())
        var out = [Guide(vertical: true, at: cx.rounded(), smart: false, major: true),
                   Guide(vertical: false, at: cy.rounded(), smart: false, major: true)]
        for i in 1...40 {
            let d = CGFloat(i) * side
            if d > v.width / 2 + 1, d > v.height / 2 + 1 { break }
            if d <= v.width / 2 + 1 {
                out += [Guide(vertical: true, at: (cx - d).rounded(), smart: false), Guide(vertical: true, at: (cx + d).rounded(), smart: false)]
            }
            if d < v.height / 2 {
                out += [Guide(vertical: false, at: (cy - d).rounded(), smart: false), Guide(vertical: false, at: (cy + d).rounded(), smart: false)]
            }
        }
        return out
    }

    static func topRect(_ s: NSScreen) -> CGRect {
        let h = NSScreen.screens.first?.frame.height ?? 0
        return CGRect(x: s.frame.minX, y: h - s.frame.maxY, width: s.frame.width, height: s.frame.height)
    }
    static func topVisible(_ s: NSScreen) -> CGRect {
        let h = NSScreen.screens.first?.frame.height ?? 0
        return CGRect(x: s.visibleFrame.minX, y: h - s.visibleFrame.maxY, width: s.visibleFrame.width, height: s.visibleFrame.height)
    }
}

struct Guide: Hashable { let vertical: Bool; let at: CGFloat; let smart: Bool; var major = false }

/// One click-through, see-through window over the screen being used; it only draws lines.
@MainActor
final class GuideOverlay {
    private let panel: NSPanel
    private let view = GuideView()
    private var fade: DispatchWorkItem?

    init() {
        panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.ignoresMouseEvents = true
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        panel.contentView = view
        panel.alphaValue = 0
    }

    func show(on screen: NSScreen, top: CGRect, guides: [Guide], lit: Set<Guide>, moving: CGRect) {
        fade?.cancel()
        if panel.frame != screen.frame { panel.setFrame(screen.frame, display: false) }
        view.origin = top.origin
        view.guides = guides
        view.lit = lit
        view.moving = moving
        view.luma = Wallpaper.map(for: screen)
        view.needsDisplay = true
        if !panel.isVisible { panel.orderFrontRegardless() }
        if panel.alphaValue < 1 {
            NSAnimationContext.runAnimationGroup { $0.duration = 0.15; panel.animator().alphaValue = 1 }
        }
    }

    func fadeOut() {
        guard panel.isVisible else { return }
        let w = DispatchWorkItem { [panel] in
            NSAnimationContext.runAnimationGroup({ $0.duration = 0.3; panel.animator().alphaValue = 0 },
                                                 completionHandler: { if panel.alphaValue == 0 { panel.orderOut(nil) } })
        }
        fade = w
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4, execute: w)
    }
}

/// How bright the wallpaper is, patch by patch (owner 13 Oct 2026: "understand the wallpaper"). Read from the
/// wallpaper FILE (NSWorkspace — no permission needed; the live screen would need Screen Recording), drawn the way the
/// Desktop fills the screen (aspect-fill, centred) into a tiny 64 × 40 grey map. Cached per screen + file.
/// No readable file (a dynamic / aerial wallpaper) → nil, and the lines fall back to light-on-dark.
struct LumaMap {
    let w: Int, h: Int, v: [UInt8]
    /// Brightness 0…1 at a point given as fractions of the screen (0,0 = top-left).
    func at(_ fx: CGFloat, _ fy: CGFloat) -> CGFloat {
        let x = min(w - 1, max(0, Int(fx * CGFloat(w)))), y = min(h - 1, max(0, Int(fy * CGFloat(h))))
        return CGFloat(v[y * w + x]) / 255
    }
}

@MainActor
enum Wallpaper {
    private static var cache: [String: LumaMap] = [:]
    static func map(for screen: NSScreen) -> LumaMap? {
        guard var url = NSWorkspace.shared.desktopImageURL(for: screen) else { return nil }
        // Wallpaper apps (Unsplash Wallpapers…) swap the file and delete the old one while macOS still reports the
        // old path — then the newest image in that folder is the one on screen.
        if !FileManager.default.fileExists(atPath: url.path) {
            let dir = url.deletingLastPathComponent()
            let files = (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []
            func date(_ u: URL) -> Date { (try? u.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast }
            guard let newest = files.filter({ ["jpg", "jpeg", "png", "heic", "tiff"].contains($0.pathExtension.lowercased()) })
                .max(by: { date($0) < date($1) }) else { return nil }
            url = newest
        }
        let key = "\(screen.frame)|\(url.path)|\((try? url.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast)"
        if let m = cache[key] { return m }
        guard let img = NSImage(contentsOf: url)?.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        let w = 64, h = 40
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w,
                                  space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return nil }
        // aspect-fill the screen's shape, centred (the Desktop's default "Fill Screen")
        let sw = screen.frame.width, sh = screen.frame.height
        let scale = max(sw / CGFloat(img.width), sh / CGFloat(img.height))
        let dw = CGFloat(img.width) * scale / sw * CGFloat(w), dh = CGFloat(img.height) * scale / sh * CGFloat(h)
        ctx.interpolationQuality = .medium
        ctx.draw(img, in: CGRect(x: (CGFloat(w) - dw) / 2, y: (CGFloat(h) - dh) / 2, width: dw, height: dh))
        guard let data = ctx.data else { return nil }
        let m = LumaMap(w: w, h: h, v: Array(UnsafeBufferPointer(start: data.assumingMemoryBound(to: UInt8.self), count: w * h)))
        cache = [key: m]
        return m
    }
}

/// The look (owner 13 Oct 2026: "better styled grid lines" → "understand the wallpaper"): hairline grid with a dot at
/// each crossing and brighter centre lines with a small crosshair, all INKED BY THE WALLPAPER under them — dark ink
/// over light patches, light ink over dark ones, changing along a line as the wallpaper changes, each with a faint
/// opposite twin so it still reads over a window. Neighbour edges are a calm blue dash; an aligned line glows mint green.
final class GuideView: NSView {
    var origin: CGPoint = .zero          // the screen's top-left in top-space
    var guides: [Guide] = []
    var lit: Set<Guide> = []
    var luma: LumaMap?
    var moving: CGRect = .null          // the dragged window, top-space
    override var isFlipped: Bool { true }

    private static let blue = NSColor(srgbRed: 0.35, green: 0.68, blue: 1.0, alpha: 0.9)
    /// "You got it" (owner 13 Oct 2026: the orange "needs to be some other color") — a mint green that reads as
    /// correct, stays clear of the cats' orange, and glows on dark and light wallpapers alike.
    private static let mint = NSColor(srgbRed: 0.24, green: 0.90, blue: 0.62, alpha: 1)
    private static let step: CGFloat = 12          // a line changes ink every 12 pt at most

    /// Ink for a spot: (line, twin). `strength` 1 = grid, more = centre lines and marks.
    private func ink(_ p: CGPoint, _ strength: CGFloat = 1) -> (NSColor, NSColor) {
        let light = (luma?.at(p.x / max(1, bounds.width), p.y / max(1, bounds.height)) ?? 0) > 0.55
        return light ? (NSColor.black.withAlphaComponent(min(0.85, 0.32 * strength)), NSColor.white.withAlphaComponent(0.3))
                     : (NSColor.white.withAlphaComponent(min(0.95, 0.34 * strength)), NSColor.black.withAlphaComponent(0.28))
    }

    override func draw(_ dirty: NSRect) {
        let hair = 1 / (window?.backingScaleFactor ?? 2)
        // The window's shadow on all four sides (owner 13 Oct 2026: "a greyed out movement shower on all four sides, very
        // light"): a faint band at the window's height across the screen and one at its width down the screen.
        if !moving.isNull {
            let r = moving.offsetBy(dx: -origin.x, dy: -origin.y)
            NSColor(white: 0.5, alpha: 0.08).setFill()
            for arm in [NSRect(x: 0, y: r.minY, width: r.minX, height: r.height),
                        NSRect(x: r.maxX, y: r.minY, width: bounds.width - r.maxX, height: r.height),
                        NSRect(x: r.minX, y: 0, width: r.width, height: r.minY),
                        NSRect(x: r.minX, y: r.maxY, width: r.width, height: bounds.height - r.maxY)] where arm.width > 0 && arm.height > 0 {
                arm.fill(using: .sourceOver)
            }
        }
        for g in guides where !g.smart && !lit.contains(g) { inked(g, width: g.major ? 1 : hair, strength: g.major ? 2 : 1) }
        // a dot where two grid lines cross
        let xs = guides.filter { $0.vertical && !$0.smart }.map(\.at), ys = guides.filter { !$0.vertical && !$0.smart }.map(\.at)
        for x in xs { for y in ys {
            let p = local(x, y), (c, t) = ink(p, 2.6), dot = NSBezierPath(ovalIn: NSRect(x: p.x - 2, y: p.y - 2, width: 4, height: 4))
            c.setFill(); dot.fill(); t.setStroke(); dot.lineWidth = 0.75; dot.stroke()
        } }
        if let cx = guides.first(where: { $0.major && $0.vertical })?.at, let cy = guides.first(where: { $0.major && !$0.vertical })?.at {
            let c = local(cx, cy), r: CGFloat = 9, (fg, tw) = ink(c, 3)
            let plus = NSBezierPath()
            plus.move(to: NSPoint(x: c.x - r, y: c.y)); plus.line(to: NSPoint(x: c.x + r, y: c.y))
            plus.move(to: NSPoint(x: c.x, y: c.y - r)); plus.line(to: NSPoint(x: c.x, y: c.y + r))
            plus.append(NSBezierPath(ovalIn: NSRect(x: c.x - 5, y: c.y - 5, width: 10, height: 10)))
            tw.setStroke(); plus.lineWidth = 3.5; plus.stroke()
            fg.setStroke(); plus.lineWidth = 1.5; plus.stroke()
        }
        for g in guides where g.smart && !lit.contains(g) { solid(g, Self.blue, 1, dash: [4, 4]) }
        for g in lit { solid(g, Self.mint, 1.5, glow: true) }
    }

    private func local(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x - origin.x, y: y - origin.y) }

    /// A grid line drawn in short pieces, each inked by the wallpaper under its middle.
    private func inked(_ g: Guide, width: CGFloat, strength: CGFloat) {
        let a = g.at - (g.vertical ? origin.x : origin.y), len = g.vertical ? bounds.height : bounds.width
        var s: CGFloat = 0
        while s < len {
            let e = min(len, s + Self.step), m = (s + e) / 2
            let pt = g.vertical ? CGPoint(x: a, y: m) : CGPoint(x: m, y: a)
            let (c, t) = ink(pt, strength)
            for (color, o) in [(t, width), (c, 0)] {
                let p = NSBezierPath()
                if g.vertical { p.move(to: NSPoint(x: a + o, y: s)); p.line(to: NSPoint(x: a + o, y: e)) }
                else { p.move(to: NSPoint(x: s, y: a + o)); p.line(to: NSPoint(x: e, y: a + o)) }
                p.lineWidth = width; color.setStroke(); p.stroke()
            }
            s = e
        }
    }

    private func solid(_ g: Guide, _ color: NSColor, _ width: CGFloat, dash: [CGFloat] = [], glow: Bool = false) {
        let a = g.at - (g.vertical ? origin.x : origin.y)
        func path(_ o: CGFloat) -> NSBezierPath {
            let p = NSBezierPath()
            if g.vertical { p.move(to: NSPoint(x: a + o, y: 0)); p.line(to: NSPoint(x: a + o, y: bounds.height)) }
            else { p.move(to: NSPoint(x: 0, y: a + o)); p.line(to: NSPoint(x: bounds.width, y: a + o)) }
            if !dash.isEmpty { p.setLineDash(dash, count: dash.count, phase: 0) }
            p.lineWidth = width
            return p
        }
        NSColor.black.withAlphaComponent(0.25).setStroke(); path(width).stroke()
        NSGraphicsContext.saveGraphicsState()
        if glow {
            let s = NSShadow(); s.shadowColor = color.withAlphaComponent(0.9); s.shadowBlurRadius = 8; s.shadowOffset = .zero; s.set()
        }
        color.setStroke(); path(0).stroke()
        NSGraphicsContext.restoreGraphicsState()
    }
}
