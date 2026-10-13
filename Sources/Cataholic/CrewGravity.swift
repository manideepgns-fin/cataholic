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

    /// Where she stands on a window: its top edge — or, when that edge is too high for her head to clear the menu bar
    /// (a maximised window's top IS the menu bar's bottom), the line where her box top meets the menu bar, so she walks
    /// along the top of the window just under the bar (owner 13 Oct 2026: on the real Mac every window was maximised,
    /// every top was rejected, and she only ever paced the Dock line). nil: the window is too short to stand on.
    static func ledge(_ r: CGRect, _ screen: NSScreen) -> CGFloat? {
        let y = max(r.minY, max(screen.frame.maxY - screen.visibleFrame.maxY, CrewMove.minTop) + feet)
        return y < r.maxY - 40 ? y : nil
    }

    /// The highest floor under `x` whose surface is at or below `fromY` (y-down). A window top counts only
    /// where you can SEE it: no window in front covers the edge or the spot the cat would sit in.
    static func floorBelow(x: CGFloat, fromY: CGFloat, in screen: NSScreen,
                           wins: [(id: Int, rect: CGRect)]) -> Floor {
        var best = ground(screen)
        for (i, w) in wins.enumerated() {
            guard let top = ledge(w.rect, screen), top >= fromY - 0.5, top < best.y,
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
