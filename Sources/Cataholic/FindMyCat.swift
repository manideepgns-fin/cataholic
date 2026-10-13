import AppKit
import SwiftUI

// Find my cat (owner 13 Oct 2026: "double click takes you to find my cat page, where we have a kingdom, and user will
// have three guesses to find where its cat is hidden — find my Waldo?", then "only one feature, simple", then — after
// playing it — "nice actually but seems easy"). Double-click the cat → five levels across three places, harder each time:
//   1. The kingdom by day — ~30 cats in the other seven coats; yours tucked behind something.
//   2. The kingdom by day — plus 4 lookalikes in YOUR coat, each with one detail wrong (collar, bell, glasses, ears,
//      tail tip, a spot). The header shows the real one.
//   3. The harbour — boats, nets, a fish market, a lighthouse; 5–6 lookalikes; yours shows only its ears.
//   4. The snowy village — pale coats blend into the snow; ~40 cats, 6 lookalikes.
//   5. The kingdom at night — dark but for lit windows and lanterns; your pointer is a lantern.
// Then endless: a random place at level-5 difficulty. Three guesses a round: a miss leaves a paw print and says warmer /
// colder (or, on a lookalike, what gave it away); a hit pops your cat up waving and meows → next level; three misses
// spotlight where it was → the same level again, new layout. The level is remembered (`findLevel`).
// Nothing animates while you look: the found-it wave runs a clock for two seconds, and the night lantern redraws only
// when the pointer moves over the scene. Closing the window frees everything.

@MainActor enum FindMyCat {
    private static var window: FindWindow?
    private static let keeper = Closer()
    private static var monitor: Any?
    fileprivate static var camera: FindCamera?

    static func open() {
        if let w = window { NSApp.activate(ignoringOtherApps: true); w.makeKeyAndOrderFront(nil); return }
        let size = NSSize(width: FindScene.size.width, height: FindScene.size.height + FindMyCatView.headerH)
        let w = FindWindow(contentRect: NSRect(origin: .zero, size: size), styleMask: [.titled, .closable, .miniaturizable],
                           backing: .buffered, defer: false)
        w.title = "Find my cat"
        w.isReleasedWhenClosed = false
        w.delegate = keeper
        let cam = FindCamera()
        camera = cam
        w.contentView = NSHostingView(rootView: FindMyCatView(game: FindGame(), lantern: FindLantern(), camera: cam))
        // Pinch and scroll (owner 13 Oct 2026: "you should allow me to zoom"): a local monitor, alive only while the
        // window is open — a trackpad pinch zooms at the pinch point, two-finger scroll pans; a mouse wheel (or ⌥+scroll)
        // zooms at the pointer.
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.magnify, .scrollWheel]) { e in
            MainActor.assumeIsolated { () -> NSEvent? in
                guard let w = FindMyCat.window, e.window === w, let cam = FindMyCat.camera else { return e }
                let p = CGPoint(x: e.locationInWindow.x, y: FindScene.size.height - e.locationInWindow.y)   // the scene viewport sits at the window's bottom
                guard p.y >= 0, p.y <= FindScene.size.height else { return e }
                if e.type == .magnify {
                    cam.zoom(to: cam.z * (1 + e.magnification), at: p)
                } else if e.hasPreciseScrollingDeltas && !e.modifierFlags.contains(.option) {
                    cam.pan(by: CGSize(width: e.scrollingDeltaX, height: e.scrollingDeltaY))
                } else {
                    let d = e.hasPreciseScrollingDeltas ? e.scrollingDeltaY / 100 : e.scrollingDeltaY / 12
                    cam.zoom(to: cam.z * (1 + max(-0.3, min(0.3, d))), at: p)
                }
                return nil
            }
        }
        w.center()
        window = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    /// Closing drops the whole view tree and the game — nothing left running.
    private final class Closer: NSObject, NSWindowDelegate {
        func windowWillClose(_ n: Notification) {
            MainActor.assumeIsolated {
                if let m = FindMyCat.monitor { NSEvent.removeMonitor(m) }
                FindMyCat.monitor = nil
                FindMyCat.camera = nil
                FindMyCat.window?.contentView = nil
                FindMyCat.window = nil
            }
        }
    }
}

/// Esc and ⌘W close it (an accessory app has no menu bar to carry ⌘W); ⌘+ / ⌘− / ⌘0 zoom.
final class FindWindow: NSWindow {
    override func cancelOperation(_ sender: Any?) { performClose(nil) }
    override func performKeyEquivalent(with e: NSEvent) -> Bool {
        let mods = e.modifierFlags.intersection(.deviceIndependentFlagsMask).subtracting(.shift)
        if mods == .command, let k = e.charactersIgnoringModifiers {
            switch k {
            case "w": performClose(nil); return true
            case "=", "+": MainActor.assumeIsolated { FindMyCat.camera?.step(1) }; return true
            case "-", "_": MainActor.assumeIsolated { FindMyCat.camera?.step(-1) }; return true
            case "0": MainActor.assumeIsolated { FindMyCat.camera?.reset(animated: true) }; return true
            default: break
            }
        }
        return super.performKeyEquivalent(with: e)
    }
}

/// The view onto the scene: zoom (1×…3.5×) and pan. The scene lives in its own 1100×640 points; `off` is where the
/// scene's top-left lands on screen. It never leaves the viewport (clamped), and every round starts at 1×.
@MainActor final class FindCamera: ObservableObject {
    @Published private(set) var z: CGFloat = 1
    @Published private(set) var off: CGPoint = .zero
    static let maxZ: CGFloat = 3.5
    var pointer: CGPoint?                        // last pointer spot over the viewport (not published: no redraws)
    fileprivate var dragFrom: CGPoint?           // a click-drag in progress: the offset it started at
    fileprivate var dragging = false

