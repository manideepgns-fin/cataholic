import AppKit

// CrewOwn — what she does next, on her own (owner 13 Oct 2026: "does it crawl all over laptop, actually do something on
// her own … just keep doing something"). CrewModel.ownTurn asks for a turn the moment she settles (chain); this picks
// ONE thing from a weighted pool — only what is possible from where she is — and starts it:
//   travel ~60%  patrol to the far end of her floor and peek over · leap to another window top · climb up a window side ·
//                hop off / climb down her window · chase the pointer a few steps · zoomies · up the screen edge to the pin
//   on the spot  the "Do something" emotes (hi at most every 30 min)
//   rest         a short nap (held as one still frame) · a 2–4 s still sit
// The screen is a 4×3 grid of cells with last-visit times: travel that lands somewhere she hasn't been lately weighs more,
// so over a few minutes she covers the whole screen. Silent throughout (sounds only when you interact, owner 27 Sep 2026).
// She never moves your windows or your pointer.
@MainActor
enum CrewOwn {
    enum Kind { case travel, spot, rest }
    struct Option {
        let name: String
        let kind: Kind
        var w: Double
        let to: CGPoint?                          // where her box (top-left, top-space) ends up — nil: unknown / stays
        let run: () -> Void
    }
    /// Where she is: box top-left in top-space, the floor under her, the windows (front to back), the pointer (top-space)
    /// when it is moving, and whether hi is allowed.
    struct Here {
        var at: CGPoint
        var hanging: Bool
        var floor: CrewGravity.Floor
        var wins: [(id: Int, rect: CGRect)]
        var screen: NSScreen
        var pointer: CGPoint?
        var hiOK: Bool
    }

    private static var visited = [Date](repeating: .distantPast, count: 12)
    private(set) static var recent: [(name: String, kind: Kind)] = []
    private static var sameCell = (cell: -1, n: 0)
    static var lastZoomies = Date.distantPast
    /// Probe seams: a simulated clock, and a line per pick. Production leaves both alone.
    static var now: () -> Date = { Date() }
    static var log: ((String) -> Void)? = nil

    /// The 4×3 cell of the screen her box centre is in.
    static func cell(_ p: CGPoint, _ sf: CGRect) -> Int {
        let cx = p.x + CrewLayout.screenW / 2 - sf.minX, cy = p.y + CrewLayout.screenH / 2
        let c = min(max(Int(cx / sf.width * 4), 0), 3), r = min(max(Int(cy / sf.height * 3), 0), 2)
        return r * 4 + c
    }

