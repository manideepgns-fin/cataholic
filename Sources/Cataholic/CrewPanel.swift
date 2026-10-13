import AppKit
import Combine
import SwiftUI

// CrewPanel — the borderless non-activating window the cat hangs in (same
// level/collection behaviour as the notch: .floating, all Spaces,
// full-screen auxiliary, no shadow), on the menu-bar display (CrewScreen.home).
// The panel is only as big as the character, so it never blocks clicks elsewhere.
// The billing pane is GONE (26 Sep 2026): a click on the cat opens the web
// Billing Desk in the default browser. Quiet mode: characters shrink to 7pt
// dots (14 pt on screen) on the edge, amber when the desk needs you.
// Sizes: layout and drawing are 1× points (CrewLayout); this file works in SCREEN
// points, so every cat-sized number here is × CrewLayout.zoom.
@MainActor
enum CrewPanel {
    private static var panel: CrewNSPanel?
    private static var model: CrewModel?

    static var isOn: Bool { CrewPrefs.showCrew }
    static func setOn(_ on: Bool) { CrewPrefs.setShowCrew(on) }

    static func currentModel() -> CrewModel? { model }

    /// The web desk is open in the browser (best-effort — the browser owns
    /// the window; used to hush the whisper while the owner works the desk).
    nonisolated(unsafe) static var deskOpen = false

    static func show() {
        guard CrewPrefs.showCrew else { return }
        if perchTimer == nil { watchPerch() }
        let m = model ?? { let x = CrewModel(); model = x; return x }()
        if let p = panel {
            p.orderFront(nil)
            p.refit()
            return
        }
        let p = CrewNSPanel(model: m)
        p.orderFront(nil)
        p.refit()
        panel = p
        CrewHerd.shared.restart()                  // the multiplier's herd comes back with the app
    }

    static func hide() { roam = nil; panel?.orderOut(nil) }
    static var visible: Bool { panel?.isVisible == true }
    /// The pointer is on her — by geometry: .onHover can miss the exit when she walks out from under a still pointer.
    static var pointerOnHer: Bool {
        charScreenRect()?.insetBy(dx: 6 * CrewLayout.zoom, dy: 6 * CrewLayout.zoom).contains(NSEvent.mouseLocation) == true
    }

    /// Double-click on the cat (owner 13 Oct 2026: "double click takes to find my cat, only one feature, simple"):
    /// the Find my cat game. Quick launch and its favourite are gone.
    static func openGame() {
        model?.dismissWhisper()
        FindMyCat.open()
    }

    // MARK: — gravity (owner 27 Sep 2026, step 2 of "make it lively")

    /// Hanging on its pin near the menu bar (the old home, and the default) — or standing on a floor
    /// (the Dock line or a window's top edge, CrewGravity). Put down near the menu bar → it hangs;
    /// anywhere lower → it falls and stands.
    static var hanging: Bool { UserDefaults.standard.object(forKey: "crewHang") as? Bool ?? true }
    static func setHanging(_ on: Bool) { UserDefaults.standard.set(on, forKey: "crewHang") }
    /// The character box's top-left in top-space (y down) — where the cat is DRAWN right now.
    static func charTopLeft(_ screen: NSScreen) -> CGPoint? {
        guard let r = panel?.charRect() else { return nil }
        return CGPoint(x: r.minX, y: screen.frame.maxY - r.maxY)
    }
    /// Remember where the cat now lives (the keys a drag writes) and let the panel sit there.
    static func settleHome(left: CGFloat, top: CGFloat, screen: NSScreen) {
        // The window she now sits on and where along it (on her own she rides it when it slides sideways — checkPerch)
        let wins = CrewGravity.windows(), fl = CrewGravity.floorBelow(x: left + CrewLayout.screenW / 2,
                                                                       fromY: top + CrewGravity.feet - 3, in: screen, wins: wins)
        perch = (abs(fl.y - top - CrewGravity.feet) > 3 ? nil : fl.window).flatMap { id in wins.first { $0.id == id } }.map { (id: $0.id, dx: left - $0.rect.minX, top: $0.rect.minY) }
        let d = UserDefaults.standard
        d.set(Double(left - screen.frame.minX), forKey: "crewLeft")
        d.set(Double(top), forKey: "crewTop")
        roam = nil
        panel?.refit()
        calmChecks = 0                                   // a new perch: watch it closely for a moment
    }
    private static var fallTimer: Timer? = nil
    /// The window under her feet: its id, her left minus its left, and its top (settleHome).
    private static var perch: (id: Int, dx: CGFloat, top: CGFloat)? = nil
    /// Let go of the cat, or knock it off its perch: it flies with (vx, vy) pt/s (y down), bounces off the
    /// screen sides and the menu bar, and lands on the first floor under it — squash, and one small bounce
    /// when it came down hard. Wide eyes while airborne (CrewView).
    static func drop(vx: CGFloat = 0, vy: CGFloat = 0, through: Int? = nil, done: (() -> Void)? = nil) {
        guard let screen = CrewScreen.home, let start = charTopLeft(screen) else { done?(); return }
        fallTimer?.invalidate()
        climbed = false
        let wins = CrewGravity.windows().filter { $0.id != through }   // `through`: the perch she was knocked off
        let w = CrewLayout.screenW, feet = CrewGravity.feet
        let minX = screen.frame.minX, maxX = screen.frame.maxX - w
        let ceiling = screen.frame.maxY - screen.visibleFrame.maxY + 2
        if Fx.reduced {                                   // no flight: straight onto the floor below
            let fl = CrewGravity.floorBelow(x: start.x + w / 2, fromY: start.y + feet - 3, in: screen, wins: wins)
            settleHome(left: start.x, top: fl.y - feet, screen: screen); done?(); return
        }
        var x = start.x, y = start.y, vx = vx, vy = vy, last = Date(), bounces = 0
        model?.airborne = true
        fallTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { t in   // 30 Hz: she moves a lot on her own (CPU)
            MainActor.assumeIsolated {
                let now = Date(), dt = CGFloat(min(now.timeIntervalSince(last), 1.0 / 15)); last = now
                let prevFeet = y + feet
                vy += CrewGravity.g * dt
                x += vx * dt; y += vy * dt
                if x < minX { x = minX; vx = -vx * 0.55 } else if x > maxX { x = maxX; vx = -vx * 0.55 }
                if y < ceiling { y = ceiling; vy = abs(vy) * 0.3 }
                if vy > 0 {
                    let fl = CrewGravity.floorBelow(x: x + w / 2, fromY: prevFeet, in: screen, wins: wins)
                    if y + feet >= fl.y {
                        y = fl.y - feet
                        if vy > 1100 && bounces == 0 {        // came down hard: one little bounce
                            bounces = 1; vy = -vy * 0.28; vx *= 0.5; model?.land(0.7)
                        } else {
                            t.invalidate(); fallTimer = nil
                            model?.airborne = false
                            model?.land(0.7)
                            settleHome(left: x, top: y, screen: screen)
                            done?()
                            model?.chain()
                            return
                        }
                    }
                }
                roam = (x, y)
                panel?.refit()
            }
        }
    }
    /// The perch check: the window under it closed, moved or minimised → it falls; the window rose a little
    /// under it → it rides up with it. Every 0.4 s while a mouse button is down (you may be dragging its window)
    /// or just after something changed; once it has sat still for ~2 s it looks only every 1.2 s (battery).
    private static var perchTimer: Timer? = nil
    private static var calmChecks = 0, perchTick = 0
    static func watchPerch() {
        perchTimer?.invalidate()
        perchTimer = Timer.scheduledTimer(withTimeInterval: 0.4, repeats: true) { _ in
            MainActor.assumeIsolated { checkPerch() }
        }
    }
    static func checkPerch() {
        perchTick += 1
        if NSEvent.pressedMouseButtons != 0 { calmChecks = 0 }
        if calmChecks >= 5 && perchTick % 3 != 0 { return }
        guard !hanging, model?.airborne != true, !zooming, lifeTimer == nil, !CrewMove.holding, !CrewPrefs.quiet,
              panel?.isVisible == true, let screen = CrewScreen.home, let at = charTopLeft(screen) else { return }
        let wins = CrewGravity.windows(), cx = at.x + CrewLayout.screenW / 2, feetY = at.y + CrewGravity.feet
        // On her own she rides her window when you slide it sideways (same top, new left): she keeps her spot on it.
        if CrewPrefs.onHerOwn, let p = perch, let w = wins.first(where: { $0.id == p.id }), abs(w.rect.minY - p.top) < 1,
           case let nl = min(max(w.rect.minX + p.dx, screen.frame.minX), screen.frame.maxX - CrewLayout.screenW),
           abs(nl - at.x) >= 1, roam == nil {
            settleHome(left: nl, top: at.y, screen: screen)
            calmChecks = 0
            return
        }
        if CrewGravity.supported(x: cx, feetY: feetY, in: screen, wins: wins) { calmChecks += 1; return }
        calmChecks = 0
        if let w = wins.first(where: { CrewGravity.ledge($0.rect, screen).map { $0 < feetY && $0 > feetY - 90 } == true
                                        && cx > $0.rect.minX + 6 && cx < $0.rect.maxX - 6 }),
           let top = CrewGravity.ledge(w.rect, screen),
           CrewGravity.floorBelow(x: cx, fromY: top - 0.5, in: screen, wins: wins).window == w.id {
            settleHome(left: at.x, top: top - CrewGravity.feet, screen: screen)
            model?.land(0.88)
            return
        }
        drop()
    }

