import CoreGraphics

// CrewLayout — how big the cat's panel is, and where the 46×62 character box
// sits inside it, for the resting cat, every mid-action pose, the hover ring
// and the text overlays (peek bubble, whisper, "N ✉"). The real panel (CrewNSPanel.refit) and the snapshot stills both call
// this, so a still shows exactly what the screen shows.
// Points, origin top-left, y down. The character box is the closed panel
// (46×62); its feet stand at y ≈ 49.
// ZOOM (owner, 27 Sep 2026: "double the cat size"): everything in this file, and
// everything a view draws, is in 1× points. CrewRoot scales the finished drawing
// by `zoom`; every place that turns layout into a window frame or a screen
// position (panelRect, the panel, the movers) multiplies by it.
@MainActor
enum CrewLayout {
    static let zoom: CGFloat = 2
    static let charW: CGFloat = 46, charH: CGFloat = 62
    /// The character box on screen (charW × charH at `zoom`).
    static let screenW = charW * zoom, screenH = charH * zoom
    static let margin: CGFloat = 4          // a pose's panel = its drawn bounds + 4 pt each side

    struct Bounds {
        var x0: CGFloat, y0: CGFloat, x1: CGFloat, y1: CGFloat
        func contains(_ o: Bounds, slack: CGFloat = 0.01) -> Bool {
            o.x0 >= x0 - slack && o.y0 >= y0 - slack && o.x1 <= x1 + slack && o.y1 <= y1 + slack
        }
        func union(_ o: Bounds) -> Bounds {
            Bounds(x0: min(x0, o.x0), y0: min(y0, o.y0), x1: max(x1, o.x1), y1: max(y1, o.y1))
        }
        /// Flipped about the body's centre line (x = 23).
        var mirrored: Bounds { Bounds(x0: 46 - x1, y0: y0, x1: 46 - x0, y1: y1) }
        /// The bounds rocked ±`deg` about the pin (23, 2), unioned with the original.
        func rocked(_ deg: CGFloat) -> Bounds {
            var r = self
            for sign: CGFloat in [-1, 1] {
                let t = sign * deg * .pi / 180, c = CGFloat(cos(t)), sn = CGFloat(sin(t))
                for (x, y) in [(x0, y0), (x1, y0), (x0, y1), (x1, y1)] {
                    let px = 23 + (x - 23) * c - (y - 2) * sn, py = 2 + (x - 23) * sn + (y - 2) * c
                    r = r.union(Bounds(x0: px, y0: py, x1: px, y1: py))
                }
            }
            return r
        }
    }
    struct Spec: Equatable {
        var size: CGSize                    // the panel
        var box: CGPoint                    // where the character box's top-left sits inside it
    }
    static let closed = Spec(size: CGSize(width: charW, height: charH), box: .zero)
    /// Quiet mode's panel, 1× points: one 12×14 dot per agent, 22 pt apart.
    static func quietSize(agents n: Int) -> CGSize {
        CGSize(width: 12, height: 14 + CGFloat(max(n - 1, 0)) * 22)
    }

    /// The text overlays that hang outside the character box: the peek bubble,
    /// the whisper and the "N ✉" caption. Fixed frames in character-box points —
    /// CrewView draws each at exactly this rect and the panel grows to hold it,
    /// so their bounds never depend on font metrics. The caption is laid out
    /// for an un-tucked cat; a tucked cat's extra 16 pt of overhang is already
    /// off-screen.
    enum Overlay: CaseIterable {
        case whisper, caption
        var rect: CGRect {
            switch self {
            case .whisper: return CGRect(x: whisperOnRight ? 46 : -276, y: 12, width: 274, height: 32)   // left: ends short of the sign the cat holds (x 0.5–13.5)
            case .caption: return CGRect(x: 0, y: 62, width: 40, height: 14)
            }
        }
        var bounds: Bounds { Bounds(x0: rect.minX, y0: rect.minY, x1: rect.maxX, y1: rect.maxY) }
    }

    /// The whisper hangs to the cat's LEFT (it lives at the right edge) — unless the cat is parked so far left that
    /// the bubble would not fit, when it hangs to the right. Otherwise the panel is pushed back on screen and the
    /// cat slides sideways when it announces something. Set by the panel (main thread) from where the cat sits.
    nonisolated(unsafe) static var whisperOnRight = false

