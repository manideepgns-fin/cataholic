import AppKit
import SwiftUI

// CrewHerd — the cat multiplier (owner 27 Sep 2026: "my screen covered with cats … the right button i can select
// how many cats"). The extra cats live in ONE full-screen overlay window that ignores the mouse, so a screen full
// of cats never blocks a click; only the main cat (CrewPanel) is pettable. Each herd cat is a tiny state machine —
// rain in from above the screen, land, sit, wander along its floor, nap melted flat, dash (zoomies) — standing on
// the same floors as the main cat (CrewGravity: the Dock line and the tops of windows). Random coats.
@MainActor
final class CrewHerd: ObservableObject {
    static let sizes = [0, 3, 10, 25, 50, 100]
    static var count: Int { UserDefaults.standard.integer(forKey: "crewHerd") }
    static func set(_ n: Int) { UserDefaults.standard.set(n, forKey: "crewHerd"); shared.restart() }

    enum Mode { case fall, sit, walk, zoom, nap }
    struct Cat: Identifiable {
        let id: Int
        let coat: CatCoat
        var x: CGFloat, feet: CGFloat              // top-space (y down from the screen top): box left, feet line
        var vx: CGFloat = 0, vy: CGFloat = 0
        var mode = Mode.fall
        var until = 0.0                            // when the current mode ends (clock seconds)
        var target: CGFloat = 0
        var facing: CGFloat = 1
        var phase = 0.0
        var squash: CGFloat = 1
    }

    static let shared = CrewHerd()
    @Published private(set) var cats: [Cat] = []
    private var window: NSPanel?
    private var timer: Timer?
    private var wins: [(id: Int, rect: CGRect)] = []
    private var winsAt = 0.0, last = 0.0
    private var clock: Double { Date().timeIntervalSinceReferenceDate }