    // MARK: — zoomies (right-click → Zoomies)

    /// Zoomies, grounded (owner 27 Sep 2026: "can you do better zoomies?"): a hanging cat drops to the
    /// floor first. Then 3–4 dashes along THAT floor — a leg-scramble start, a galloping squash-and-stretch
    /// run, sometimes a hop mid-dash, a skid that leans back, a look around — and it stays where it ends.
    /// A click mid-run catches it (`catchCat`).
    private(set) static var zooming = false
    private static var caught = false
    static func zoomies() {
        guard !zooming, !Fx.reduced, panel != nil, model != nil else { return }
        zooming = true; caught = false
        if hanging { setHanging(false); drop { run() } } else { run() }
    }
    private static func run() {
        guard let screen = CrewScreen.home, let m = model, let at = charTopLeft(screen) else { zooming = false; return }
        let wins = CrewGravity.windows(), w = CrewLayout.screenW, feet = CrewGravity.feet
        let fl = CrewGravity.floorBelow(x: at.x + w / 2, fromY: at.y + feet - 3, in: screen, wins: wins)
        let lo = max(fl.x0, screen.frame.minX) + 4, hi = min(fl.x1, screen.frame.maxX) - w - 4
        guard hi - lo > 80 else { zooming = false; m.land(); return }          // no room to run: a squish
        var xs = [min(max(at.x, lo), hi)]
        for _ in 0..<Int.random(in: 3...4) {
            var q = xs[xs.count - 1]
            for _ in 0..<16 { q = .random(in: lo...hi); if abs(q - xs[xs.count - 1]) > (hi - lo) * 0.3 { break } }
            xs.append(q)
        }
        let legs = zip(xs, xs.dropFirst()).map { (a: $0, b: $1, hop: abs($1 - $0) > 300 && Bool.random()) }
        let floorTop = fl.y - feet, z = CrewLayout.zoom
        let topSpeed: CGFloat = 560, accel: CGFloat = 2400, brake: CGFloat = 2200
        var i = 0, x = xs[0], v: CGFloat = 0, phase = 0.0, stage = 0     // 0 scramble · 1 run · 2 skid · 3 look
        var stageT = Date(), last = Date(), pause = 0.45
        m.runStride = 38
        Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { t in
            MainActor.assumeIsolated {
                let now = Date(), dt = min(now.timeIntervalSince(last), 1.0 / 15); last = now
                let se = now.timeIntervalSince(stageT)
                if caught || i >= legs.count {
                    t.invalidate()
                    m.runFacing = 0; m.runPhase = 0; m.runLean = 0; m.runSpeed = 0; m.runGait = 0
                    zooming = false
                    settleHome(left: x, top: floorTop, screen: screen)
                    // a proud squash and a happy squint when it stops (owner 13 Oct 2026)
                    m.land(0.75)
                    m.petUntil = Date().addingTimeInterval(1.3)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.35) { MainActor.assumeIsolated { m.objectWillChange.send() } }
                    m.chain(1.4...2.4)                    // after the squint
                    return
                }
                let leg = legs[i], dir: CGFloat = leg.b >= leg.a ? 1 : -1
                var lift: CGFloat = 0
                m.runFacing = dir > 0 ? 1 : -1
                switch stage {
                case 0:                                   // scramble: legs blur, body leans in, no ground covered
                    phase += dt * 30; m.runLean = 7 * Double(dir)
                    if se > 0.3 { stage = 1; stageT = now }
                case 1:                                   // gallop
                    v = min(topSpeed, v + accel * CGFloat(dt))
                    x += dir * v * CGFloat(dt)
                    phase += Double(v * CGFloat(dt) / z) / 6
                    m.runLean = 0
                    let span = abs(leg.b - leg.a), p = span > 0 ? (x - leg.a) / (leg.b - leg.a) : 1
                    if leg.hop, p > 0.35, p < 0.65 { lift = CGFloat(sin(Double((p - 0.35) / 0.3) * .pi)) * 46 * z / 2 }
                    if (leg.b - x) * dir <= v * v / (2 * brake) + 2 { stage = 2; stageT = now }
                case 2:                                   // skid: lean back, legs planted, slide to a stop
                    v = max(0, v - brake * CGFloat(dt))
                    x += dir * v * CGFloat(dt)
                    m.runLean = -13 * Double(dir)
                    if v == 0 { stage = 3; stageT = now; pause = .random(in: 0.35...0.7); m.runLean = 0; m.land(0.88) }
                default:                                  // stand, glance back over the shoulder, go again
                    if se > pause * 0.3 && se < pause * 0.7 { m.runFacing = dir > 0 ? -1 : 1 }
                    if se > pause { i += 1; stage = Bool.random() ? 0 : 1; stageT = now; v = 0 }
                }
                x = min(max(x, lo), hi)
                m.runPhase = stage == 3 ? 0 : phase      // a skid (runGait 2) draws its legs braced; the phase drives its dust
                if stage == 2 { phase += dt * 20 }
                m.runSpeed = Double(v / topSpeed)
                m.runGait = stage == 0 ? 1 : (stage == 2 ? 2 : 0)
                roam = (x, floorTop - lift)
                panel?.refit()
            }
        }
    }

    /// A click on the running cat catches it: it stops right there with a squish.
    static func catchCat() { if zooming { caught = true } }

    // MARK: — cat-life panel movers (owner, 26 Sep 2026)

    /// Where the character box (46×62 × zoom on screen) sits while it roams away from home (a jump
    /// in flight, settled next to the pointer): top-left, screen points, y from
    /// the screen top. nil = home. The panel around it changes size with the
    /// pose (CrewLayout) — moving the CHARACTER keeps its feet anchored.
    static var roam: (left: CGFloat, top: CGFloat)? = nil
    /// One mover at a time: a 60 Hz timer gliding the character box. The
    /// zoomies timer owns its own path; life actions never run mid-zoomies
    /// (the scheduler guards) and a click stops them via stopLife().
    private static var lifeTimer: Timer? = nil
    private static var jumpWork: DispatchWorkItem? = nil
    static func stopLife() {
        jumpWork?.cancel()
        jumpWork = nil
        lifeTimer?.invalidate()
        lifeTimer = nil
        roam = nil
        if !zooming, let m = model { m.runFacing = 0; m.runPhase = 0; m.runStride = 38 }   // a walk cut short stands up
        fallTimer?.invalidate(); fallTimer = nil; model?.airborne = false                   // caught mid-air
        climbDir = 0; climbed = false; model?.climbRot = 0                                  // caught mid-climb
        if zooming { caught = true }
        model?.stopAction()
    }
    static func refit() { panel?.refit() }
    /// Keep the main (pettable) cat above the herd's overlay window.
    static func raise() { panel?.orderFront(nil) }
    /// Where the cat is drawn on screen right now (y up) — the drag grabs from here, not from the
    /// stored home, so a cat that jumped or walked over moves from where you see it.
    static func charScreenRect() -> NSRect? { panel?.charRect() }
    static func catCenter() -> NSPoint? {
        guard let p = panel else { return nil }
        let r = p.charRect()
        return NSPoint(x: r.midX, y: r.midY)
    }
    static func glide(toLeft: CGFloat, toTop: CGFloat, dur: Double, hop: Double = 0,
                              done: (() -> Void)? = nil) {
        guard let p = panel, let screen = CrewScreen.home else { return }
        lifeTimer?.invalidate()
        let r = p.charRect()
        let fromLeft = r.minX, fromTop = screen.frame.maxY - r.maxY, t0 = Date()
        // A move on the ground is a WALK (owner 27 Sep 2026: "it's not live like" — the cat slid like a sticker):
        // side-on, legs stepping with the distance covered, ~100 pt/s (about a body length a second). A hop (the leap) keeps its own pose.
        let dist = hypot(toLeft - fromLeft, toTop - fromTop)
        let walks = hop == 0 && dist > 12 && !Fx.reduced
        let dur = walks ? min(max(Double(dist) / 100, 0.6), 5) : dur
        var tick = 0
        lifeTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { timer in
            MainActor.assumeIsolated {
                tick += 1
                let e = Date().timeIntervalSince(t0)
                let u = min(e / dur, 1)
                // a walk keeps an even pace (gentle start/stop); a leap eases in and out
                let k = walks ? CGFloat(u < 0.12 ? u * u / 0.24 : u > 0.88 ? 1 - (1 - u) * (1 - u) / 0.24 : u - 0.06) / 0.88
                              : CGFloat(u < 0.5 ? 2 * u * u : 1 - pow(-2 * u + 2, 2) / 2)
                let lift = CGFloat(hop * sin(u * .pi))
                roam = (fromLeft + (toLeft - fromLeft) * k, fromTop + (toTop - fromTop) * k - lift)
                if walks, let m = model {
                    // set only on change: every @Published set redraws her and queues a refit (CPU, 13 Oct 2026)
                    let face = abs(toLeft - fromLeft) > 4 && toLeft < fromLeft ? -1 : 1
                    if m.runFacing != face { m.runFacing = face }
                    if m.runStride != 22 { m.runStride = 22 }
                    // legs drawn "on twos" (15 drawings a second, as cartoons do) while the window glides at 30 Hz:
                    // each drawing is a SwiftUI layout + render, half the CPU of a walk (owner 13 Oct 2026)
                    if tick % 2 == 1 { m.runPhase = Double(dist * k / CrewLayout.zoom) / 3 }   // one stride cycle ≈ 19 pt of drawing
                }
                p.refit()
                if u >= 1 {
                    timer.invalidate()
                    lifeTimer = nil
                    if walks, let m = model { m.runFacing = 0; m.runPhase = 0; m.runStride = 38 }
                    if hop > 0 { model?.land() }
                    done?()
                }
            }
        }
    }
    /// Jump: crouch 0.2 s, then a parabolic arc to the opposite side of its
    /// screen; where it lands is its new home (persisted like a drag).
    /// On her own: the next jump goes HERE (a window top) instead of across the screen — CrewOwn sets it, lifeJump uses it once.
    static var leapTarget: (left: CGFloat, top: CGFloat)? = nil
    static func lifeJump() {
        let target = leapTarget; leapTarget = nil
        guard !zooming, let p = panel, let screen = CrewScreen.home else { return }
        let vf = screen.visibleFrame, r = p.charRect()
        let fromLeft = r.minX, fromTop = screen.frame.maxY - r.maxY
        let leftX = screen.frame.minX + 8
        let rightX = max(leftX, vf.maxX - CrewLayout.screenW - 8)
        let destL = target?.left ?? (abs(fromLeft - leftX) < abs(fromLeft - rightX) ? rightX : leftX)
        let top = target?.top ?? fromTop
        // window to window: the arc clears the higher ledge by 40 pt, so a leap up never cuts through its edge
        let hop = target == nil ? 60 * Double(CrewLayout.zoom) : max(40, Double(fromTop - top) + 40)
        model?.jumpFacing = destL < fromLeft ? -1 : 1     // the crouch and the leap face the way it jumps
        let work = DispatchWorkItem {
            MainActor.assumeIsolated {
                model?.jumpCrouch = false                 // take-off: crouch → leap
                glide(toLeft: destL, toTop: top, dur: Emote.jumpFlight, hop: hop) {
                    if target != nil {
                        settleHome(left: destL, top: top, screen: screen)
                        climbed = true                    // up high by herself — the pointer can knock her off
                        return
                    }
                    let d = UserDefaults.standard
                    if destL + CrewLayout.screenW > screen.frame.maxX - 28 { d.removeObject(forKey: "crewLeft") }
                    else { d.set(Double(destL - screen.frame.minX), forKey: "crewLeft") }
                    d.set(Double(top), forKey: "crewTop")
                    roam = nil
                    panel?.refit()
                    checkPerch()                          // leapt past the end of its window → it falls
                }
            }
        }
        jumpWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + Emote.jumpTakeoff, execute: work)   // the crouch, then the leap (Emote's timing)
    }
    /// Clingy: walk to the still pointer and settle next to it (no jump — the
    /// rub is in the drawing: swaying body, half-closed eyes).
    static func lifeCling(to pointer: NSPoint) {
        guard !zooming, let screen = CrewScreen.home else { return }
        let vf = screen.visibleFrame
        let z = CrewLayout.zoom
        let left = min(max(pointer.x - CrewLayout.screenW - 6 * z, vf.minX), vf.maxX - CrewLayout.screenW)
        var top = min(max(screen.frame.maxY - pointer.y - CrewLayout.screenH / 2, screen.frame.maxY - vf.maxY),
                      screen.frame.maxY - vf.minY - CrewLayout.screenH)
        // Standing on a floor it WALKS along it toward the pointer (no walking on air); it falls if it walks off.
        if !hanging, let at = charTopLeft(screen) { top = at.y }
        glide(toLeft: left, toTop: top, dur: 0.9)
    }
    /// Something moving her right now (a walk, a leap, a climb, a fall) — the on-her-own scheduler waits for it.
    static var busy: Bool { lifeTimer != nil || fallTimer != nil || climbDir != 0 }
    /// Climbing (owner 13 Oct 2026: "climbing on screen, when i point cursor on it it falls from there"): she walks to
    /// the side of a nearby window and climbs its edge — the side-on runner turned upright against the wall, paws
    /// stepping in small pulls — then hops onto its top, her new spot. Standing on the Dock line with no window to
    /// climb, she climbs the nearer screen edge back up to her pin (zoomies take her off it). False: nothing to climb.
    static let hangTop: CGFloat = 64                   // CrewMove.top's default: the pin under the menu bar
    private static var climbDir: CGFloat = 0           // mid-climb: 1 = the wall is on her right · -1 on her left
    private(set) static var climbed = false            // up high by herself — the pointer can knock her off (slip)
    /// `edge`: only the screen edge — up to the window top that meets it, else to the pin (from the Dock line) — else only
    /// a window side. Returns where she ends up (box top-left) or nil: nothing to climb. `go: false` only asks (CrewOwn's picker).
    static func climb(at: CGPoint, floor fl: CrewGravity.Floor, screen: NSScreen,
                      wins: [(id: Int, rect: CGRect)], edge: Bool = false, go: Bool = true) -> CGPoint? {
        let w = CrewLayout.screenW, z = CrewLayout.zoom, feet = CrewGravity.feet, feetY = at.y + feet
        let gap = 12 * z                                // box centre → wall: the upright body's back against the edge
        var best: (base: CGFloat, land: CGFloat, top: CGFloat, bottom: CGFloat, dir: CGFloat)? = nil
        for win in wins {
            let r = win.rect
            // taller than her, and its side reaches down to her floor — or stops at most 150 pt above it (she hops onto it)
            guard let top = CrewGravity.ledge(r, screen), top < feetY - 60, r.maxY >= feetY - 150, r.maxY - top > 80 else { continue }
            for (edge, dir) in [(r.minX, CGFloat(1)), (r.maxX, CGFloat(-1))] {
                let base = edge - dir * gap - w / 2, land = dir > 0 ? r.minX + 6 : r.maxX - w - 6
                // any side on her floor — a walk of up to 5 s (glide) — not only one within 420 pt: a big window's sides
                // are far apart and she never reached them (owner 13 Oct 2026)
                guard base + w / 2 > fl.x0, base + w / 2 < fl.x1, base >= screen.frame.minX, base + w <= screen.frame.maxX,
                      CrewGravity.floorBelow(x: land + w / 2, fromY: top - 0.5, in: screen, wins: wins).window == win.id
                else { continue }
                if best == nil || abs(base - at.x) < abs(best!.base - at.x) { best = (base, land, top, r.maxY, dir) }
            }
        }
        if !edge, let b = best {
            guard go else { return CGPoint(x: b.land, y: b.top - feet) }
            let up = {
                climbWall(left: b.base, to: b.top - feet + 2 * z, dir: b.dir) {   // head over the top edge…
                    glide(toLeft: b.land, toTop: b.top - feet, dur: 0.4, hop: 10 * Double(z)) {   // …and a hop onto it
                        settleHome(left: b.land, top: b.top - feet, screen: screen)
                        climbed = true
                        model?.chain()
                    }
                }
            }
            glide(toLeft: b.base, toTop: at.y, dur: 1) {
                // a side that stops above her floor: a hop up onto its foot first
                if b.bottom < feetY - 4 { glide(toLeft: b.base, toTop: b.bottom - feet, dur: 0.5, hop: Double(feetY - b.bottom) + 30) { up() } } else { up() }
            }
            return CGPoint(x: b.land, y: b.top - feet)
        }
        guard edge, fl.window == nil else { return nil }   // up the screen edge — only from the Dock line
        let right = at.x + w / 2 > screen.frame.midX, dir: CGFloat = right ? 1 : -1
        let base = right ? screen.frame.maxX - w / 2 - gap : screen.frame.minX - w / 2 + gap
        // A window flush with that edge (maximised, or snapped to the side): no side of it is on screen to climb, so she
        // climbs the screen edge up to its top and steps onto it. None: up to her pin under the menu bar.
        let land = right ? screen.frame.maxX - w - 6 : screen.frame.minX + 6
        let onto = CrewGravity.floorBelow(x: land + w / 2, fromY: 0, in: screen, wins: wins)
        if onto.window != nil, onto.y < feetY - 120 {
            guard go else { return CGPoint(x: land, y: onto.y - feet) }
            glide(toLeft: base, toTop: at.y, dur: 1) {
                climbWall(left: base, to: onto.y - feet + 2 * z, dir: dir) {
                    glide(toLeft: land, toTop: onto.y - feet, dur: 0.4, hop: 10 * Double(z)) {
                        // the window closed or moved while she climbed: the perch check drops her
                        settleHome(left: land, top: onto.y - feet, screen: screen)
                        climbed = true
                        model?.chain()
                    }
                }
            }
            return CGPoint(x: land, y: onto.y - feet)
        }
        guard go else { return CGPoint(x: base, y: hangTop) }
        glide(toLeft: base, toTop: at.y, dur: 1) {
            climbWall(left: base, to: hangTop, dir: dir) {
                settleHome(left: base, top: hangTop, screen: screen)
                if right { UserDefaults.standard.removeObject(forKey: "crewLeft") }   // docked, as at first launch
                setHanging(true)
                climbed = true
                panel?.refit()
                model?.land(0.85)
                model?.chain()
            }
        }
        return CGPoint(x: base, y: hangTop)
    }
    /// The climb itself: up (or down — on her own, head first) the wall at `left` to box top `top1`, one 30 Hz timer only
    /// while she climbs. The pointer on her makes her let go (slip).
    static func climbWall(left: CGFloat, to top1: CGFloat, dir: CGFloat, done: @escaping () -> Void) {
        guard let p = panel, let m = model, let screen = CrewScreen.home, let at = charTopLeft(screen) else { return }
        lifeTimer?.invalidate()
        climbDir = dir
        let down: CGFloat = top1 > at.y ? 1 : -1
        m.runFacing = dir * -down > 0 ? 1 : -1; m.runStride = 22; m.runSpeed = 0; m.runGait = 0; m.runLean = 0
        m.climbRot = -90 * Double(dir)                  // head up (or, facing back, head down) — belly to the wall either way
        var phase = m.runPhase
        let z = CrewLayout.zoom
        var y = at.y, t = 0.0, last = Date(), tick = 0
        // Going up (desktop-pet prior art): a 0.4 s scratch at the foot of the wall first, so the climb reads as meant —
        // and one climb in six fails halfway: she slides back a little, lets go, lands, and shakes it off with zoomies.
        let claw = down < 0 ? 0.4 : 0.0
        let failAt: CGFloat? = down < 0 && Int.random(in: 0..<6) == 0 ? (at.y + top1) / 2 : nil
        var slideT: Double? = nil
        lifeTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { timer in
            MainActor.assumeIsolated {
                let now = Date(), dt = min(now.timeIntervalSince(last), 1.0 / 15); last = now; t += dt
                if let s0 = slideT {                                                 // losing her grip
                    y += CGFloat(110 * dt); roam = (left, y); p.refit()
                    if t - s0 > 0.35 {
                        timer.invalidate(); lifeTimer = nil; climbDir = 0
                        m.runFacing = 0; m.runPhase = 0; m.runStride = 38; m.climbRot = 0
                        drop(vx: -110 * dir, vy: 30) { CrewOwn.lastZoomies = Date(); zoomies() }
                    }
                    return
                }
                if t < claw { phase += dt * 16; m.runPhase = phase; return }                        // paws up on the wall, a quick scratch
                let v = 160 * (0.2 + 0.8 * max(0, sin((t - claw) * 2 * .pi * 1.5)))  // small pulls, ~70 pt/s
                y = down > 0 ? min(top1, y + CGFloat(v * dt)) : max(top1, y - CGFloat(v * dt))
                phase += v * dt / Double(z) / 3
                tick += 1
                if tick % 2 == 1 { m.runPhase = phase }                              // paws on twos, like the walk (CPU)
                if let f = failAt, y <= f { slideT = t }
                roam = (left, y)
                p.refit()
                if p.charRect().insetBy(dx: 6 * z, dy: 6 * z).contains(NSEvent.mouseLocation) { slip(); return }
                if y == top1 {
                    timer.invalidate(); lifeTimer = nil; climbDir = 0
                    m.runFacing = 0; m.runPhase = 0; m.runStride = 38; m.climbRot = 0
                    done()
                }
            }
        }
    }
    /// The pointer on a cat that climbed up by herself (on her own only): she loses her grip and falls — off the wall,
    /// off the pin, or straight off the front of the window she sits on. A cat you put somewhere stays put.
    static func slip() {
        guard CrewPrefs.onHerOwn, climbDir != 0 || climbed, !zooming, !CrewMove.holding, model?.airborne != true,
              !CrewPrefs.quiet, !Fx.reduced, let m = model, let screen = CrewScreen.home, let at = charTopLeft(screen) else { return }
        if climbDir != 0 {                                                   // mid-wall: pushed off it
            let dir = climbDir
            lifeTimer?.invalidate(); lifeTimer = nil; climbDir = 0
            m.runFacing = 0; m.runPhase = 0; m.runStride = 38; m.climbRot = 0
            drop(vx: -150 * dir, vy: -80)
            return
        }
        guard lifeTimer == nil, m.action == .none else { return }
        if hanging { setHanging(false); drop(vy: -60); return }
        let fl = CrewGravity.floorBelow(x: at.x + CrewLayout.screenW / 2, fromY: at.y + CrewGravity.feet - 3,
                                        in: screen, wins: CrewGravity.windows())
        drop(vx: .random(in: -70...70), vy: -220, through: fl.window)        // a startled hop, then down
    }
    /// Something new is waiting (owner 29 Sep 2026: "the cat, wherever it is, bumps and says you have a new
    /// notification… and goes back"): two quick hops on the spot, straight back to where it sat — no trip across the
    /// screen — while the whisper shows. A cat with no headroom (hanging right under the menu bar) squishes twice
    /// instead. Never over a full-screen app or a presentation, never in Quiet mode / Reduce Motion.
    static func bump() {
        guard !zooming, lifeTimer == nil, !CrewMove.holding, model?.airborne != true, !CrewPrefs.quiet, !Fx.reduced,
              !fullScreenInFront(), let screen = CrewScreen.home, let at = charTopLeft(screen) else { return }
        let back = roam                                                  // nil at home; a walked-over cat stays where it is
        let hop = min(12 * Double(CrewLayout.zoom), Double(at.y - (screen.frame.maxY - screen.visibleFrame.maxY)))   // air up to the menu bar
        guard hop >= 6 else {
            model?.land(0.7)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { MainActor.assumeIsolated { model?.land(0.7) } }
            return
        }
        glide(toLeft: at.x, toTop: at.y, dur: 0.3, hop: hop) {
            glide(toLeft: at.x, toTop: at.y, dur: 0.24, hop: hop * 0.55) {
                roam = back
                panel?.refit()
            }
        }
    }
    /// A window of another app covering the whole screen (a full-screen app, a slideshow).
    private static var fullScreenSeen = (at: Date.distantPast, yes: false)
    static func fullScreenInFront() -> Bool {
        if Date().timeIntervalSince(fullScreenSeen.at) < 2 { return fullScreenSeen.yes }   // the window list costs; ask at most every 2 s
        let yes = fullScreenNow()
        fullScreenSeen = (Date(), yes)
        return yes
    }
    private static func fullScreenNow() -> Bool {
        guard let screen = CrewScreen.home,
              let wins = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]]
        else { return false }
        let me = ProcessInfo.processInfo.processIdentifier
        return wins.contains { w in
            guard (w[kCGWindowLayer as String] as? Int) == 0, (w[kCGWindowOwnerPID as String] as? Int32) != me,
                  let b = w[kCGWindowBounds as String] as? [String: CGFloat] else { return false }
            return (b["Width"] ?? 0) >= screen.frame.width && (b["Height"] ?? 0) >= screen.frame.height
        }
    }
    static func lifeGlideHome() {
        guard !zooming, let p = panel, let screen = CrewScreen.home else { return }
        let h = p.homeAnchor(screen, out: model?.isOut ?? false)
        glide(toLeft: h.left, toTop: h.top, dur: 0.9) {
            roam = nil
            panel?.refit()
        }
    }
    // Redraw, not just resize: the views read prefs in body, so tell them
    // something changed.
    static func applyPrefs() { model?.objectWillChange.send(); panel?.refit() }
}