    /// Snapshot measuring only: a roomy stage that clips nothing.
    static var measureOverride: Spec? = nil

    /// What each pose draws, static and unrotated, in character-box points —
    /// copied from the snapshot run's `bounds` lines (measured on transparent
    /// pixels: cat, bubble, grown cat and all). The run fails when a drawing
    /// outgrows its row, so this table cannot rot.
    private static func measured(_ a: CrewModel.Action) -> Bounds {
        switch a {
        case .hi: return Bounds(x0: -27.0, y0: -21.0, x1: 41.5, y1: 50.5)      // bubble + tail, ears, wave paw
        case .big: return Bounds(x0: -11.5, y0: -35.0, x1: 58.0, y1: 52.0)      // 1.95× (spring peak) about the feet
        case .stretch: return Bounds(x0: 2.0, y0: 8.5, x1: 41.5, y1: 50.0)     // side-on bow
        case .jump: return Bounds(x0: -0.5, y0: 21.5, x1: 42.0, y1: 50.5)      // side-on leap
        case .clingy: return Bounds(x0: 10.5, y0: 0.0, x1: 43.0, y1: 50.0)     // leaning +8°
        case .look: return Bounds(x0: 5.0, y0: 0.0, x1: 41.5, y1: 50.5)        // head turned +3
        case .nap: return Bounds(x0: 0.0, y0: 0.0, x1: 46.5, y1: 50.5)          // sitting on the pin, or melted flat when standing
        case .yawn, .groom, .spin, .none: return Bounds(x0: 0.0, y0: 0.0, x1: 41.5, y1: 50.5)  // spin covers the needs sign
        }
    }
    /// The 0.2 s take-off crouch before the leap (front-on, off the pin) — copied from the run's `bounds jump-crouch`
    /// line (x 6.0..40.0, y 10.0..49.5); the run fails when the drawing outgrows it.
    private static let crouch = Bounds(x0: 5.0, y0: 9.0, x1: 41.0, y1: 50.0)
    /// The bounds the panel must hold: the static drawing plus its motion —
    /// the look turns ±3 pt, the lean is ±8°, the spin flips about the body,
    /// the jump lifts 6 pt, the whole cat rocks ±2.5° about the pin (working
    /// sway) — ±5.5° while clingy rubs.
    static func drawn(_ a: CrewModel.Action) -> Bounds {
        var b = measured(a)
        switch a {
        case .look: b.x0 -= 6
        case .clingy, .spin: b = b.union(b.mirrored)
        case .jump:
            b.y0 -= 6                                       // the leap lifts; the crouch is on the ground
            b = b.union(crouch)
            b = b.union(b.mirrored)                         // …and it leaps (crouches) left as well as right
        default: break
        }
        return b.rocked(a == .clingy ? 5.5 : 2.5)
    }

    /// The resting cat as the ring sees it — needs sign at the left to the tail
    /// at the right, ears to feet; the pin steps aside while the ring shows.
    static let idle = Bounds(x0: 0, y0: 6, x1: 43, y1: 51)   // y1 51: the sticker outline (27 Sep 2026)
    /// The resting cat WITH its pin string (drawn from y = 0): what an overlay's panel
    /// wraps, so the string gets the same 4 pt margin as any pose (x1 42 + 4 = the box).
    static let resting = Bounds(x0: 0, y0: 0, x1: 42, y1: 51)
    static let ringCentre = CGPoint(x: 23, y: 28)
    // The emotes (owner 27 Sep 2026: "some of the emotes I can't even understand"): labelled pills — icon + word —
    // in two short columns hugging the cat. A ring of words could not fit without overlapping. Near a screen
    // edge both columns go to the open side, so the panel never has to be pushed back on screen — the cat
    // never moves when you hover ("sometimes it slides back").
    enum RingSide { case both, left, right }
    nonisolated(unsafe) static var ringSide: RingSide = .both   // set by the panel (main thread) from where the cat sits
    static let pill = CGSize(width: 56, height: 13)  // 1× (on screen × zoom: 112 × 26)
    static let pillGap: CGFloat = 3, colGap: CGFloat = 4