    func toScene(_ p: CGPoint) -> CGPoint { CGPoint(x: (p.x - off.x) / z, y: (p.y - off.y) / z) }
    private func clamped(_ o: CGPoint, _ z: CGFloat) -> CGPoint {
        let W = FindScene.size.width, H = FindScene.size.height
        return CGPoint(x: min(0, max(W - W * z, o.x)), y: min(0, max(H - H * z, o.y)))
    }
    private func set(_ nz: CGFloat, _ o: CGPoint, animated: Bool) {
        let apply = { self.z = nz; self.off = self.clamped(o, nz) }
        if animated && !Fx.reduced { withAnimation(.easeOut(duration: 0.18)) { apply() } } else { apply() }
    }
    /// Zoom so the scene point under `p` (viewport points) stays under it.
    func zoom(to nz: CGFloat, at p: CGPoint, animated: Bool = false) {
        let nz = min(Self.maxZ, max(1, nz)), s = toScene(p)
        set(nz, CGPoint(x: p.x - s.x * nz, y: p.y - s.y * nz), animated: animated)
    }
    /// The +/− buttons and ⌘+ / ⌘−: a step of ×1.5 about the pointer (or the middle).
    func step(_ dir: Int) {
        let p = pointer ?? CGPoint(x: FindScene.size.width / 2, y: FindScene.size.height / 2)
        zoom(to: dir > 0 ? z * 1.5 : z / 1.5, at: p, animated: true)
    }
    func pan(by d: CGSize) { guard z > 1 else { return }; set(z, CGPoint(x: off.x + d.width, y: off.y + d.height), animated: false) }
    func panTo(_ o: CGPoint) { set(z, o, animated: false) }
    func reset(animated: Bool = false) { set(1, .zero, animated: animated) }
}

// MARK: — the round

/// What a lookalike got wrong — one detail, chosen so it reads at the game's size.
enum FindOdd: CaseIterable {
    case collar, bell, glasses, ears, tail, spot
    var hint: String {
        switch self {
        case .collar: return "Not her — look at the collar!"
        case .bell: return "Not her — where's the bell?"
        case .glasses: return "Not her — check the glasses!"
        case .ears: return "Not her — look at the ears!"
        case .tail: return "Not her — look at the tail!"
        case .spot: return "Not her — that spot isn't hers!"
        }
    }
    /// Can you still see it on a cat peeking over something?
    var showsFromAbove: Bool { self == .glasses || self == .ears || self == .spot }
}

/// One cat in the scene. `feet` is where it sits (scene points); `scale` × the 46×62 character box.
struct FindCat: Identifiable {
    let id: Int
    let coat: CatCoat
    let state: CrewSkinState
    let feet: CGPoint
    let scale: CGFloat
    let flip: Bool
    var cover: CGRect? = nil         // the prop it peeks over, if any
    var top: CGFloat? = nil          // …and the highest point that prop draws over it (FindProp.faceTop)
    var odd: FindOdd? = nil          // a lookalike: the one detail that's wrong
    /// The drawn cat, unhidden, in scene points (each pose's own outline inside the 46×62 box).
    var rect: CGRect {
        let b: CGRect
        switch state {
        case .sleeping: b = CGRect(x: 0, y: 28, width: 46, height: 22.5)
        case .nap: b = CGRect(x: 2, y: 24.5, width: 42, height: 25.5)
        case .running: b = CGRect(x: 0, y: 6, width: 46, height: 44)
        default: b = CGRect(x: 0, y: 6, width: 43, height: 45)
        }
        let x0 = flip ? 46 - b.maxX : b.minX
        return CGRect(x: feet.x + (x0 - 23) * scale, y: feet.y + (b.minY - 49) * scale, width: b.width * scale, height: b.height * scale)
    }
    /// The part you can actually see (above whatever it hides behind).
    var visible: CGRect {
        var r = rect
        if let t = top ?? cover?.minY { r.size.height = max(6, t - r.minY) }
        return r
    }
    var box: CGRect { CGRect(x: feet.x - 23 * scale, y: feet.y - 49 * scale, width: 46 * scale, height: 62 * scale) }
}

struct FindGuess: Identifiable {
    let id: Int
    let at: CGPoint
    let hint: String
}

/// How hard a level is: where, how many cats, how many lookalikes, how much of yours shows.
struct FindRound {
    let level: Int                   // 1…5, 6 = endless
    let place: FindPlace
    let night: Bool
    let decoys: Int
    let lookalikes: Int
    let show: ClosedRange<CGFloat>   // how much of your cat's height peeks out
    let mine: CGFloat                // your cat's scale
    static func make(_ level: Int, _ rng: inout FindRandom) -> FindRound {
        switch level {
        // `show` never goes below the face line (FindFair): the face always shows, ~50 % of the body at 1–2, ~35 %+ after.
        case 1: return FindRound(level: 1, place: .kingdom, night: false, decoys: 30, lookalikes: 0, show: 0.55...0.66, mine: 0.6)
        case 2: return FindRound(level: 2, place: .kingdom, night: false, decoys: 32, lookalikes: 4, show: 0.52...0.62, mine: 0.58)
        case 3: return FindRound(level: 3, place: .harbour, night: false, decoys: 38, lookalikes: .random(in: 5...6, using: &rng), show: 0.5...0.58, mine: 0.52)
        case 4: return FindRound(level: 4, place: .snow, night: false, decoys: 44, lookalikes: 6, show: 0.5...0.58, mine: 0.52)
        case 5: return FindRound(level: 5, place: .kingdom, night: true, decoys: 40, lookalikes: 6, show: 0.5...0.6, mine: 0.52)
        default:
            let p = FindPlace.allCases.randomElement(using: &rng)!
            return FindRound(level: 6, place: p, night: p == .kingdom, decoys: 42, lookalikes: 6, show: 0.5...0.58, mine: 0.52)
        }
    }
}