/// The display the cat lives on: the one with the menu bar
/// (NSScreen.screens[0]). NOT NSScreen.main — that is whichever screen has
/// keyboard focus at the moment, so with an external monitor plugged in the
/// cat launched onto the other display (owner's reinstall, 25 Sep 2026:
/// "character not visible").
enum CrewScreen {
    static var home: NSScreen? { NSScreen.screens.first ?? NSScreen.main }
}

@MainActor
final class CrewNSPanel: NSPanel {
    private let model: CrewModel
    private var subs = Set<AnyCancellable>()
    private var host: NSHostingView<CrewRoot>!
    private var outside: Any? = nil
    private var escMon: Any? = nil
    private var refitQueued = false

    // The cat never takes the keyboard: the work moved to the web desk.
    override var canBecomeKey: Bool { false }

    /// AppKit keeps a window below the menu bar, which shoved a cat parked right under it down by the panel's empty top
    /// margin whenever a bubble grew the panel. A resting cat draws nothing in that margin, so let it (only it) slide under the bar.
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        var r = super.constrainFrameRect(frameRect, to: screen)
        if model.action == .none, !model.ringOpen {
            r.origin.y += min(max(frameRect.origin.y - r.origin.y, 0), spec.box.y * CrewLayout.zoom)
        }
        return r
    }

    init(model: CrewModel) {
        self.model = model
        super.init(contentRect: NSRect(x: 0, y: 0, width: CrewLayout.screenW, height: CrewLayout.screenH),
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        level = .floating
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        ignoresMouseEvents = false
        isMovable = false
        host = NSHostingView(rootView: CrewRoot(model: model))
        contentView = host
        // Grow/shrink with the card; refit on pose + list changes — once per run-loop turn, however many properties a
        // mover's tick sets (zoomies set five a frame; each queued its own refit — CPU, 13 Oct 2026).
        model.objectWillChange.sink { [weak self] _ in
            MainActor.assumeIsolated {                  // the model publishes on the main thread
                guard let self, !self.refitQueued else { return }
                self.refitQueued = true
                Task { @MainActor in self.refitQueued = false; self.refit() }
            }
        }.store(in: &subs)
        MailStore.shared.$cases.sink { [weak self] _ in
            Task { @MainActor in self?.refit() }
        }.store(in: &subs)
        MailStore.shared.$watchRunning.sink { [weak self] _ in
            Task { @MainActor in self?.refit() }
        }.store(in: &subs)
        refit()
        watchOutside()
    }

    deinit {
        if let o = outside as? NSObjectProtocol { NSEvent.removeMonitor(o) }
        if let e = escMon as? NSObjectProtocol { NSEvent.removeMonitor(e) }
    }

    // Esc hushes the whisper bubble. A click in another app only hushes the
    // whisper too — there is no pane to close any more.
    private func watchOutside() {
        outside = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            Task { @MainActor in self?.model.dismissWhisper() }
        }
        // A monitor plugged in / unplugged / rearranged: put the cat back on the menu-bar display.
        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.refit() }
        }
        escMon = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
            if e.keyCode == 53 {
                Task { @MainActor in self?.model.dismissWhisper() }
                return nil
            }
            return e
        }
    }

    /// The layout the panel wears right now (size + where the character box sits).
    private var spec = CrewLayout.closed
    /// Screen rect (y up) of the character box inside the current panel (`spec` is 1×, the panel is × zoom).
    func charRect() -> NSRect {
        NSRect(x: frame.minX + spec.box.x * CrewLayout.zoom, y: frame.maxY - spec.box.y * CrewLayout.zoom - CrewLayout.screenH,
               width: CrewLayout.screenW, height: CrewLayout.screenH)
    }
    /// Home of the character box (top-left, y from the screen top): docked at the
    /// right edge (tucked 16 pt × zoom unless it is out) or where the owner parked it,
    /// kept above the Dock.
    func homeAnchor(_ screen: NSScreen, out: Bool) -> (left: CGFloat, top: CGFloat) {
        // Docked = fully visible, 8 pt in from the edge. The old 16 pt tuck (doubled twice at 2×) hid
        // the cat in the corner and slid it out on hover (owner 27 Sep 2026). `out` no longer moves it.
        let hang: CGFloat = -8
        _ = out
        let left = CrewMove.docked ? screen.frame.maxX - CrewLayout.screenW + hang : CrewMove.left(screen)
        var top = CrewMove.top(screen)
        let ground = screen.frame.maxY - screen.visibleFrame.minY           // top-offset of the Dock line
        if top + CrewGravity.feet > ground { top = max(CrewMove.minTop, ground - CrewGravity.feet) }   // feet on the Dock line
        return (left, top)
    }

    // Size = the characters only (the billing pane is gone). Quiet = one 12×14 dot (× zoom).
    // A pose or the ring grows the panel to the drawn bounds + 4 pt (CrewLayout),
    // anchored so the character's FEET stay put, clamped into the visible frame;
    // it shrinks back when the pose ends.
    func refit() {
        guard let screen = CrewScreen.home else { return }
        if CrewPrefs.quiet {
            let q = CrewLayout.quietSize(agents: model.agents.count)
            let w = q.width * CrewLayout.zoom, h = q.height * CrewLayout.zoom
            let x = CrewMove.docked ? screen.frame.maxX - w : CrewMove.left(screen)
            var top = CrewMove.top(screen)
            let lowest = screen.frame.maxY - screen.visibleFrame.minY - 12
            if top + h > lowest { top = max(CrewMove.minTop, lowest - h) }
            spec = CrewLayout.Spec(size: q, box: .zero)
            setFrame(NSRect(x: x, y: screen.frame.maxY - top - h, width: w, height: h), display: true)
            host.frame = NSRect(origin: .zero, size: frame.size)
            return
        }
        // The character's face is drawn by the panel-edge offset inside
        // CrewView (16pt tucked, 0 out; × zoom on screen) — the panel itself always covers the
        // full 46pt (× zoom) so macOS never clips the face, and the 30pt-visible tuck
        // comes from hanging the panel's right 16pt (× zoom) off-screen.
        let anchor = CrewPanel.roam ?? homeAnchor(screen, out: model.isOut)
        let vf = screen.visibleFrame, top = screen.frame.maxY
        // Emote columns go where there is room, so the panel is never pushed back on screen and the
        // cat never moves under the pointer. Decided before the spec reads it.
        let col = (CrewLayout.colGap + CrewLayout.pill.width) * CrewLayout.zoom * 2 + 8
        let roomR = vf.maxX - (anchor.left + CrewLayout.screenW), roomL = anchor.left - vf.minX
        CrewLayout.ringSide = roomR >= col / 2 && roomL >= col / 2 ? .both : (roomL >= roomR ? .left : .right)
        CrewLayout.whisperOnRight = roomL < 280 * CrewLayout.zoom && roomR > roomL   // the bubble needs ~280 pt (1×) at the cat's left
        let s = CrewLayout.spec(for: model)
        let tucked = CrewPanel.roam == nil && CrewMove.docked && !model.isOut   // hangs off the right edge on purpose
        // The empty box below the feet may hang off the bottom of the screen (a cat standing on the floor with the Dock
        // hidden): clamping it back lifted the cat ~26 pt for as long as a pose or bubble was showing. Only the hover
        // ring, whose pills reach below the box, still needs the room.
        let ringed = model.ringOpen && model.action == .none
        let belowFeet: CGFloat = ringed ? 0 : CrewLayout.screenH - CrewGravity.feet
        // A resting cat draws nothing outside its box but a bubble, so the panel needn't stay under the menu bar either
        // (a cat parked right beneath it sagged ~18 pt while it announced). Any pose that draws above the box keeps the clamp.
        let ceiling: CGFloat = model.action == .none && !ringed ? 0 : top - vf.maxY
        let r = CrewLayout.panelRect(charLeft: anchor.left, charTop: anchor.top, spec: s,
                                     // down to the screen's bottom: a cat standing on the Dock line has its box
                                     // (below the feet) over the Dock — clamping there would lift the cat
                                     within: CGRect(x: vf.minX, y: ceiling, width: vf.width,
                                                    height: screen.frame.maxY - ceiling - screen.frame.minY + belowFeet),
                                     tucked: tucked)
        spec = s
        let f = NSRect(x: r.minX, y: top - r.maxY, width: r.width, height: r.height)
        // Same size (a walk, a climb, a fall): just move the window — no re-layout, no forced redraw (CPU).
        if f.size == frame.size { if f.origin != frame.origin { setFrame(f, display: false) }; return }
        setFrame(f, display: true)
        host.frame = NSRect(origin: .zero, size: frame.size)
    }
}