    /// Each action's pill, in character-box points (1×), in `ringActions` order.
    static func pillRects(_ side: RingSide = ringSide) -> [CGRect] {
        let n = CrewModel.ringActions.count, first = (n + 1) / 2          // 5 then 4
        let inL = idle.x0 - colGap - pill.width, inR = idle.x1 + colGap
        let cols: [CGFloat]
        switch side {
        case .both: cols = [inR, inL]
        case .left: cols = [inL, inL - colGap - pill.width]
        case .right: cols = [inR, inR + pill.width + colGap]
        }
        var out: [CGRect] = []
        for (c, x) in cols.enumerated() {
            let count = c == 0 ? first : n - first
            let h = CGFloat(count) * pill.height + CGFloat(count - 1) * pillGap
            let y0 = ringCentre.y - h / 2
            for k in 0..<count {
                out.append(CGRect(x: x, y: y0 + CGFloat(k) * (pill.height + pillGap), width: pill.width, height: pill.height))
            }
        }
        return out
    }
    /// Smallest gap between any pill and the cat (must be ≥ 4) — the snapshot guard reads it.
    static func pillGapToCat(_ side: RingSide = ringSide) -> CGFloat {
        pillRects(side).map { r in
            let dx = max(idle.x0 - r.maxX, 0, r.minX - idle.x1), dy = max(idle.y0 - r.maxY, 0, r.minY - idle.y1)
            return hypot(dx, dy)
        }.min() ?? 0
    }
    /// The pills around the cat, in character-box points.
    private static var ringBounds: Bounds {
        pillRects().reduce(idle) { b, r in b.union(Bounds(x0: r.minX, y0: r.minY, x1: r.maxX, y1: r.maxY)) }
    }

    /// The panel for whatever is drawn: the ring or the pose (+ 4 pt), plus every
    /// overlay showing (+ 4 pt) — always at least the 46×62 character box, in
    /// whole points (no blurry half-pixel panel).
    static func spec(action a: CrewModel.Action, ringed: Bool, overlays: [Overlay] = []) -> Spec {
        if let m = measureOverride { return m }
        var content: Bounds? = ringed ? ringBounds : (a == .none ? (overlays.isEmpty ? nil : resting) : drawn(a))
        for o in overlays { content = content?.union(o.bounds) ?? o.bounds }
        guard let b = content else { return closed }
        let x0 = min(b.x0 - margin, 0).rounded(.down), y0 = min(b.y0 - margin, 0).rounded(.down)
        let x1 = max(b.x1 + margin, charW).rounded(.up), y1 = max(b.y1 + margin, charH).rounded(.up)
        return Spec(size: CGSize(width: x1 - x0, height: y1 - y0), box: CGPoint(x: -x0, y: -y0))
    }
    /// The panel the model wants right now — the view, the real panel and the stills all ask this.
    static func spec(for m: CrewModel) -> Spec {
        spec(action: m.action, ringed: m.ringOpen && m.action == .none, overlays: m.overlays)
    }

    /// The panel rect, in SCREEN points, for a character box whose top-left is
    /// (charLeft, charTop) on screen: `s` is the 1× layout, scaled by `zoom` here.
    /// A grown panel is shifted back inside `screen` (the visible frame, same
    /// y-down space); the resting panel is never clamped (the docked cat hangs
    /// 16 pt × zoom off the edge on purpose). A `tucked` cat keeps that overhang while a
    /// bubble or caption grows its panel — it is not pulled back in on the right.
    static func panelRect(charLeft: CGFloat, charTop: CGFloat, spec s: Spec, within screen: CGRect?,
                          tucked: Bool = false) -> CGRect {
        var r = CGRect(x: charLeft - s.box.x * zoom, y: charTop - s.box.y * zoom,
                       width: s.size.width * zoom, height: s.size.height * zoom)
        guard let v = screen, s != closed else { return r }
        r.origin.x = min(max(r.minX, v.minX), tucked ? r.minX : max(v.minX, v.maxX - r.width))
        r.origin.y = min(max(r.minY, v.minY), max(v.minY, v.maxY - r.height))
        return r
    }
}