@MainActor final class FindGame: ObservableObject {
    enum Phase { case playing, found, lost }
    @Published private(set) var cats: [FindCat] = []
    @Published private(set) var yours = 0
    @Published private(set) var guesses: [FindGuess] = []
    @Published private(set) var phase = Phase.playing
    @Published private(set) var round: FindRound
    @Published var celebrating = false          // the found-it wave + sparkles run a clock only while this is on
    private(set) var foundAt = Date.distantPast
    private(set) var unfair = false              // no fair spot was found for your cat (the probe asserts this stays false)

    nonisolated static var savedLevel: Int {
        get { min(6, max(1, UserDefaults.standard.integer(forKey: "findLevel"))) }
        set { UserDefaults.standard.set(newValue, forKey: "findLevel") }
    }
    /// The furthest level reached (owner 13 Oct 2026: "can people change levels?") — the picker unlocks up to here.
    /// Older builds only kept `findLevel`, which was then always the furthest.
    nonisolated static var maxLevel: Int {
        get {
            let d = UserDefaults.standard
            return min(6, max(1, d.object(forKey: "findMaxLevel") != nil ? d.integer(forKey: "findMaxLevel") : d.integer(forKey: "findLevel")))
        }
        set { UserDefaults.standard.set(min(6, max(maxLevel, newValue)), forKey: "findMaxLevel") }   // never lowers
    }
    static func levelName(_ l: Int) -> String {
        switch l {
        case 1: return "1 — Kingdom"
        case 2: return "2 — Kingdom lookalikes"
        case 3: return "3 — Harbour"
        case 4: return "4 — Snowy village"
        case 5: return "5 — Kingdom at night"
        default: return "Endless — anywhere"
        }
    }
    /// Start a fresh round on a level picked from the header (only levels already reached).
    func play(level: Int) {
        let level = min(Self.maxLevel, max(1, level))
        Self.savedLevel = level
        var rng = FindRandom(UInt64.random(in: 1...UInt64.max))
        round = FindRound.make(level, &rng)
        deal(&rng)
    }

    private let coat: CatCoat?                  // the self-check deals for every coat without touching your pick

    init(level: Int = FindGame.savedLevel, seed: UInt64 = UInt64.random(in: 1...UInt64.max), coat: CatCoat? = nil) {
        self.coat = coat
        var rng = FindRandom(seed)
        round = FindRound.make(level, &rng)
        deal(&rng)
    }

    var mine: FindCat { cats[yours] }
    var guessesLeft: Int { 3 - guesses.count }
    /// What counts as a hit: the part of your cat you can see, plus a little slack.
    var target: CGRect { mine.visible.insetBy(dx: -9, dy: -9) }
    var center: CGPoint { let r = target; return CGPoint(x: r.midX, y: r.midY) }

    func guess(at p: CGPoint) {
        guard phase == .playing else { return }
        if target.contains(p) {
            phase = .found
            foundAt = Date()
            if !CrewPrefs.quiet { CrewSound.meow() }        // the one sound: a real click on your cat
            if !Fx.reduced {
                celebrating = true
                DispatchQueue.main.asyncAfter(deadline: .now() + FindCelebration.duration) { [weak self] in self?.celebrating = false }
            }
            return
        }
        let hint: String
        if let twin = cats.first(where: { $0.odd != nil && $0.visible.insetBy(dx: -4, dy: -4).contains(p) }), let odd = twin.odd {
            hint = odd.hint
        } else {
            let d = hypot(p.x - center.x, p.y - center.y)
            hint = d < 140 ? "So warm!" : d < 300 ? "Warmer" : d < 520 ? "Colder" : "So cold"
        }
        guesses.append(FindGuess(id: guesses.count, at: p, hint: hint))
        if guesses.count == 3 { phase = .lost }
    }

    /// After a find: the next level (remembered). After three misses: the same level, a new layout.
    func next() {
        let level = phase == .found ? min(6, round.level + 1) : round.level
        Self.savedLevel = level
        Self.maxLevel = level
        var rng = FindRandom(UInt64.random(in: 1...UInt64.max))
        round = FindRound.make(level, &rng)
        deal(&rng)
    }

    /// A lookalike's coat: yours with one detail wrong (the coat id changes, so it never counts as yours).
    static func variant(of c: CatCoat, _ odd: FindOdd) -> CatCoat {
        let dark = ["tux", "cocoa"].contains(c.id)
        let contrast = dark ? Color(red: 0.98, green: 0.96, blue: 0.92) : Color(red: 0.30, green: 0.24, blue: 0.23)
        var v = CatCoat(id: c.id + "-" + "\(odd)", name: c.name, fur: c.fur, stripe: c.stripe, shade: c.shade, cream: c.cream, line: c.line,
                        eye: c.eye, patches: c.patches, glasses: c.glasses, earL: c.earL, earR: c.earR, napPatches: c.napPatches)
        switch odd {
        case .glasses: v.glasses.toggle()
        case .ears: v.earL = contrast; v.earR = contrast
        case .spot: v.patches.append((contrast, CGRect(x: 25.5, y: 11, width: 10, height: 8)))
        default: break
        }
        return v
    }