// The panel root: the character column. Quiet: one 7pt dot per agent (amber when it needs you).
// The column is laid out and drawn at its 1× size (CrewLayout), then scaled by CrewLayout.zoom
// from the top-left and framed at the on-screen size — gestures, hover and tooltips ride the
// scale, so nothing inside has to know about it.
struct CrewRoot: View {
    @ObservedObject var model: CrewModel
    var quiet = CrewPrefs.quiet
    var body: some View {
        let size = quiet ? CrewLayout.quietSize(agents: model.agents.count) : CrewLayout.spec(for: model).size
        let z = CrewLayout.zoom
        VStack(spacing: 4 * CrewLayout.zoom) {
            ForEach(model.agents) { a in
                if quiet {
                    quietDot
                        .onTapGesture { CrewPanel.openGame() }
                        .help(quietTip)
                } else {
                    CrewView(model: model, agent: a)
                        // Ring open: the grown panel content (154×170) — the
                        // view sizes itself; closed: the plain character box.
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                }
            }
        }
        // Native size: the views multiply their own numbers by zoom. Scaling a rendered layer
        // (the old scaleEffect) blurred the cat, its bubbles and the ring icons (owner 27 Sep 2026).
        .frame(width: size.width * z, height: size.height * z, alignment: .topLeading)
        .background(Color.clear)
    }