    /// A live turn: read where she is, pick, start it.
    static func turn(model m: CrewModel, hiOK: Bool) {
        guard let screen = CrewScreen.home, let at = CrewPanel.charTopLeft(screen) else { m.chain(); return }
        let wins = CrewGravity.windows(), w = CrewLayout.screenW
        let fl = CrewGravity.floorBelow(x: at.x + w / 2, fromY: at.y + CrewGravity.feet - 3, in: screen, wins: wins)
        var pointer: CGPoint? = nil
        if CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: .mouseMoved) < 0.5 {
            let p = NSEvent.mouseLocation
            pointer = CGPoint(x: p.x, y: screen.frame.maxY - p.y)
        }
        let h = Here(at: at, hanging: CrewPanel.hanging, floor: fl, wins: wins, screen: screen, pointer: pointer, hiOK: hiOK)
        guard let o = pick(options(h, model: m), here: h) else { m.chain(); return }
        o.run()
    }

    /// Choose one option: the caps first, then novelty and height balance, then a weighted draw. Records the pick.
    static func pick(_ all: [Option], here h: Here) -> Option? {
        let t = now(), sf = h.screen.frame, c = cell(h.at, sf)
        sameCell = sameCell.cell == c ? (c, sameCell.n + 1) : (c, 1)
        visited[c] = t
        var opts = all
        // never the same thing 3× in a row; never two on-the-spot actions back to back
        if recent.count >= 2, recent[recent.count - 1].name == recent[recent.count - 2].name {
            opts.removeAll { $0.name == recent[recent.count - 1].name }
        }
        if recent.last?.kind == .spot { opts.removeAll { $0.kind == .spot } }
        // 3 picks in one cell → she moves on
        if sameCell.n >= 3, opts.contains(where: { $0.kind == .travel }) { opts.removeAll { $0.kind != .travel } }
        for i in opts.indices where opts[i].kind == .travel {
            if let to = opts[i].to {
                let since = t.timeIntervalSince(visited[cell(to, sf)])
                opts[i].w *= (1 + min(since / 60, 2)) / 2        // 0.5 … 1.5: steers WHERE she goes, keeps the ~60% travel share
            } else {
                opts[i].w *= 1                                   // somewhere along the floor — about average
            }
            opts[i].w *= flow(after: recent.last?.name, opts[i].name)
        }
        let total = opts.reduce(0) { $0 + $1.w }
        guard total > 0 else { return nil }
        var r = Double.random(in: 0..<total)
        var chosen = opts[opts.count - 1]
        for o in opts { if r < o.w { chosen = o; break }; r -= o.w }
        recent.append((chosen.name, chosen.kind)); if recent.count > 8 { recent.removeFirst() }
        if chosen.name == "zoomies" { lastZoomies = t }
        log?(String(format: "%@ cell %d → %@ (%@)", fmtTime(t), c, chosen.name,
                    chosen.to.map { "cell \(cell($0, sf))" } ?? "here"))
        return chosen
    }
    private static func fmtTime(_ t: Date) -> String { String(format: "%7.1fs", t.timeIntervalSince1970.truncatingRemainder(dividingBy: 100000)) }

    /// Natural chains (desktop-pet prior art): zoomies → patrol → leap on; a climb up → a walk along the top → hop down.
    private static func flow(after prev: String?, _ next: String) -> Double {
        switch (prev, next) {
        case ("zoomies", "patrol"), ("patrol", "leap"), ("climb up", "patrol"), ("leap", "patrol"): return 2
        case ("patrol", "hop down"), ("patrol", "climb down"): return 1.5
        case ("edge to pin", "drop"): return 0.4                       // up on the pin she stays a moment
        default: return 1
        }
    }

    /// Everything she could do from here, with its base weight. A weight exists only when the move is possible.
    static func options(_ h: Here, model m: CrewModel) -> [Option] {
        let sf = h.screen.frame, w = CrewLayout.screenW, z = CrewLayout.zoom, feet = CrewGravity.feet
        let fl = h.floor, at = h.at, cx = at.x + w / 2, t = now()
        let onWin = !h.hanging && fl.window != nil, onDock = !h.hanging && fl.window == nil
        let high = h.hanging || (onWin && at.y + feet < sf.height * 0.5)
        let up = onDock ? 1.5 : 1.0, down = high ? 1.5 : 1.0
        var o: [Option] = []

        // — travel —
        if !h.hanging {
            // Patrol to the far end of her floor and peek over it (on the Dock line: a screen corner, then look around).
            let inset: CGFloat = onDock ? 8 : 4
            let lo = max(fl.x0, sf.minX) + inset, hi = min(fl.x1, sf.maxX) - w - inset
            if hi - lo > 60 {
                let end = at.x - lo > hi - at.x ? lo : hi, dir: Double = end == hi ? 1 : -1
                if abs(end - at.x) > 60 {
                    o.append(Option(name: "patrol", kind: .travel, w: 14, to: CGPoint(x: end, y: at.y)) {
                        patrol(to: end, top: at.y, peek: dir, model: m)
                    })
                }
            }
            // Leap to another window's top: within ~450 pt across and 250 pt up or down, its top visible where she lands.
            // Up only from beside the window (never up through it); the arc clears the ledge (CrewPanel.lifeJump).
            var leaps: [CGPoint] = []
            for win in h.wins where win.id != fl.window {
                let r = win.rect
                guard r.width >= w + 12 else { continue }
                let lx = min(max(at.x, r.minX + 6), r.maxX - w - 6), top = r.minY - feet
                guard abs(lx - at.x) <= 450, abs(top - at.y) <= 250, hypot(lx - at.x, top - at.y) > 60,
                      top > at.y - 20 || cx < r.minX || cx > r.maxX,
                      lx >= sf.minX, lx + w <= sf.maxX,
                      CrewGravity.floorBelow(x: lx + w / 2, fromY: r.minY - 0.5, in: h.screen, wins: h.wins).window == win.id
                else { continue }
                leaps.append(CGPoint(x: lx, y: top))
            }
            for p in leaps {
                o.append(Option(name: "leap", kind: .travel, w: 14 * max(up, down) / Double(leaps.count), to: p) {
                    CrewPanel.leapTarget = (p.x, p.y)
                    m.startAction(.jump)
                })
            }
            // Climb up a window side (walks to its foot first).
            if let p = CrewPanel.climb(at: at, floor: fl, screen: h.screen, wins: h.wins, go: false) {
                o.append(Option(name: "climb up", kind: .travel, w: 12 * up, to: p) {
                    _ = CrewPanel.climb(at: at, floor: fl, screen: h.screen, wins: h.wins, go: true)
                })
            }
            // Up the screen edge to her pin — only from the Dock line.
            if onDock, let p = CrewPanel.climb(at: at, floor: fl, screen: h.screen, wins: h.wins, edge: true, go: false) {
                o.append(Option(name: "edge to pin", kind: .travel, w: 3, to: p) {
                    _ = CrewPanel.climb(at: at, floor: fl, screen: h.screen, wins: h.wins, edge: true, go: true)
                })
            }
        }
        if onWin, let id = fl.window, let r = h.wins.first(where: { $0.id == id })?.rect {
            // Hop down off the nearer end, or climb down that side of her window head first.
            let left = cx - r.minX < r.maxX - cx, dir: CGFloat = left ? -1 : 1     // dir: the way she goes off
            let endX = left ? max(r.minX, sf.minX) + 4 : min(r.maxX, sf.maxX) - w - 4
            let offX = endX + dir * 70
            if offX >= sf.minX, offX + w <= sf.maxX {
                let land = CrewGravity.floorBelow(x: offX + w / 2, fromY: r.minY + 1, in: h.screen, wins: h.wins)
                o.append(Option(name: "hop down", kind: .travel, w: 5 * down, to: CGPoint(x: offX, y: land.y - feet)) {
                    CrewPanel.glide(toLeft: endX, toTop: at.y, dur: 1) {
                        CrewPanel.drop(vx: dir * 140, vy: -220, through: id)
                    }
                })
            }
            let gap = 12 * z, base = (left ? r.minX - gap : r.maxX + gap) - w / 2
            if base >= sf.minX, base + w <= sf.maxX {
                let below = CrewGravity.floorBelow(x: base + w / 2, fromY: r.minY + 1, in: h.screen, wins: h.wins)
                let bottom = min(below.y, r.maxY)
                if bottom - r.minY > 120 {
                    o.append(Option(name: "climb down", kind: .travel, w: 5 * down, to: CGPoint(x: base, y: below.y - feet)) {
                        climbDown(endX: endX, top: at.y, base: base, wallDir: -dir, to: bottom - feet - 20 * z, model: m)
                    })
                }
            }
        }
        if h.hanging {
            // Off the pin: let go and land on whatever is below (zoomies are their own pick).
            let land = CrewGravity.floorBelow(x: cx, fromY: at.y + feet, in: h.screen, wins: h.wins)
            o.append(Option(name: "drop", kind: .travel, w: 15, to: CGPoint(x: at.x, y: land.y - feet)) {
                CrewPanel.setHanging(false); CrewPanel.drop(vy: -60)
            })
        }
        // Chase the pointer a few steps — only while it moves, within 400 pt, over her floor.
        if !h.hanging, let p = h.pointer, hypot(p.x - cx, p.y - (at.y + feet / 2)) < 400,
           p.x > fl.x0, p.x < fl.x1, p.y < fl.y, abs(p.x - cx) > w / 2 + 50 {
            o.append(Option(name: "chase", kind: .travel, w: 5, to: CGPoint(x: min(max(p.x - w / 2, fl.x0), fl.x1 - w), y: at.y)) {
                chase(steps: Int.random(in: 2...3), model: m)
            })
        }
        if t.timeIntervalSince(lastZoomies) > 120 {
            o.append(Option(name: "zoomies", kind: .travel, w: 6, to: nil) {
                CrewPanel.zoomies()
            })
        }

        // — on the spot —
        var spot: [(CrewModel.Action, Double)] = [(.look, 6), (.groom, 5), (.stretch, 5), (.yawn, 4), (.spin, 3), (.big, 2), (.jump, 3)]
        if h.hiOK { spot.append((.hi, 1)) }
        for (a, wt) in spot {
            o.append(Option(name: "\(a)", kind: .spot, w: wt, to: nil) { m.startAction(a) })
        }

        // — rest (each under ~10 s) —
        o.append(Option(name: "nap", kind: .rest, w: 6, to: nil) { m.startAction(.nap, dur: .random(in: 6...10)) })
        o.append(Option(name: "sit", kind: .rest, w: 4, to: nil) { m.chain(2...4) })
        return o
    }

    // MARK: — the new moves (reuse the walk, the leap, the climb and the fall)

    /// Walk to the end of her floor, settle there, and peek over it (the look, facing out).
    private static func patrol(to x: CGFloat, top: CGFloat, peek dir: Double, model m: CrewModel) {
        CrewPanel.glide(toLeft: x, toTop: top, dur: 1) {
            guard let screen = CrewScreen.home else { return }
            CrewPanel.settleHome(left: x, top: top, screen: screen)
            m.jumpFacing = dir
            m.startAction(.look)
        }
    }

    /// Walk to the end of her window, step over the edge onto its side, climb down head first, and hop the last bit.
    private static func climbDown(endX: CGFloat, top: CGFloat, base: CGFloat, wallDir: CGFloat, to y1: CGFloat, model m: CrewModel) {
        let z = CrewLayout.zoom
        CrewPanel.glide(toLeft: endX, toTop: top, dur: 1) {
            CrewPanel.glide(toLeft: base, toTop: top + 2 * z, dur: 0.35, hop: 8 * Double(z)) {
                CrewPanel.climbWall(left: base, to: y1, dir: wallDir) {
                    CrewPanel.drop(vx: -wallDir * 70, vy: -120)
                }
            }
        }
    }

    /// A few short walks toward the moving pointer, stopping 40 pt short of it — then a look or a happy squint.
    private static func chase(steps: Int, model m: CrewModel) {
        guard steps > 0, let screen = CrewScreen.home, let at = CrewPanel.charTopLeft(screen)
        else { return }
        let w = CrewLayout.screenW, p = NSEvent.mouseLocation, cx = at.x + w / 2
        let fl = CrewGravity.floorBelow(x: cx, fromY: at.y + CrewGravity.feet - 3, in: screen, wins: CrewGravity.windows())
        let dir: CGFloat = p.x > cx ? 1 : -1
        let want = p.x - dir * (w / 2 + 40) - w / 2
        let x = min(max(min(max(want, at.x - 160), at.x + 160), max(fl.x0, screen.frame.minX) + 4), min(fl.x1, screen.frame.maxX) - w - 4)
        guard abs(x - at.x) > 10 else { finishChase(m); return }
        CrewPanel.glide(toLeft: x, toTop: at.y, dur: 1) {
            CrewPanel.settleHome(left: x, top: at.y, screen: screen)
            if steps > 1 { chase(steps: steps - 1, model: m) } else { finishChase(m) }
        }
    }
    private static func finishChase(_ m: CrewModel) {
        if Bool.random() { m.startAction(.look); return }
        m.land(0.9)
        m.petUntil = Date().addingTimeInterval(1.2)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.25) { MainActor.assumeIsolated { m.objectWillChange.send() } }
        m.chain(1.3...2.2)
    }
}