    /// A new layout for the current round: your cat behind a prop, the lookalikes (mostly out in the open, where their
    /// one wrong detail shows), then decoys in the other seven coats — behind props, on roofs, on the open ground —
    /// never covering yours, never wearing your coat.
    private func deal(_ rng: inout FindRandom) {
        let me = coat ?? CatCoat.current, R = round, place = R.place
        var others = CatCoat.all.filter { $0 != me }
        if place == .snow {                                       // the snowy village is full of pale cats (the hard part)
            let pale = others.filter { ["snow", "smokey", "lilac", "butter", "patches"].contains($0.id) }
            others += pale + pale
        }
        let props = FindScene.props(place)
        var out: [FindCat] = []
        var taken: [CGRect] = []
        func free(_ r: CGRect, _ pad: CGFloat = 4) -> Bool { !taken.contains { $0.insetBy(dx: -pad, dy: -pad).intersects(r) } }
        func behind(_ i: Int, coat: CatCoat, scale: CGFloat, show: CGFloat) -> FindCat? {
            let c = props[i].cover, w = 43 * scale
            guard c.width > w + 6 else { return nil }
            let x = CGFloat.random(in: (c.minX + w / 2 + 3)...(c.maxX - w / 2 - 3), using: &rng)
            let y = props[i].faceTop - show * 45 * scale + 43 * scale     // `show` of its height above what the prop draws
            guard y + 2 * scale <= c.maxY - 1 else { return nil }
            return FindCat(id: out.count, coat: coat, state: .look, feet: CGPoint(x: x, y: y), scale: scale,
                           flip: Bool.random(using: &rng), cover: c, top: props[i].faceTop)
        }
        func ok(_ c: FindCat) -> Bool { free(c.rect) && !FindScene.blocked(place, c.rect, behind: c.cover) && FindScene.inside(c.box) }
        // 1. Yours: tucked behind a prop where it is still FAIR (face and ears show, enough body — FindFair);
        //    if no prop gives that, out in the open.
        let minShow = R.show.lowerBound
        func fair(_ c: FindCat) -> Bool { FindFair.fair(c, place, minShow: minShow) }
        let hideouts = props.indices.filter { props[$0].hides }.shuffled(using: &rng)
        placing: for i in hideouts where props[i].fairHide {
            for _ in 0..<3 {
                if let c = behind(i, coat: me, scale: R.mine, show: .random(in: R.show, using: &rng)), ok(c), fair(c) {
                    out.append(c); break placing
                }
            }
        }
        var t0 = 0
        while out.isEmpty && t0 < 1500 {
            t0 += 1
            let spot = FindScene.openSpot(place, &rng)
            let c = FindCat(id: 0, coat: me, state: .look, feet: spot.at, scale: R.mine * spot.scale, flip: Bool.random(using: &rng))
            if ok(c), fair(c) { out.append(c) }
        }
        unfair = out.isEmpty
        if out.isEmpty, let i = hideouts.first, let c = behind(i, coat: me, scale: R.mine, show: R.show.upperBound) { out.append(c) }   // never happens (the probe checks)
        taken.append(out[0].rect.insetBy(dx: -12, dy: -12))
        yours = 0
        // 2. Lookalikes: about a third peek over props (with a detail you can see from above), the rest in the open.
        var odds = FindOdd.allCases.shuffled(using: &rng)
        var tries = 0, made = 0
        while made < R.lookalikes && tries < 3000 {
            tries += 1
            if odds.isEmpty { odds = FindOdd.allCases.shuffled(using: &rng) }
            let peek = made % 3 == 2
            let pick = peek ? (odds.firstIndex { $0.showsFromAbove } ?? 0) : 0
            let odd = odds[pick]
            let scale = R.mine * .random(in: 0.98...1.12, using: &rng)
            var c: FindCat?
            if peek, odd.showsFromAbove, let i = hideouts.filter({ props[$0].fairHide }).randomElement(using: &rng) {
                c = behind(i, coat: Self.variant(of: me, odd), scale: scale, show: .random(in: 0.55...0.75, using: &rng))
            } else if !peek {
                let spot = FindScene.openSpot(place, &rng)
                c = FindCat(id: out.count, coat: Self.variant(of: me, odd), state: .look, feet: spot.at, scale: scale * spot.scale,
                            flip: Bool.random(using: &rng))
            }
            guard var cat = c, ok(cat), FindFair.fair(cat, place, minShow: 0.5) else { continue }   // lookalikes are findable too
            cat.odd = odd
            out.append(cat); taken.append(cat.rect); odds.remove(at: pick); made += 1
        }
        // 3. Decoys peeking over the other props, then out in the open: sitting, napping, running about.
        let total = out.count + R.decoys
        for i in hideouts.dropFirst() where out.count < made + 1 + 8 {
            for _ in 0..<(props[i].cover.width > 150 ? 2 : 1) {
                guard let c = behind(i, coat: others.randomElement(using: &rng)!, scale: .random(in: 0.5...0.68, using: &rng),
                                     show: .random(in: 0.45...0.8, using: &rng)), ok(c) else { continue }
                out.append(c); taken.append(c.rect)
            }
        }
        tries = 0
        let states: [CrewSkinState] = [.look, .look, .look, .working, .sleeping, .nap, .running, .running]
        while out.count < total && tries < 5000 {
            tries += 1
            let spot = FindScene.openSpot(place, &rng)
            let st = spot.perch ? CrewSkinState.look : states.randomElement(using: &rng)!
            let c = FindCat(id: out.count, coat: others.randomElement(using: &rng)!, state: st, feet: spot.at,
                            scale: .random(in: 0.5...0.68, using: &rng) * spot.scale, flip: Bool.random(using: &rng))
            guard ok(c) else { continue }
            out.append(c); taken.append(c.rect)
        }
        cats = out
        guesses = []
        phase = .playing
        celebrating = false
    }
}