    /// Rebuild the herd for the chosen count (menu, launch, screen change). Quiet mode and Reduce Motion: no herd.
    func restart() {
        timer?.invalidate(); timer = nil
        let n = CrewPrefs.quiet || Fx.reduced || !CrewPrefs.showCrew ? 0 : Self.count
        guard n > 0, let screen = CrewScreen.home else { window?.orderOut(nil); cats = []; return }
        let w = CrewLayout.screenW
        cats = (0..<n).map { i in
            Cat(id: i, coat: CatCoat.all.randomElement()!,
                x: .random(in: screen.frame.minX...(screen.frame.maxX - w)),
                feet: -.random(in: 40...900),                          // above the screen: they rain in, staggered
                vx: .random(in: -120...120))
        }
        if window == nil {
            let p = NSPanel(contentRect: screen.frame, styleMask: [.borderless, .nonactivatingPanel],
                            backing: .buffered, defer: false)
            p.isOpaque = false; p.backgroundColor = .clear; p.hasShadow = false
            p.level = .floating
            p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
            p.ignoresMouseEvents = true                                // never blocks a click
            p.contentView = NSHostingView(rootView: HerdView(herd: self))
            window = p
        }
        window?.setFrame(screen.frame, display: true)
        window?.orderFront(nil)
        CrewPanel.raise()                                              // the main cat stays on top of its herd
        last = clock
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.step() }
        }
    }

    private func step() {
        guard let screen = CrewScreen.home else { return }
        let now = clock, dt = CGFloat(min(now - last, 1.0 / 15)); last = now
        if now - winsAt > 1 { wins = CrewGravity.windows(); winsAt = now }        // floors, once a second
        let w = CrewLayout.screenW, z = CrewLayout.zoom
        let minX = screen.frame.minX, maxX = screen.frame.maxX - w
        func floor(_ c: Cat, from y: CGFloat) -> CrewGravity.Floor {
            CrewGravity.floorBelow(x: c.x + w / 2, fromY: y, in: screen, wins: wins)
        }
        for i in cats.indices {
            var c = cats[i]
            c.squash += (1 - c.squash) * min(1, dt * 9)
            switch c.mode {
            case .fall:
                let prev = c.feet
                c.vy += CrewGravity.g * dt
                c.x += c.vx * dt; c.feet += c.vy * dt
                if c.x < minX { c.x = minX; c.vx = -c.vx * 0.5 } else if c.x > maxX { c.x = maxX; c.vx = -c.vx * 0.5 }
                if c.vy > 0, c.feet >= 0 {
                    let f = floor(c, from: max(prev, 0))
                    if c.feet >= f.y { c.feet = f.y; c.vy = 0; c.vx = 0; c.squash = 0.72; settle(&c, now) }
                }
            case .walk, .zoom:
                let speed: CGFloat = c.mode == .zoom ? 430 : 70
                let d = c.target - c.x
                let stepX = min(abs(d), speed * dt) * (d < 0 ? -1 : 1)
                c.x += stepX
                c.facing = d < 0 ? -1 : 1
                c.phase += Double(abs(stepX) / z) / (c.mode == .zoom ? 6 : 3)
                if abs(d) < 1 { c.phase = 0; if c.mode == .zoom { c.squash = 0.86 }; settle(&c, now) }
                if !onFloor(c, screen) { c.mode = .fall; c.phase = 0 }            // walked off the end of a window
            case .sit, .nap:
                if now > c.until { next(&c, now, screen) }
            }
            cats[i] = c
        }
        // a window moved or closed under a sitting cat: it falls (checked in a rolling slice — cheap at 100 cats)
        if !cats.isEmpty {
            let k = Int(now * 30) % cats.count
            if cats[k].mode == .sit || cats[k].mode == .nap, !onFloor(cats[k], screen) { cats[k].mode = .fall }
        }
    }
    private func onFloor(_ c: Cat, _ screen: NSScreen) -> Bool {
        CrewGravity.supported(x: c.x + CrewLayout.screenW / 2, feetY: c.feet, in: screen, wins: wins)
    }
    private func settle(_ c: inout Cat, _ now: Double) { c.mode = .sit; c.until = now + .random(in: 1.5...5) }
    /// What a cat does next: mostly wander, often nap, sometimes zoom.
    private func next(_ c: inout Cat, _ now: Double, _ screen: NSScreen) {
        let f = CrewGravity.floorBelow(x: c.x + CrewLayout.screenW / 2, fromY: c.feet - 3, in: screen, wins: wins)
        let lo = max(f.x0, screen.frame.minX), hi = max(lo, min(f.x1, screen.frame.maxX) - CrewLayout.screenW)
        switch Int.random(in: 0..<100) {
        case 0..<45: c.mode = .walk; c.target = .random(in: lo...hi)
        case 45..<72: c.mode = .nap; c.until = now + .random(in: 6...20)
        case 72..<84: c.mode = .zoom; c.target = abs(c.x - lo) > abs(c.x - hi) ? lo : hi
        default: c.mode = .sit; c.until = now + .random(in: 2...6); if Bool.random() { c.facing *= -1 }
        }
    }
}

/// The herd drawn in the overlay: each cat is the same CrewCharacter as the main cat, in its own coat.
private struct HerdView: View {
    @ObservedObject var herd: CrewHerd
    var body: some View {
        let top = CrewScreen.home?.frame.maxY ?? 0, left = CrewScreen.home?.frame.minX ?? 0
        ZStack(alignment: .topLeading) {
            ForEach(herd.cats) { c in
                CrewCharacter(state: skin(c), badge: "", colors: CrewSkinColors(),
                              runPhase: c.phase, pin: false, stride: c.mode == .zoom ? 38 : 22, coatOverride: c.coat)
                    .scaleEffect(x: c.facing * (1 + (1 - c.squash) * 0.6), y: c.squash, anchor: UnitPoint(x: 0.5, y: 49 / 62))
                    .offset(x: c.x - left, y: c.feet - CrewGravity.feet)
            }
        }
        .frame(width: CrewScreen.home?.frame.width ?? 0, height: top - (CrewScreen.home?.frame.minY ?? 0), alignment: .topLeading)
        .allowsHitTesting(false)
    }
    private func skin(_ c: CrewHerd.Cat) -> CrewSkinState {
        switch c.mode {
        case .fall: return .alert
        case .walk, .zoom: return .running
        case .nap: return .sleeping
        case .sit: return .look
        }
    }
}
