import AppKit

// CrewGravity — what the cat can stand on (owner 27 Sep 2026, step 2 of "make it lively": "gravity").
// Floors are the Dock line (the bottom of the visible frame) and the TOP EDGES of the ordinary app windows
// on screen — read from CGWindowListCopyWindowInfo, which gives bounds and layer without any permission
// (only window TITLES need Screen Recording). Coordinates: screen points, y DOWN from the top of the
// menu-bar display — the same "top" space CrewMove / CrewPanel use (window bounds come in that space).
@MainActor
enum CrewGravity {
    /// Near the menu bar the cat HANGS on its pin (the old home); anywhere lower it stands on a floor.
    static let hangZone: CGFloat = 110
    /// The feet sit this far below the character box's top (feet ≈ y 49 of the 62-pt box).
    static var feet: CGFloat { 49 * CrewLayout.zoom }
    static let g: CGFloat = 2600             // pt/s² — snappy, cartoon gravity

    struct Floor { let y: CGFloat; let x0: CGFloat; let x1: CGFloat; let window: Int? }

    /// Every ordinary window on screen, FRONT to BACK (the order CGWindowList returns), in top-space.
    static func windows() -> [(id: Int, rect: CGRect)] {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
                as? [[String: Any]] else { return [] }
        let me = Int(ProcessInfo.processInfo.processIdentifier)
        var out: [(Int, CGRect)] = []
        for w in list {
            guard (w[kCGWindowLayer as String] as? Int) == 0,
                  (w[kCGWindowOwnerPID as String] as? Int) != me,
                  (w[kCGWindowAlpha as String] as? Double ?? 1) > 0.1,
                  let b = w[kCGWindowBounds as String] as? [String: CGFloat],
                  let id = w[kCGWindowNumber as String] as? Int else { continue }
            let r = CGRect(x: b["X"] ?? 0, y: b["Y"] ?? 0, width: b["Width"] ?? 0, height: b["Height"] ?? 0)
            if r.width >= 160, r.height >= 80 { out.append((id, r)) }
        }
        return out
    }

    /// The Dock line: the bottom of the visible frame, across the whole display.
    static func ground(_ screen: NSScreen) -> Floor {
        Floor(y: screen.frame.maxY - screen.visibleFrame.minY, x0: screen.frame.minX, x1: screen.frame.maxX, window: nil)
    }

    /// The highest floor under `x` whose surface is at or below `fromY` (y-down). A window top counts only
    /// where you can SEE it: no window in front covers the edge or the spot the cat would sit in.
    static func floorBelow(x: CGFloat, fromY: CGFloat, in screen: NSScreen,
                           wins: [(id: Int, rect: CGRect)]) -> Floor {
        let menu = screen.frame.maxY - screen.visibleFrame.maxY
        var best = ground(screen)
        for (i, w) in wins.enumerated() {
            let top = w.rect.minY
            guard top >= fromY - 0.5, top < best.y, top > menu + 20,
                  x >= w.rect.minX + 6, x <= w.rect.maxX - 6 else { continue }
            let hidden = wins[..<i].contains { f in
                f.rect.contains(CGPoint(x: x, y: top + 1)) || f.rect.contains(CGPoint(x: x, y: top - 6))
            }
            if !hidden { best = Floor(y: top, x0: w.rect.minX, x1: w.rect.maxX, window: w.id) }
        }
        return best
    }

    /// Is something holding the cat up right now (feet at `feetY`, centre `x`)? ±3 pt.
    static func supported(x: CGFloat, feetY: CGFloat, in screen: NSScreen, wins: [(id: Int, rect: CGRect)]) -> Bool {
        abs(floorBelow(x: x, fromY: feetY - 3, in: screen, wins: wins).y - feetY) <= 3
    }
}