/// SplitMix64 — a seeded layout, so a round can be drawn again exactly (and the probe can render one).
struct FindRandom: RandomNumberGenerator {
    var s: UInt64
    init(_ seed: UInt64) { s = seed }
    mutating func next() -> UInt64 {
        s &+= 0x9E37_79B9_7F4A_7C15
        var z = s
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }
}

/// Where the night lantern is — its own object, so a mouse move redraws only the darkness layer, not the scene.
@MainActor final class FindLantern: ObservableObject {
    @Published var at = CGPoint(x: 550, y: 430)
}

// MARK: — the window

/// Every cat in the game wears a slightly bolder collar and bell, so a lookalike's wrong collar reads at this size.
private let findCollar: CGFloat = 1.6

/// A cat as the game draws it — a lookalike's wrong collar, missing bell or tail tip included.
private func findCharacter(_ cat: FindCat, state: CrewSkinState? = nil, pawRaise: CGFloat = 1, pawWave: Double = 0,
                           pawWrist: Double = 0, eyes: CrewEyes? = nil) -> CrewCharacter {
    let dark = ["tux", "cocoa"].contains(CatCoat.current.id)
    return CrewCharacter(state: state ?? cat.state, badge: "", colors: CrewSkinColors(),
                         runPhase: cat.state == .running ? 0.9 + Double(cat.id % 3) : 0, pin: false, coatOverride: cat.coat,
                         pawRaise: pawRaise, pawWave: pawWave, eyes: eyes, pawWrist: pawWrist,
                         collarTint: cat.odd == .collar ? Color(red: 0.86, green: 0.30, blue: 0.36) : nil,
                         bell: cat.odd != .bell,
                         tailTip: cat.odd == .tail ? (dark ? Color(red: 0.98, green: 0.96, blue: 0.92) : Color(red: 0.30, green: 0.24, blue: 0.23)) : nil,
                         collarBold: findCollar)
}

struct FindMyCatView: View {
    static let headerH: CGFloat = 64
    @ObservedObject var game: FindGame
    let lantern: FindLantern
    let camera: FindCamera

    var body: some View {
        let me = CatCoat.current
        let R = game.round
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                CrewCharacter(state: .look, badge: "", colors: CrewSkinColors(), pin: false, coatOverride: me, collarBold: findCollar)
                    .frame(width: 92, height: 124).scaleEffect(0.42).frame(width: 40, height: 52)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Find \(me.name)").font(.system(size: 17, weight: .bold, design: .rounded))
                    Text(R.lookalikes == 0 ? "\(me.name) is hiding somewhere in the kingdom. Three guesses — click where you think."
                         : R.night ? "Night has fallen — your pointer is a lantern. Lookalikes about: only one is exactly \(me.name)."
                         : "Lookalikes about — only one has exactly this collar, bell, face and tail.")
                        .font(.system(size: 12)).foregroundStyle(.secondary)
                }
                Spacer()
                FindZoomControls(camera: camera)
                FindLevelPicker(game: game, camera: camera)
                HStack(spacing: 6) {
                    ForEach(0..<3, id: \.self) { i in
                        Image(systemName: "pawprint.fill").font(.system(size: 17))
                            .foregroundStyle(i < game.guessesLeft ? me.accent : Color.secondary.opacity(0.25))
                    }
                }
                .help("Guesses left")
            }
            .padding(.horizontal, 18)
            .frame(height: Self.headerH)
            FindViewport(game: game, lantern: lantern, camera: camera)
                .frame(width: FindScene.size.width, height: FindScene.size.height)
        }
        .background(Color(nsColor: .windowBackgroundColor))
    }
}