    private var quietDot: some View {
        Circle()
            .fill(MailStore.shared.yourMove.isEmpty
                  ? Color(nsColor: .separatorColor)
                  : Color(red: 0.91, green: 0.69, blue: 0.29))
            .frame(width: 7 * CrewLayout.zoom, height: 7 * CrewLayout.zoom)
            .padding(.trailing, 3 * CrewLayout.zoom).padding(.top, 3 * CrewLayout.zoom)
    }
    private var quietTip: String {
        let n = MailStore.shared.yourMove.count
        return n > 0 ? "Cataholic · \(n)" : "Cataholic · asleep"
    }
}


/// Move the cat anywhere (owner 25 Sep 2026: "can drag anywhere to screen?"). Dropped within 28 pt of
/// the right edge it DOCKS (peeks from the edge, as before); anywhere else it floats. Remembered as
/// crewLeft / crewTop (points from the menu-bar display's left / top); screen-space deltas because the
/// window moves under the pointer. Screen points throughout: the cat is `CrewLayout.screenW × screenH`
/// (× zoom); the 28 pt dock threshold and the 30 pt menu-bar floor are plain screen distances.
@MainActor enum CrewMove {
    static let minTop: CGFloat = 30
    /// How far above the bottom of the visible frame the cat's TOP may sit: its own height + 18 pt
    /// (was the literal 80 = 62 + 18). One number for the read (`top`) and the write (`drag`) so the
    /// stored top never runs past what homeAnchor draws (no dead zone dragging back up).
    private static let bottomRoom = 49 * CrewLayout.zoom   // feet on the Dock line (gravity, 27 Sep 2026)
    private static var grab: NSPoint? = nil
    static var holding: Bool { grab != nil }
    /// The last pointer samples of a drag (time, screen point) — the fling velocity on release.
    private static var trail: [(Date, NSPoint)] = []
    static var docked: Bool { UserDefaults.standard.object(forKey: "crewLeft") == nil }
    static func top(_ screen: NSScreen) -> CGFloat {
        let v = UserDefaults.standard.double(forKey: "crewTop")
        let maxTop = screen.frame.maxY - screen.visibleFrame.minY - bottomRoom
        return v > 0 ? min(max(CGFloat(v), minTop), maxTop) : 64
    }
    /// The character's left edge in screen coordinates (docked = flush with the right edge).
    static func left(_ screen: NSScreen) -> CGFloat {
        guard !docked else { return screen.frame.maxX - CrewLayout.screenW }
        let v = CGFloat(UserDefaults.standard.double(forKey: "crewLeft"))
        return min(max(screen.frame.minX + v, screen.frame.minX), screen.frame.maxX - CrewLayout.screenW)
    }
    static func drag() {
        guard let screen = CrewScreen.home else { return }
        let m = NSEvent.mouseLocation
        if grab == nil {
            // Grabbing the cat (owner 27 Sep 2026: "unable to move the cat clearly"): stop whatever it is
            // doing and drop a roam position (after a jump or a clingy walk the panel stayed pinned there
            // while the drag moved only the stored home), then take the pointer's offset from where the
            // cat is DRAWN, not from the stored home (the docked cat sits 8 pt in from the edge).
            CrewPanel.stopLife()
            CrewSound.purrStart()          // it purrs while you hold it
            let r = CrewPanel.charScreenRect()
            let drawnLeft = r?.minX ?? left(screen), drawnTop = r.map { screen.frame.maxY - $0.maxY } ?? top(screen)
            grab = NSPoint(x: m.x - drawnLeft, y: (screen.frame.maxY - m.y) - drawnTop)
        }
        trail.append((Date(), m)); if trail.count > 6 { trail.removeFirst() }
        let g = grab ?? .zero
        let newLeft = m.x - g.x, newTop = (screen.frame.maxY - m.y) - g.y
        let d = UserDefaults.standard
        if newLeft + CrewLayout.screenW > screen.frame.maxX - 28 { d.removeObject(forKey: "crewLeft") }   // dock
        else { d.set(Double(newLeft - screen.frame.minX), forKey: "crewLeft") }
        // stored clamped, as top() reads it, so dragging back from past an edge has no dead zone
        let maxTop = screen.frame.maxY - screen.visibleFrame.minY - bottomRoom
        d.set(Double(min(max(newTop, minTop), maxTop)), forKey: "crewTop")
        CrewPanel.applyPrefs()
    }
    /// Let go: slow and near the menu bar → it hangs on its pin again; anywhere else it falls (flung fast →
    /// it flies with the pointer's speed) and lands on the floor below (CrewPanel.drop).
    static func end() {
        grab = nil; CrewSound.purrStop()
        var vx: CGFloat = 0, vy: CGFloat = 0
        if let a = trail.first, let b = trail.last, b.0.timeIntervalSince(a.0) > 0.01, Date().timeIntervalSince(b.0) < 0.1 {
            let dt = CGFloat(b.0.timeIntervalSince(a.0))
            vx = (b.1.x - a.1.x) / dt; vy = -(b.1.y - a.1.y) / dt        // y down
            let sp = hypot(vx, vy); if sp > 3200 { vx *= 3200 / sp; vy *= 3200 / sp }
        }
        trail = []
        CrewPanel.currentModel()?.touched()                              // on her own: she stays put a few seconds after you let go
        // A window top right under its feet wins over the pin (owner 13 Oct 2026: put on a window near the top, it
        // "gets attached to the top bar rather than sitting on" the window) — it hangs only with no window to stand on.
        if let screen = CrewScreen.home, let r = CrewPanel.charScreenRect(),
           screen.frame.maxY - r.maxY < CrewGravity.hangZone, hypot(vx, vy) < 400,
           !windowUnderFeet(r, screen) {
            CrewPanel.setHanging(true); CrewPanel.applyPrefs(); CrewPanel.currentModel()?.land(0.85)
            return
        }
        CrewPanel.setHanging(false)
        CrewPanel.drop(vx: vx, vy: vy)
    }
    /// Is there a window top within reach (≤ 60 pt) below the cat's feet? `r` is the character's screen rect.
    private static func windowUnderFeet(_ r: CGRect, _ screen: NSScreen) -> Bool {
        let top = screen.frame.maxY - r.maxY, feetY = top + CrewGravity.feet
        let fl = CrewGravity.floorBelow(x: r.midX, fromY: feetY - 8, in: screen, wins: CrewGravity.windows())
        return fl.window != nil && fl.y - feetY <= 60
    }
}