/// The scene, the cats sorted into it by depth, night and lantern, and the marks / spotlight / celebration on top.
/// The level label is a quiet pop-up: 1 · 2 · 3 · 4 · 5 · Endless, each with its place; levels not reached yet are
/// locked. Picking one starts a fresh round there at 1×.
private struct FindLevelPicker: View {
    @ObservedObject var game: FindGame
    let camera: FindCamera
    var body: some View {
        let cur = game.round.level, max = FindGame.maxLevel
        Menu {
            ForEach(1...6, id: \.self) { l in
                Button {
                    camera.reset(); game.play(level: l)
                } label: {
                    if l == cur { Label(FindGame.levelName(l), systemImage: "checkmark") }
                    else if l > max { Label(FindGame.levelName(l), systemImage: "lock.fill") }
                    else { Text(FindGame.levelName(l)) }
                }
                .disabled(l > max)
            }
        } label: {
            Text(cur > 5 ? "Endless" : "Level \(cur) of 5")
                .font(.system(size: 13, weight: .semibold, design: .rounded))
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .foregroundStyle(.secondary)
        .help("Pick a level you've reached")
    }
}

/// − / 1× / + in the header (the 1× button resets, and shows the zoom while zoomed).
private struct FindZoomControls: View {
    @ObservedObject var camera: FindCamera
    var body: some View {
        HStack(spacing: 2) {
            Button { camera.step(-1) } label: { Image(systemName: "minus.magnifyingglass") }.disabled(camera.z <= 1.001).help("Zoom out (⌘−)")
            Button { camera.reset(animated: true) } label: {
                Text(camera.z < 1.05 ? "1×" : String(format: "%.1f×", camera.z)).font(.system(size: 11, weight: .semibold).monospacedDigit()).frame(minWidth: 30)
            }.help("Actual size (⌘0)")
            Button { camera.step(1) } label: { Image(systemName: "plus.magnifyingglass") }.disabled(camera.z >= FindCamera.maxZ - 0.01).help("Zoom in (⌘+) — or pinch")
        }
        .buttonStyle(.borderless)
        .controlSize(.small)
        .padding(.horizontal, 6).padding(.vertical, 3)
        .background(Capsule().fill(Color.primary.opacity(0.06)))
        .padding(.trailing, 6)
    }
}

/// The window onto the scene: the scene scaled by the camera's zoom and moved by its pan, clipped to 1100×640.
/// A click (moving ≤ 4 pt) is a guess, in scene points; a click-drag pans when zoomed. The banner sits on top, unscaled.
private struct FindViewport: View {
    @ObservedObject var game: FindGame
    let lantern: FindLantern
    @ObservedObject var camera: FindCamera
    var body: some View {
        ZStack(alignment: .topLeading) {
            FindStage(game: game, lantern: lantern, camera: camera)
                .frame(width: FindScene.size.width, height: FindScene.size.height)
                .scaleEffect(camera.z, anchor: .topLeading)          // vector all the way down: the cats are redrawn at this scale
                .offset(x: camera.off.x, y: camera.off.y)
            if game.phase != .playing { banner(CatCoat.current) }
        }
        .frame(width: FindScene.size.width, height: FindScene.size.height, alignment: .topLeading)
        .clipped()
        .contentShape(Rectangle())
        .gesture(DragGesture(minimumDistance: 0)
            .onChanged { v in
                if camera.dragFrom == nil { camera.dragFrom = camera.off; camera.dragging = false }
                if hypot(v.translation.width, v.translation.height) > 4 { camera.dragging = true }
                if camera.dragging, camera.z > 1, let o = camera.dragFrom {
                    camera.panTo(CGPoint(x: o.x + v.translation.width, y: o.y + v.translation.height))
                }
            }
            .onEnded { v in
                if !camera.dragging { game.guess(at: camera.toScene(v.location)) }   // a drag is never a guess
                camera.dragFrom = nil; camera.dragging = false
            })
        .onContinuousHover { phase in                        // the night lantern follows the pointer (no timers)
            guard case .active(let p) = phase else { camera.pointer = nil; return }
            camera.pointer = p
            if game.round.night, game.phase == .playing { lantern.at = camera.toScene(p) }
        }
    }

    private func banner(_ me: CatCoat) -> some View {
        let found = game.phase == .found, level = game.round.level
        let title = !found ? "There she was!" : level >= 5 ? (level == 5 ? "Found her! Endless mode →" : "Found her!") : "Found her! Level \(level + 1) →"
        let button = !found ? "Try again" : level >= 5 ? "Next round" : "Level \(level + 1)"
        return HStack(spacing: 14) {
            Text(title)
                .font(.system(size: 17, weight: .bold, design: .rounded))
                .foregroundStyle(Color(red: 0.24, green: 0.18, blue: 0.16))
            Button(button) { camera.reset(); game.next() }
                .keyboardShortcut(.defaultAction)
                .controlSize(.large)
        }
        .padding(.horizontal, 20).padding(.vertical, 12)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color(red: 1, green: 0.98, blue: 0.94)))
        .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(FindScene.ink, lineWidth: 2))
        .shadow(color: .black.opacity(0.18), radius: 8, y: 3)
        .frame(width: FindScene.size.width, height: FindScene.size.height, alignment: .bottom)
        .padding(.bottom, 22)
        .environment(\.colorScheme, .light)
    }
}

/// The scene in its own coordinates: backdrop, cats sorted into it by depth, night and lantern, marks, spotlight, celebration.
private struct FindStage: View {
    @ObservedObject var game: FindGame
    let lantern: FindLantern
    let camera: FindCamera
    var body: some View {
        let me = CatCoat.current
        let R = game.round, props = FindScene.props(R.place)
        ZStack(alignment: .topLeading) {
            FindBackdrop(place: R.place)
            // Painter's order: props and cats by the line they stand on, so a cat behind a bush is behind it.
            ForEach(FindScene.depthOrder(R.place, game.cats)) { item in
                switch item {
                case .prop(let i): FindPropView(prop: props[i])
                case .cat(let k):
                    if !(k == game.yours && game.phase != .playing) { FindCatView(cat: game.cats[k]) }
                }
            }
            if R.night {
                // While you search, the lantern shows true colours (the details matter); once the round is over the whole
                // kingdom takes the moonlit tint instead.
                if game.phase == .playing { FindLanternLayer(lantern: lantern, camera: camera) }
                else { Rectangle().fill(Color(red: 0.40, green: 0.44, blue: 0.68)).blendMode(.multiply).allowsHitTesting(false) }
                FindNightLights()
            }
            FindMarks(game: game, camera: camera, tint: me.accent)
            if game.phase == .lost {
                let c = FindCelebration.popped(game.mine)
                Spotlight(at: CGPoint(x: c.midX, y: c.midY), size: FindScene.size)
                FindCelebration(cat: game.mine, t: 99, tint: me.accent, wave: false)
            }
            if game.phase == .found {
                if game.celebrating {
                    TimelineView(.animation) { tl in
                        FindCelebration(cat: game.mine, t: tl.date.timeIntervalSince(game.foundAt), tint: me.accent, wave: true)
                    }
                } else {
                    FindCelebration(cat: game.mine, t: 99, tint: me.accent, wave: true)
                }
            }
        }
        .frame(width: FindScene.size.width, height: FindScene.size.height, alignment: .topLeading)
    }
}

/// The paw prints and hints: glued to the art (scene points) but kept at their screen size whatever the zoom.
private struct FindMarks: View {
    @ObservedObject var game: FindGame
    @ObservedObject var camera: FindCamera
    let tint: Color
    var body: some View {
        ZStack(alignment: .topLeading) {
            ForEach(game.guesses) { g in PawMark(guess: g, latest: g.id == game.guesses.count - 1, tint: tint, z: camera.z) }
        }
        .frame(width: FindScene.size.width, height: FindScene.size.height, alignment: .topLeading)
        .allowsHitTesting(false)
    }
}

/// Night: near-dark everywhere except a soft lantern circle (~110 pt) around the pointer.
private struct FindLanternLayer: View {
    @ObservedObject var lantern: FindLantern
    @ObservedObject var camera: FindCamera
    var body: some View {
        let R = 110 / camera.z                               // ~110 pt on screen at any zoom
        Canvas { ctx, sz in
            ctx.fill(Path(CGRect(origin: .zero, size: sz)),
                     with: .radialGradient(Gradient(stops: [.init(color: .clear, location: 0), .init(color: Color(red: 1, green: 0.85, blue: 0.5).opacity(0.06), location: 0.45),
                                                            .init(color: Color(red: 0.03, green: 0.04, blue: 0.11).opacity(0.965), location: 1)]),
                                           center: lantern.at, startRadius: 0, endRadius: R))
            ctx.stroke(Path(ellipseIn: CGRect(x: lantern.at.x - R * 0.98, y: lantern.at.y - R * 0.98, width: R * 1.96, height: R * 1.96)),
                       with: .color(Color(red: 1, green: 0.86, blue: 0.5).opacity(0.18)), lineWidth: 3 / camera.z)
        }
        .frame(width: FindScene.size.width, height: FindScene.size.height)
        .allowsHitTesting(false)
    }
}

private struct FindCatView: View {
    let cat: FindCat
    var body: some View {
        let b = cat.box
        findCharacter(cat)
            .frame(width: 46 * CrewLayout.zoom, height: 62 * CrewLayout.zoom)
            .scaleEffect(x: (cat.flip ? -1 : 1) * cat.scale / CrewLayout.zoom, y: cat.scale / CrewLayout.zoom)
            .frame(width: b.width, height: b.height)
            .offset(x: b.minX, y: b.minY)
            .allowsHitTesting(false)
    }
}

/// A miss: a paw print where you clicked and the hint beside it.
private struct PawMark: View {
    let guess: FindGuess
    let latest: Bool
    let tint: Color
    var z: CGFloat = 1
    var body: some View {
        ZStack(alignment: .topLeading) {
            Canvas { ctx, _ in
                func o(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> Path { Path(ellipseIn: CGRect(x: x, y: y, width: w, height: h)) }
                let parts = [o(5, 10, 14, 12), o(2.5, 4, 5, 6.2), o(8, 0.5, 5, 6.5), o(14, 0.5, 5, 6.5), o(19.5, 4, 5, 6.2)]
                for p in parts { ctx.stroke(p, with: .color(.white), lineWidth: 3) }
                for p in parts { ctx.fill(p, with: .color(tint)) }
            }
            .frame(width: 28, height: 24)
            .rotationEffect(.degrees(Double(guess.id * 17 - 12)))
            .offset(x: -14, y: -12)
            Text(guess.hint)
                .font(.system(size: latest ? 14 : 12, weight: .bold, design: .rounded))
                .foregroundStyle(Color(red: 0.24, green: 0.18, blue: 0.16))
                .padding(.horizontal, 9).padding(.vertical, 4)
                .background(Capsule().fill(Color(red: 1, green: 0.98, blue: 0.94)))
                .overlay(Capsule().strokeBorder(FindScene.ink.opacity(0.8), lineWidth: 1.5))
                .fixedSize()
                .offset(x: guess.at.x > FindScene.size.width - 140 ? -110 : 18, y: -14)
                .opacity(latest ? 1 : 0.75)
        }
        .scaleEffect(1 / z, anchor: .topLeading)             // the print and its hint keep their screen size
        .offset(x: guess.at.x, y: guess.at.y)
        .allowsHitTesting(false)
    }
}

/// Three misses: the kingdom dims, with a soft spotlight on where your cat was.
private struct Spotlight: View {
    let at: CGPoint, size: CGSize
    var body: some View {
        Canvas { ctx, sz in
            var p = Path(CGRect(origin: .zero, size: sz))
            p.addEllipse(in: CGRect(x: at.x - 58, y: at.y - 58, width: 116, height: 116))
            ctx.fill(p, with: .color(.black.opacity(0.55)), style: FillStyle(eoFill: true))
            ctx.stroke(Path(ellipseIn: CGRect(x: at.x - 58, y: at.y - 58, width: 116, height: 116)),
                       with: .color(Color(red: 1, green: 0.95, blue: 0.75).opacity(0.9)), lineWidth: 3)
        }
        .frame(width: size.width, height: size.height)
        .allowsHitTesting(false)
    }
}

/// Found it: your cat pops up out of its hiding place, twice its size, and waves (the Hi emote's own timing) in a burst
/// of sparkles. Three misses: the same pop, sitting still, under the spotlight.
private struct FindCelebration: View {
    static let duration = 2.2
    static let grow: CGFloat = 1.9
    let cat: FindCat
    let t: Double
    let tint: Color
    let wave: Bool
    /// How far it rises: clear of whatever it hid behind.
    static func rise(_ cat: FindCat) -> CGFloat {
        guard let c = cat.cover else { return 6 }
        return max(6, cat.feet.y - c.minY + 4)
    }
    /// Where the popped cat ends up (scene points).
    static func popped(_ cat: FindCat) -> CGRect {
        let r = cat.rect, k = grow, f = cat.feet
        return CGRect(x: f.x + (r.minX - f.x) * k, y: f.y - rise(cat) + (r.minY - f.y) * k, width: r.width * k, height: r.height * k)
    }
    var body: some View {
        let u = min(1, max(0, t / 0.4))
        let pop = 1 - pow(1 - u, 3)                       // ease out…
        let over = u < 1 ? sin(u * .pi) * 0.12 : 0        // …with a little overshoot
        let k = 1 + (Self.grow - 1) * CGFloat(pop + over)
        let rise = Self.rise(cat) * CGFloat(pop)
        let f = Emote.frame(.hi, at: min(t, 1.62))
        let b = cat.box
        let center = CGPoint(x: b.midX, y: cat.feet.y - rise - cat.rect.height * Self.grow / 2)
        ZStack(alignment: .topLeading) {
            if wave && t < 1.5 {
                Canvas { ctx, _ in                         // sparkles fly out and fade
                    let fade = max(0, 1 - t / 1.5)
                    for i in 0..<16 {
                        let a = Double(i) / 16 * 2 * .pi + 0.3, d = 34 + 110 * (1 - pow(1 - min(1, t / 1.0), 2)) * (i % 2 == 0 ? 1 : 0.75)
                        let p = CGPoint(x: center.x + CGFloat(cos(a) * d), y: center.y + CGFloat(sin(a) * d))
                        let r: CGFloat = i % 2 == 0 ? 9 : 6
                        var star = Path()
                        star.move(to: CGPoint(x: p.x, y: p.y - r)); star.addQuadCurve(to: CGPoint(x: p.x + r, y: p.y), control: p)
                        star.addQuadCurve(to: CGPoint(x: p.x, y: p.y + r), control: p); star.addQuadCurve(to: CGPoint(x: p.x - r, y: p.y), control: p)
                        star.addQuadCurve(to: CGPoint(x: p.x, y: p.y - r), control: p)
                        ctx.stroke(star, with: .color(.white.opacity(fade)), lineWidth: 2)
                        ctx.fill(star, with: .color((i % 3 == 0 ? tint : Color(red: 1, green: 0.80, blue: 0.30)).opacity(fade)))
                    }
                }
                .allowsHitTesting(false)
            }
            findCharacter(cat, state: wave ? .hi : .look, pawRaise: max(f.paw, t >= 1.62 ? 1 : 0), pawWave: f.wave,
                          pawWrist: f.wrist, eyes: wave && t > 0.5 && t < 1.3 ? .happy : nil)
                .frame(width: 46 * CrewLayout.zoom, height: 62 * CrewLayout.zoom)
                .scaleEffect(x: (cat.flip && !wave ? -1 : 1) * cat.scale * k / CrewLayout.zoom, y: cat.scale * k / CrewLayout.zoom,
                             anchor: UnitPoint(x: 0.5, y: 49 / 62))
                .frame(width: b.width, height: b.height)
                .offset(x: b.minX, y: b.minY - rise)
                .shadow(color: .black.opacity(0.18), radius: 4, y: 2)
                .allowsHitTesting(false)
        }
        .frame(width: FindScene.size.width, height: FindScene.size.height, alignment: .topLeading)
    }
}


// MARK: — the fairness self-check (`Cataholic --check-find [N]`, run by scripts/release.sh before every release)

/// Deals N layouts per level per coat, silently, and checks every placement of your cat and of every lookalike against
/// FindFair: the face and ears show, enough of the body shows, never behind a snowman. Prints a table; exit 0 only
/// when every placement passes (owner 13 Oct 2026, after a cat hid wholly behind a snowman).
@MainActor enum FindCheck {
    static func run(_ n: Int) -> Int32 {
        let t0 = Date()
        var failed: String?
        print("Find my cat fairness check — \(n) layouts × \(CatCoat.all.count) coats × 5 levels")
        for lv in 1...5 {
            var mine = 0, minePass = 0, open = 0, twins = 0, twinPass = 0, least = 1.0
            for coat in CatCoat.all {
                for k in 0..<n {
                    let seed = UInt64(lv * 1_000_003 + k * 7_919 + 17)
                    let g = FindGame(level: lv, seed: seed, coat: coat)
                    let place = g.round.place, m = g.mine
                    least = min(least, Double(FindFair.shown(m)))
                    let ok = !g.unfair && m.coat == coat && FindFair.fair(m, place, minShow: g.round.show.lowerBound)
                    mine += 1
                    if ok { minePass += 1 } else if failed == nil { failed = "L\(lv) coat \(coat.id) seed \(seed): your cat (shows \(String(format: "%.2f", FindFair.shown(m))))" }
                    if m.cover == nil { open += 1 }
                    for t in g.cats where t.odd != nil {
                        twins += 1
                        if FindFair.fair(t, place, minShow: 0.5) { twinPass += 1 }
                        else if failed == nil { failed = "L\(lv) coat \(coat.id) seed \(seed): lookalike \(t.id) (\(t.odd!))" }
                    }
                }
            }
            print(String(format: "L%d  your cat %d/%d fair (%.1f%%) · in the open %d · lookalikes %d/%d fair · least shown %.0f%%",
                         lv, minePass, mine, Double(minePass) * 100 / Double(max(1, mine)), open, twinPass, twins, least * 100))
        }
        print(String(format: "took %.1f s", Date().timeIntervalSince(t0)))
        if let f = failed { print("FAIL — first failure: \(f)"); return 1 }
        print("PASS — every placement is fair")
        return 0
    }
}
