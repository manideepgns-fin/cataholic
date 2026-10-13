import AppKit
import SwiftUI

// Find my cat's three places (owner 13 Oct 2026: a kingdom, a harbour, a snowy village; the kingdom again at night).
// Each is drawn in the cat's own sticker style — soft fills, one warm ink outline — as a fixed backdrop plus props
// (bushes, crates, boats, snowmen…) that sort in depth with the cats, so a cat behind a crate is really behind it.

// MARK: — places, props, spots

enum FindPlace: CaseIterable { case kingdom, harbour, snow }

/// One piece of scenery a cat can stand behind: drawn in `rect`, solid over `cover`, standing on cover's bottom edge.
struct FindProp {
    enum Kind { case parapet, bush, crate, barrels, hay, counter, well, wall, tree, cart,
                boat, net, pots, fishCrate, snowman, sled, drift, woodpile, pine }
    let kind: Kind
    let rect: CGRect
    let cover: CGRect
    var tint = 0
    var hides: Bool { kind != .tree && kind != .pine }
    var baseline: CGFloat { cover.maxY }
    /// The highest point the prop actually draws over a cat behind it — goods on a counter, a boat's gunwale, a bush's
    /// round top — so "the face shows" is judged on what you see, not on the solid box.
    var faceTop: CGFloat {
        switch kind {
        case .boat: return cover.minY - 7
        case .well: return cover.minY - 4
        case .cart: return cover.minY - 2
        case .counter: return cover.minY - 12
        default: return min(rect.minY, cover.minY)
        }
    }
    /// May YOUR cat (or a lookalike) hide here? Not behind a snowman: its head and body sit right over a cat's face.
    var fairHide: Bool { hides && kind != .snowman }
}

@MainActor enum FindScene {
    static let size = CGSize(width: 1100, height: 640)
    nonisolated static let ink = Color(red: 0.33, green: 0.25, blue: 0.23)

    static func props(_ place: FindPlace) -> [FindProp] {
        switch place { case .kingdom: return kingdomProps; case .harbour: return harbourProps; case .snow: return snowProps }
    }
    private static func P(_ k: FindProp.Kind, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat, top: CGFloat = 0, _ tint: Int = 0) -> FindProp {
        FindProp(kind: k, rect: CGRect(x: x, y: y, width: w, height: h), cover: CGRect(x: x, y: y + top, width: w, height: h - top), tint: tint)
    }
    static let kingdomProps: [FindProp] = [
        P(.parapet, 428, 166, 244, 36, top: 18),
        FindProp(kind: .tree, rect: CGRect(x: 318, y: 318, width: 92, height: 122), cover: CGRect(x: 352, y: 420, width: 22, height: 20)),
        P(.bush, 690, 368, 86, 42, top: 4),
        P(.counter, 766, 426, 94, 44, top: 12, 0), P(.counter, 878, 426, 94, 44, top: 12, 1), P(.counter, 990, 426, 94, 44, top: 12, 2),
        P(.hay, 380, 452, 72, 40),
        P(.wall, 26, 560, 168, 30, top: 4),
        P(.crate, 214, 568, 54, 50),
        P(.bush, 300, 586, 112, 46, top: 4),
        FindProp(kind: .well, rect: CGRect(x: 650, y: 486, width: 74, height: 92), cover: CGRect(x: 652, y: 538, width: 70, height: 40)),
        P(.barrels, 790, 540, 74, 46),
        P(.crate, 878, 556, 58, 52),
        P(.bush, 960, 572, 116, 48, top: 4),
        FindProp(kind: .cart, rect: CGRect(x: 470, y: 548, width: 96, height: 64), cover: CGRect(x: 470, y: 560, width: 96, height: 40)),
        P(.bush, 20, 452, 74, 38, top: 4),
    ]
    static let harbourProps: [FindProp] = [
        FindProp(kind: .boat, rect: CGRect(x: 430, y: 296, width: 132, height: 118), cover: CGRect(x: 430, y: 368, width: 132, height: 44), tint: 0),
        FindProp(kind: .boat, rect: CGRect(x: 606, y: 392, width: 146, height: 54), cover: CGRect(x: 606, y: 398, width: 146, height: 46), tint: 1),
        FindProp(kind: .boat, rect: CGRect(x: 830, y: 300, width: 108, height: 98), cover: CGRect(x: 830, y: 360, width: 108, height: 38), tint: 2),
        P(.counter, 562, 494, 100, 46, top: 12, 3), P(.counter, 694, 494, 100, 46, top: 12, 4),
        P(.barrels, 40, 470, 74, 46),
        P(.fishCrate, 170, 556, 70, 46),
        P(.net, 268, 590, 128, 42),
        P(.pots, 836, 570, 96, 46),
        P(.crate, 980, 520, 58, 52),
        P(.crate, 448, 572, 54, 48),
        P(.fishCrate, 940, 600, 76, 36),
    ]
    static let snowProps: [FindProp] = [
        FindProp(kind: .pine, rect: CGRect(x: 312, y: 300, width: 76, height: 128), cover: CGRect(x: 342, y: 410, width: 16, height: 18)),
        FindProp(kind: .pine, rect: CGRect(x: 1006, y: 290, width: 80, height: 134), cover: CGRect(x: 1038, y: 406, width: 16, height: 18)),
        FindProp(kind: .pine, rect: CGRect(x: 690, y: 296, width: 64, height: 110), cover: CGRect(x: 714, y: 390, width: 14, height: 16)),
        FindProp(kind: .snowman, rect: CGRect(x: 150, y: 510, width: 58, height: 92), cover: CGRect(x: 150, y: 552, width: 58, height: 50)),
        FindProp(kind: .snowman, rect: CGRect(x: 760, y: 408, width: 52, height: 82), cover: CGRect(x: 760, y: 446, width: 52, height: 44)),
        FindProp(kind: .snowman, rect: CGRect(x: 970, y: 540, width: 60, height: 94), cover: CGRect(x: 970, y: 584, width: 60, height: 50)),
        P(.sled, 380, 592, 100, 36), P(.sled, 862, 476, 94, 34, 1),
        P(.drift, 28, 596, 126, 38), P(.drift, 600, 594, 136, 40), P(.drift, 1000, 438, 92, 34), P(.drift, 470, 410, 110, 34),
        P(.woodpile, 232, 470, 94, 44),
    ]

    /// Buildings drawn in the backdrop: wall rect, roof height, wall / roof colours. Cats sit on their ridges, never in their walls.
    struct House { let wall: CGRect; let roof: CGFloat; let wallC: Color; let roofC: Color }
    static let houses: [House] = [
        House(wall: CGRect(x: 30, y: 392, width: 120, height: 60), roof: 44, wallC: Color(red: 0.99, green: 0.93, blue: 0.80), roofC: Color(red: 0.84, green: 0.45, blue: 0.36)),
        House(wall: CGRect(x: 176, y: 402, width: 110, height: 56), roof: 40, wallC: Color(red: 0.98, green: 0.86, blue: 0.84), roofC: Color(red: 0.46, green: 0.58, blue: 0.78)),
        House(wall: CGRect(x: 50, y: 484, width: 112, height: 62), roof: 42, wallC: Color(red: 0.86, green: 0.92, blue: 0.96), roofC: Color(red: 0.50, green: 0.68, blue: 0.46)),
        House(wall: CGRect(x: 196, y: 496, width: 120, height: 60), roof: 44, wallC: Color(red: 0.99, green: 0.93, blue: 0.80), roofC: Color(red: 0.86, green: 0.62, blue: 0.32)),
    ]
    static let shacks: [House] = [
        House(wall: CGRect(x: 26, y: 372, width: 176, height: 74), roof: 40, wallC: Color(red: 0.62, green: 0.76, blue: 0.86), roofC: Color(red: 0.84, green: 0.45, blue: 0.36)),
    ]
    static let cabins: [House] = [
        House(wall: CGRect(x: 40, y: 384, width: 132, height: 64), roof: 42, wallC: Color(red: 0.66, green: 0.46, blue: 0.33), roofC: .white),
        House(wall: CGRect(x: 196, y: 364, width: 112, height: 58), roof: 38, wallC: Color(red: 0.74, green: 0.52, blue: 0.36), roofC: .white),
        House(wall: CGRect(x: 812, y: 360, width: 122, height: 62), roof: 40, wallC: Color(red: 0.70, green: 0.48, blue: 0.34), roofC: .white),
        House(wall: CGRect(x: 52, y: 470, width: 118, height: 62), roof: 40, wallC: Color(red: 0.74, green: 0.52, blue: 0.36), roofC: .white),
    ]
    static func buildings(_ p: FindPlace) -> [House] {
        switch p { case .kingdom: return houses; case .harbour: return shacks; case .snow: return cabins }
    }

    /// Places in the open: rooftops, and the open ground of each place (the bridge, the pier, the frozen pond…).
    static func openSpot(_ place: FindPlace, _ rng: inout FindRandom) -> (at: CGPoint, scale: CGFloat, perch: Bool) {
        let r = Double.random(in: 0..<1, using: &rng)
        if r < 0.14 {                                                     // a rooftop ridge
            let h = buildings(place).randomElement(using: &rng)!
            let ridge = h.wall.width * 0.36
            return (CGPoint(x: h.wall.midX + .random(in: -ridge / 2...ridge / 2, using: &rng), y: h.wall.minY - h.roof + 3), 0.92, true)
        }
        switch place {
        case .kingdom:
            if r < 0.2 { return (CGPoint(x: .random(in: 528...572, using: &rng), y: .random(in: 322...344, using: &rng)), 0.95, false) }   // the bridge
            return (CGPoint(x: .random(in: 24...1076, using: &rng), y: .random(in: 372...632, using: &rng)), 1, false)
        case .harbour:
            if r < 0.26 { return (CGPoint(x: .random(in: 318...384, using: &rng), y: .random(in: 330...452, using: &rng)), 0.92, false) }  // the pier
            if r < 0.34 { return (CGPoint(x: .random(in: 220...250, using: &rng), y: .random(in: 420...452, using: &rng)), 0.95, false) }  // the quay
            return (CGPoint(x: .random(in: 24...1076, using: &rng), y: .random(in: 490...632, using: &rng)), 1, false)                    // the dock
        case .snow:
            return (CGPoint(x: .random(in: 24...1076, using: &rng), y: .random(in: 384...632, using: &rng)), 1, false)
        }
    }

    /// Would this cat land somewhere silly — in the water, inside a wall, or half-buried in a prop it isn't behind?
    static func blocked(_ place: FindPlace, _ r: CGRect, behind: CGRect?) -> Bool {
        for p in props(place) where p.cover != behind {
            if !p.hides && r.maxY > p.baseline + 2 { continue }                 // sitting in front of a tree is fine
            if p.rect.insetBy(dx: -3, dy: -3).intersects(r) { return true }
        }
        for h in buildings(place) where h.wall.insetBy(dx: 4, dy: 4).intersects(r) && r.maxY < h.wall.maxY + 3 { return true }  // in front of a wall is fine
        switch place {
        case .kingdom:
            let moat = CGRect(x: 0, y: 306, width: 1100, height: 46)
            if moat.intersects(r) && !(r.minX > 516 && r.maxX < 584) { return true }
            if CGRect(x: 760, y: 376, width: 330, height: 66).intersects(r) { return true }      // the stalls' awnings and cloths
            if r.minY < 352 && !(r.minX > 516 && r.maxX < 584) && r.maxY > 300 { return true }
        case .harbour:
            if CGRect(x: 554, y: 446, width: 248, height: 52).intersects(r) { return true }      // the fish stalls' awnings
            let water = CGRect(x: 0, y: 240, width: 1100, height: 222)
            let pier = CGRect(x: 306, y: 300, width: 88, height: 162), quay = CGRect(x: 0, y: 330, width: 262, height: 132)
            if water.intersects(r) && !pier.contains(r) && !quay.contains(r) && behind == nil { return true }
        case .snow:
            if r.minY < 340 { return true }
        }
        return false
    }
    static func inside(_ r: CGRect) -> Bool { CGRect(origin: .zero, size: size).insetBy(dx: 6, dy: 6).contains(r) }

    enum Item: Identifiable { case prop(Int), cat(Int)
        var id: String { switch self { case .prop(let i): return "p\(i)"; case .cat(let i): return "c\(i)" } }
    }
    /// Props and cats sorted by the line they stand on (a cat behind a prop stands a little above its bottom edge).
    static func depthOrder(_ place: FindPlace, _ cats: [FindCat]) -> [Item] {
        var all: [(CGFloat, Item)] = props(place).enumerated().map { ($0.element.baseline, .prop($0.offset)) }
        all += cats.map { ($0.feet.y, .cat($0.id)) }
        return all.sorted { $0.0 < $1.0 }.map { $0.1 }
    }
}

/// Fill a shape and give it the cat's sticker outline.
private func inked(_ ctx: inout GraphicsContext, _ p: Path, _ c: Color, _ w: CGFloat = 2.2, alpha: Double = 1) {
    ctx.fill(p, with: .color(c))
    ctx.stroke(p, with: .color(FindScene.ink.opacity(alpha)), style: StrokeStyle(lineWidth: w, lineCap: .round, lineJoin: .round))
}
/// Several blobs that read as one (outline all, then fill all, so the inner lines vanish).
private func blobs(_ ctx: inout GraphicsContext, _ rs: [CGRect], _ c: Color, _ w: CGFloat = 2.2, alpha: Double = 1) {
    for r in rs { ctx.stroke(Path(ellipseIn: r), with: .color(FindScene.ink.opacity(alpha)), lineWidth: w * 2) }
    for r in rs { ctx.fill(Path(ellipseIn: r), with: .color(c)) }
}
private func line(_ ctx: inout GraphicsContext, _ pts: [CGPoint], _ c: Color, _ w: CGFloat) {
    var p = Path(); p.addLines(pts)
    ctx.stroke(p, with: .color(c), style: StrokeStyle(lineWidth: w, lineCap: .round, lineJoin: .round))
}

private let stone = Color(red: 0.88, green: 0.85, blue: 0.80), stoneShade = Color(red: 0.78, green: 0.75, blue: 0.70)
private let wood = Color(red: 0.82, green: 0.62, blue: 0.42), woodDark = Color(red: 0.62, green: 0.44, blue: 0.30)
private let leaf = Color(red: 0.48, green: 0.71, blue: 0.44), leafLight = Color(red: 0.62, green: 0.82, blue: 0.52)
private let roofBlue = Color(red: 0.49, green: 0.53, blue: 0.80)
private let flagRed = Color(red: 0.92, green: 0.44, blue: 0.40), flagGold = Color(red: 0.98, green: 0.80, blue: 0.36)

/// The fixed kingdom behind everything: sky, hills, castle, moat, bridge, paths, village houses, market awnings.
struct FindBackdrop: View {
    let place: FindPlace
    var body: some View {
        Canvas { ctx, size in
            switch place {
            case .kingdom: kingdom(&ctx, size.width)
            case .harbour: harbour(&ctx, size.width)
            case .snow: snow(&ctx, size.width)
            }
        }
        .frame(width: FindScene.size.width, height: FindScene.size.height)
    }

    private func kingdom(_ ctx: inout GraphicsContext, _ W: CGFloat) {
            // sky + sun + clouds
            ctx.fill(Path(CGRect(x: 0, y: 0, width: W, height: 360)),
                     with: .linearGradient(Gradient(colors: [Color(red: 0.62, green: 0.82, blue: 0.96), Color(red: 0.90, green: 0.95, blue: 0.97)]),
                                           startPoint: .zero, endPoint: CGPoint(x: 0, y: 300)))
            ctx.fill(Path(ellipseIn: CGRect(x: 920, y: 36, width: 96, height: 96)), with: .color(Color(red: 1, green: 0.93, blue: 0.62).opacity(0.45)))
            inked(&ctx, Path(ellipseIn: CGRect(x: 938, y: 54, width: 60, height: 60)), Color(red: 1, green: 0.86, blue: 0.48), 2, alpha: 0.5)
            for (x, y, s) in [(120.0, 70.0, 1.0), (300, 40, 0.75), (790, 92, 0.9), (1040, 170, 0.6), (210, 170, 0.55)] as [(CGFloat, CGFloat, CGFloat)] {
                blobs(&ctx, [CGRect(x: x, y: y + 10 * s, width: 60 * s, height: 30 * s), CGRect(x: x + 24 * s, y: y, width: 54 * s, height: 40 * s),
                             CGRect(x: x + 60 * s, y: y + 12 * s, width: 50 * s, height: 28 * s)], .white.opacity(0.95), 1, alpha: 0.25)
            }
            // hills
            var far = Path(); far.move(to: CGPoint(x: 0, y: 262))
            far.addQuadCurve(to: CGPoint(x: 330, y: 228), control: CGPoint(x: 160, y: 205))
            far.addQuadCurve(to: CGPoint(x: 760, y: 236), control: CGPoint(x: 560, y: 262))
            far.addQuadCurve(to: CGPoint(x: W, y: 218), control: CGPoint(x: 950, y: 190))
            far.addLine(to: CGPoint(x: W, y: 400)); far.addLine(to: CGPoint(x: 0, y: 400)); far.closeSubpath()
            inked(&ctx, far, Color(red: 0.72, green: 0.85, blue: 0.66), 2, alpha: 0.35)
            for (x, y) in [(70.0, 238.0), (128, 230), (242, 226), (860, 218), (930, 206), (1060, 218)] as [(CGFloat, CGFloat)] {
                line(&ctx, [CGPoint(x: x, y: y + 6), CGPoint(x: x, y: y + 20)], woodDark.opacity(0.7), 3)
                blobs(&ctx, [CGRect(x: x - 11, y: y - 12, width: 22, height: 22)], Color(red: 0.56, green: 0.76, blue: 0.50), 1, alpha: 0.4)
            }
            var near = Path(); near.move(to: CGPoint(x: 0, y: 300))
            near.addQuadCurve(to: CGPoint(x: 420, y: 296), control: CGPoint(x: 220, y: 262))
            near.addLine(to: CGPoint(x: 700, y: 296))
            near.addQuadCurve(to: CGPoint(x: W, y: 290), control: CGPoint(x: 900, y: 256))
            near.addLine(to: CGPoint(x: W, y: 400)); near.addLine(to: CGPoint(x: 0, y: 400)); near.closeSubpath()
            inked(&ctx, near, Color(red: 0.64, green: 0.80, blue: 0.56), 2, alpha: 0.4)
            // ground
            ctx.fill(Path(CGRect(x: 0, y: 340, width: W, height: 300)),
                     with: .linearGradient(Gradient(colors: [Color(red: 0.74, green: 0.87, blue: 0.56), Color(red: 0.66, green: 0.82, blue: 0.50)]),
                                           startPoint: CGPoint(x: 0, y: 340), endPoint: CGPoint(x: 0, y: 640)))
            castle(&ctx)
            // moat
            var moat = Path(); moat.move(to: CGPoint(x: 0, y: 310))
            moat.addCurve(to: CGPoint(x: W, y: 312), control1: CGPoint(x: 300, y: 304), control2: CGPoint(x: 800, y: 318))
            moat.addLine(to: CGPoint(x: W, y: 348))
            moat.addCurve(to: CGPoint(x: 0, y: 346), control1: CGPoint(x: 800, y: 354), control2: CGPoint(x: 300, y: 340))
            moat.closeSubpath()
            inked(&ctx, moat, Color(red: 0.55, green: 0.78, blue: 0.91))
            for (x, y) in [(60.0, 326.0), (190, 334), (330, 322), (420, 334), (660, 326), (760, 336), (880, 322), (1000, 334)] as [(CGFloat, CGFloat)] {
                var w = Path(); w.move(to: CGPoint(x: x, y: y))
                w.addQuadCurve(to: CGPoint(x: x + 14, y: y), control: CGPoint(x: x + 7, y: y - 5))
                w.addQuadCurve(to: CGPoint(x: x + 28, y: y), control: CGPoint(x: x + 21, y: y + 5))
                ctx.stroke(w, with: .color(.white.opacity(0.75)), style: StrokeStyle(lineWidth: 2, lineCap: .round))
            }
            // paths: from the bridge down to the bottom, a branch to the village and the market square
            var path = Path(); path.move(to: CGPoint(x: 522, y: 346))
            path.addCurve(to: CGPoint(x: 430, y: 640), control1: CGPoint(x: 510, y: 460), control2: CGPoint(x: 420, y: 540))
            path.addLine(to: CGPoint(x: 600, y: 640))
            path.addCurve(to: CGPoint(x: 578, y: 346), control1: CGPoint(x: 560, y: 540), control2: CGPoint(x: 590, y: 460))
            path.closeSubpath()
            let sand = Color(red: 0.95, green: 0.88, blue: 0.71)
            inked(&ctx, path, sand, 2, alpha: 0.45)
            var west = Path(); west.move(to: CGPoint(x: 520, y: 470))
            west.addQuadCurve(to: CGPoint(x: 20, y: 470), control: CGPoint(x: 300, y: 440))
            west.addLine(to: CGPoint(x: 20, y: 498)); west.addQuadCurve(to: CGPoint(x: 512, y: 502), control: CGPoint(x: 300, y: 472))
            west.closeSubpath()
            inked(&ctx, west, sand, 2, alpha: 0.45)
            inked(&ctx, Path(ellipseIn: CGRect(x: 730, y: 412, width: 370, height: 128)), sand, 2, alpha: 0.45)
            var east = Path(); east.move(to: CGPoint(x: 560, y: 430))
            east.addQuadCurve(to: CGPoint(x: 760, y: 452), control: CGPoint(x: 660, y: 420))
            east.addLine(to: CGPoint(x: 760, y: 482)); east.addQuadCurve(to: CGPoint(x: 552, y: 470), control: CGPoint(x: 660, y: 452))
            east.closeSubpath()
            ctx.fill(east, with: .color(sand))
            for (x, y) in [(560.0, 600.0), (470, 470), (520, 420), (860, 480), (980, 500)] as [(CGFloat, CGFloat)] {   // cobbles
                ctx.stroke(Path(ellipseIn: CGRect(x: x, y: y, width: 9, height: 5)), with: .color(FindScene.ink.opacity(0.18)), lineWidth: 1.2)
            }
            // bridge
            inked(&ctx, Path(roundedRect: CGRect(x: 516, y: 304, width: 68, height: 48), cornerRadius: 3), wood)
            for y in stride(from: 312.0, through: 344, by: 8) { line(&ctx, [CGPoint(x: 518, y: y), CGPoint(x: 582, y: y)], woodDark.opacity(0.55), 1.4) }
            for x in [512.0, 582] as [CGFloat] {
                inked(&ctx, Path(roundedRect: CGRect(x: x, y: 298, width: 6, height: 56), cornerRadius: 3), woodDark, 1.6)
            }
            // flowers in the grass
            var h: UInt64 = 7
            for _ in 0..<70 {
                h = h &* 6364136223846793005 &+ 1442695040888963407
                let x = CGFloat(h >> 33 % 1000) / 1000 * 1080 + 10, y = CGFloat((h >> 13) % 1000) / 1000 * 270 + 360
                let c = [Color(red: 1, green: 0.72, blue: 0.78), Color(red: 1, green: 0.92, blue: 0.55), .white][Int(h % 3)]
                for (dx, dy) in [(-2.2, 0.0), (2.2, 0), (0, -2.2), (0, 2.2)] as [(CGFloat, CGFloat)] {
                    ctx.fill(Path(ellipseIn: CGRect(x: x + dx - 1.8, y: y + dy - 1.8, width: 3.6, height: 3.6)), with: .color(c))
                }
                ctx.fill(Path(ellipseIn: CGRect(x: x - 1.2, y: y - 1.2, width: 2.4, height: 2.4)), with: .color(Color(red: 0.95, green: 0.65, blue: 0.25)))
            }
            for hs in FindScene.houses { house(&ctx, hs) }
            market(&ctx)
            lampPost(&ctx, 452, 418); lampPost(&ctx, 640, 418)
    }

    // MARK: harbour

    private func harbour(_ ctx: inout GraphicsContext, _ W: CGFloat) {
        ctx.fill(Path(CGRect(x: 0, y: 0, width: W, height: 260)),
                 with: .linearGradient(Gradient(colors: [Color(red: 0.58, green: 0.82, blue: 0.95), Color(red: 0.92, green: 0.96, blue: 0.97)]),
                                       startPoint: .zero, endPoint: CGPoint(x: 0, y: 240)))
        ctx.fill(Path(ellipseIn: CGRect(x: 66, y: 30, width: 90, height: 90)), with: .color(Color(red: 1, green: 0.93, blue: 0.62).opacity(0.45)))
        inked(&ctx, Path(ellipseIn: CGRect(x: 82, y: 46, width: 58, height: 58)), Color(red: 1, green: 0.86, blue: 0.48), 2, alpha: 0.5)
        for (x, y, s) in [(260.0, 60.0, 0.9), (560, 34, 0.7), (680, 120, 0.55), (420, 150, 0.5)] as [(CGFloat, CGFloat, CGFloat)] {
            blobs(&ctx, [CGRect(x: x, y: y + 10 * s, width: 60 * s, height: 30 * s), CGRect(x: x + 24 * s, y: y, width: 54 * s, height: 40 * s),
                         CGRect(x: x + 60 * s, y: y + 12 * s, width: 50 * s, height: 28 * s)], .white.opacity(0.95), 1, alpha: 0.25)
        }
        for (x, y, s) in [(330.0, 96.0, 1.0), (366, 112, 0.8), (720, 70, 0.9), (612, 176, 0.7)] as [(CGFloat, CGFloat, CGFloat)] {   // gulls
            var g = Path(); g.move(to: CGPoint(x: x - 10 * s, y: y)); g.addQuadCurve(to: CGPoint(x: x, y: y), control: CGPoint(x: x - 5 * s, y: y - 7 * s))
            g.addQuadCurve(to: CGPoint(x: x + 10 * s, y: y), control: CGPoint(x: x + 5 * s, y: y - 7 * s))
            ctx.stroke(g, with: .color(FindScene.ink.opacity(0.75)), style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
        }
        // the sea
        ctx.fill(Path(CGRect(x: 0, y: 240, width: W, height: 230)),
                 with: .linearGradient(Gradient(colors: [Color(red: 0.55, green: 0.80, blue: 0.91), Color(red: 0.40, green: 0.66, blue: 0.86)]),
                                       startPoint: CGPoint(x: 0, y: 240), endPoint: CGPoint(x: 0, y: 470)))
        line(&ctx, [CGPoint(x: 0, y: 240), CGPoint(x: W, y: 240)], FindScene.ink.opacity(0.35), 2)
        var sh: UInt64 = 11
        for _ in 0..<26 {
            sh = sh &* 6364136223846793005 &+ 1442695040888963407
            let x = CGFloat((sh >> 33) % 1000) / 1000 * 1060 + 10, y = CGFloat((sh >> 13) % 1000) / 1000 * 200 + 256
            var w = Path(); w.move(to: CGPoint(x: x, y: y))
            w.addQuadCurve(to: CGPoint(x: x + 12, y: y), control: CGPoint(x: x + 6, y: y - 4))
            w.addQuadCurve(to: CGPoint(x: x + 24, y: y), control: CGPoint(x: x + 18, y: y + 4))
            ctx.stroke(w, with: .color(.white.opacity(0.7)), style: StrokeStyle(lineWidth: 1.8, lineCap: .round))
        }
        // a sailboat on the horizon
        inked(&ctx, Path(roundedRect: CGRect(x: 200, y: 232, width: 30, height: 8), cornerRadius: 4), Color(red: 0.90, green: 0.48, blue: 0.38), 1.4)
        var sail = Path(); sail.move(to: CGPoint(x: 214, y: 230)); sail.addLine(to: CGPoint(x: 214, y: 204)); sail.addLine(to: CGPoint(x: 229, y: 230)); sail.closeSubpath()
        inked(&ctx, sail, .white, 1.4)
        // the headland and its lighthouse
        var land = Path(); land.move(to: CGPoint(x: 720, y: 252))
        land.addQuadCurve(to: CGPoint(x: 900, y: 196), control: CGPoint(x: 790, y: 196))
        land.addLine(to: CGPoint(x: W, y: 178)); land.addLine(to: CGPoint(x: W, y: 300))
        land.addQuadCurve(to: CGPoint(x: 720, y: 252), control: CGPoint(x: 900, y: 296)); land.closeSubpath()
        inked(&ctx, land, Color(red: 0.70, green: 0.82, blue: 0.58))
        for (x, y) in [(800.0, 252.0), (880, 262), (1000, 276), (1060, 262)] as [(CGFloat, CGFloat)] {
            blobs(&ctx, [CGRect(x: x, y: y, width: 30, height: 18)], Color(red: 0.74, green: 0.72, blue: 0.70), 1)
        }
        inked(&ctx, Path(roundedRect: CGRect(x: 994, y: 176, width: 58, height: 34), cornerRadius: 2), Color(red: 0.98, green: 0.94, blue: 0.86))
        var hr = Path(); hr.move(to: CGPoint(x: 988, y: 178)); hr.addLine(to: CGPoint(x: 1023, y: 154)); hr.addLine(to: CGPoint(x: 1058, y: 178)); hr.closeSubpath()
        inked(&ctx, hr, Color(red: 0.84, green: 0.45, blue: 0.36))
        inked(&ctx, Path(roundedRect: CGRect(x: 1014, y: 188, width: 14, height: 22), cornerRadius: 2), woodDark, 1.6)
        var tower = Path(); tower.move(to: CGPoint(x: 926, y: 214)); tower.addLine(to: CGPoint(x: 936, y: 104))
        tower.addLine(to: CGPoint(x: 972, y: 104)); tower.addLine(to: CGPoint(x: 982, y: 214)); tower.closeSubpath()
        ctx.fill(tower, with: .color(.white))
        ctx.drawLayer { l in
            l.clip(to: tower)
            for y in [120.0, 160, 200] as [CGFloat] { l.fill(Path(CGRect(x: 920, y: y, width: 70, height: 18)), with: .color(flagRed)) }
        }
        ctx.stroke(tower, with: .color(FindScene.ink), style: StrokeStyle(lineWidth: 2.2, lineJoin: .round))
        inked(&ctx, Path(roundedRect: CGRect(x: 930, y: 96, width: 48, height: 9), cornerRadius: 2), Color(red: 0.40, green: 0.40, blue: 0.46))
        inked(&ctx, Path(CGRect(x: 940, y: 72, width: 28, height: 24)), Color(red: 1, green: 0.90, blue: 0.56))
        line(&ctx, [CGPoint(x: 954, y: 74), CGPoint(x: 954, y: 94)], FindScene.ink.opacity(0.5), 1.4)
        var cap = Path(); cap.move(to: CGPoint(x: 936, y: 73)); cap.addQuadCurve(to: CGPoint(x: 972, y: 73), control: CGPoint(x: 954, y: 44)); cap.closeSubpath()
        inked(&ctx, cap, flagRed)
        inked(&ctx, Path(roundedRect: CGRect(x: 948, y: 172, width: 12, height: 22), cornerRadius: 5), woodDark, 1.6)
        // the quay, the shack, the pier and the dock
        inked(&ctx, Path(CGRect(x: -4, y: 330, width: 266, height: 140)), stone)
        for (x, y) in [(20.0, 452.0), (90, 456), (160, 450), (230, 440), (236, 400), (234, 360)] as [(CGFloat, CGFloat)] {
            ctx.stroke(Path(roundedRect: CGRect(x: x, y: y, width: 18, height: 8), cornerRadius: 2), with: .color(stoneShade), lineWidth: 1.4)
        }
        for hs in FindScene.shacks { house(&ctx, hs) }
        inked(&ctx, Path(roundedRect: CGRect(x: 66, y: 352, width: 96, height: 18), cornerRadius: 3), .white, 1.6)
        let sign = Text("FISH").font(.system(size: 12, weight: .heavy, design: .rounded)).foregroundColor(Color(red: 0.30, green: 0.46, blue: 0.70))
        ctx.draw(sign, at: CGPoint(x: 114, y: 361), anchor: .center)
        for x in stride(from: 312.0, through: 384, by: 24) { inked(&ctx, Path(CGRect(x: x, y: 296, width: 8, height: 170)), woodDark, 1.6) }
        inked(&ctx, Path(CGRect(x: 306, y: 300, width: 88, height: 166)), wood)
        for x in stride(from: 318.0, through: 384, by: 12) { line(&ctx, [CGPoint(x: x, y: 302), CGPoint(x: x, y: 464)], woodDark.opacity(0.45), 1.3) }
        inked(&ctx, Path(CGRect(x: -4, y: 462, width: W + 8, height: 190)), wood)
        var k = 0
        for y in stride(from: 484.0, to: 640, by: 22) {
            line(&ctx, [CGPoint(x: 0, y: y), CGPoint(x: W, y: y)], woodDark.opacity(0.35), 1.4)
            for x in stride(from: CGFloat(k % 2 == 0 ? 60 : 130), to: W, by: 140) { line(&ctx, [CGPoint(x: x, y: y - 22), CGPoint(x: x, y: y)], woodDark.opacity(0.35), 1.4) }
            k += 1
        }
        for x in [150.0, 470, 860, 1050] as [CGFloat] {                                     // bollards on the edge
            inked(&ctx, Path(roundedRect: CGRect(x: x, y: 452, width: 16, height: 18), cornerRadius: 5), Color(red: 0.40, green: 0.40, blue: 0.46), 1.8)
        }
        for (x, y, c) in [(268.0, 424.0, flagRed), (792, 300, flagGold), (560, 452, flagRed)] as [(CGFloat, CGFloat, Color)] {   // buoys
            inked(&ctx, Path(ellipseIn: CGRect(x: x, y: y, width: 16, height: 14)), c, 1.6)
            line(&ctx, [CGPoint(x: x + 2, y: y + 7), CGPoint(x: x + 14, y: y + 7)], .white, 2)
        }
        stall(&ctx, 562, 452, (Color(red: 0.42, green: 0.62, blue: 0.90), .white))
        stall(&ctx, 694, 452, (Color(red: 0.36, green: 0.72, blue: 0.66), .white))
    }

    // MARK: snowy village

    private func snow(_ ctx: inout GraphicsContext, _ W: CGFloat) {
        ctx.fill(Path(CGRect(x: 0, y: 0, width: W, height: 360)),
                 with: .linearGradient(Gradient(colors: [Color(red: 0.74, green: 0.82, blue: 0.93), Color(red: 0.94, green: 0.96, blue: 0.99)]),
                                       startPoint: .zero, endPoint: CGPoint(x: 0, y: 300)))
        for (x, y, s) in [(140.0, 60.0, 1.0), (520, 40, 0.8), (860, 90, 0.9)] as [(CGFloat, CGFloat, CGFloat)] {
            blobs(&ctx, [CGRect(x: x, y: y + 10 * s, width: 60 * s, height: 30 * s), CGRect(x: x + 24 * s, y: y, width: 54 * s, height: 40 * s),
                         CGRect(x: x + 60 * s, y: y + 12 * s, width: 50 * s, height: 28 * s)], .white.opacity(0.95), 1, alpha: 0.2)
        }
        // mountains with snowy caps
        for (x, top, w, c) in [(-40.0, 120.0, 420.0, 0.70), (300, 90, 460, 0.66), (700, 130, 460, 0.72)] as [(CGFloat, CGFloat, CGFloat, Double)] {
            var m = Path(); m.move(to: CGPoint(x: x, y: 300)); m.addLine(to: CGPoint(x: x + w / 2, y: top)); m.addLine(to: CGPoint(x: x + w, y: 300)); m.closeSubpath()
            inked(&ctx, m, Color(red: c, green: c + 0.06, blue: c + 0.16), 2, alpha: 0.35)
            var cap = Path(); cap.move(to: CGPoint(x: x + w / 2 - 44, y: top + 50)); cap.addLine(to: CGPoint(x: x + w / 2, y: top))
            cap.addLine(to: CGPoint(x: x + w / 2 + 44, y: top + 50)); cap.addLine(to: CGPoint(x: x + w / 2 + 22, y: top + 40))
            cap.addLine(to: CGPoint(x: x + w / 2 + 4, y: top + 54)); cap.addLine(to: CGPoint(x: x + w / 2 - 18, y: top + 38)); cap.closeSubpath()
            inked(&ctx, cap, .white, 1.6, alpha: 0.35)
        }
        var hills = Path(); hills.move(to: CGPoint(x: 0, y: 300))
        hills.addQuadCurve(to: CGPoint(x: 520, y: 296), control: CGPoint(x: 260, y: 250))
        hills.addQuadCurve(to: CGPoint(x: W, y: 286), control: CGPoint(x: 820, y: 246))
        hills.addLine(to: CGPoint(x: W, y: 400)); hills.addLine(to: CGPoint(x: 0, y: 400)); hills.closeSubpath()
        inked(&ctx, hills, Color(red: 0.95, green: 0.97, blue: 1.0), 2, alpha: 0.3)
        for (x, y) in [(60.0, 286.0), (96, 280), (420, 286), (600, 278), (640, 286), (910, 268), (950, 276)] as [(CGFloat, CGFloat)] {
            smallPine(&ctx, x, y, 0.55)
        }
        // the ground
        ctx.fill(Path(CGRect(x: 0, y: 330, width: W, height: 310)),
                 with: .linearGradient(Gradient(colors: [Color(red: 0.97, green: 0.98, blue: 1.0), Color(red: 0.90, green: 0.93, blue: 0.98)]),
                                       startPoint: CGPoint(x: 0, y: 330), endPoint: CGPoint(x: 0, y: 640)))
        for (x, y, w) in [(120.0, 560.0, 180.0), (760, 600, 220), (880, 420, 160), (300, 420, 140)] as [(CGFloat, CGFloat, CGFloat)] {
            ctx.fill(Path(ellipseIn: CGRect(x: x, y: y, width: w, height: 22)), with: .color(Color(red: 0.84, green: 0.89, blue: 0.97).opacity(0.7)))
        }
        // the frozen pond
        inked(&ctx, Path(ellipseIn: CGRect(x: 392, y: 456, width: 340, height: 112)), Color(red: 0.78, green: 0.89, blue: 0.97))
        ctx.fill(Path(ellipseIn: CGRect(x: 420, y: 470, width: 284, height: 82)), with: .color(Color(red: 0.86, green: 0.94, blue: 0.99)))
        for (x, y) in [(470.0, 486.0), (600, 520), (650, 480)] as [(CGFloat, CGFloat)] {
            line(&ctx, [CGPoint(x: x, y: y + 12), CGPoint(x: x + 18, y: y)], .white, 3)
            line(&ctx, [CGPoint(x: x + 10, y: y + 14), CGPoint(x: x + 20, y: y + 7)], .white, 2)
        }
        // footprints across the snow
        for i in 0..<9 { ctx.fill(Path(ellipseIn: CGRect(x: 180 + CGFloat(i) * 26, y: 452 + CGFloat(i % 2) * 8, width: 7, height: 4)), with: .color(Color(red: 0.78, green: 0.84, blue: 0.94))) }
        for hs in FindScene.cabins { cabin(&ctx, hs) }
        lampPost(&ctx, 342, 452); lampPost(&ctx, 790, 560)
        // falling snow
        var h: UInt64 = 5
        for _ in 0..<90 {
            h = h &* 6364136223846793005 &+ 1442695040888963407
            let x = CGFloat((h >> 33) % 1000) / 1000 * W, y = CGFloat((h >> 13) % 1000) / 1000 * 640, r = CGFloat(1.4 + Double(h % 3) * 0.7)
            ctx.fill(Path(ellipseIn: CGRect(x: x, y: y, width: r * 2, height: r * 2)), with: .color(.white.opacity(0.9)))
            ctx.stroke(Path(ellipseIn: CGRect(x: x, y: y, width: r * 2, height: r * 2)), with: .color(Color(red: 0.70, green: 0.78, blue: 0.90).opacity(0.5)), lineWidth: 0.6)
        }
    }

    private func smallPine(_ ctx: inout GraphicsContext, _ x: CGFloat, _ y: CGFloat, _ s: CGFloat) {
        for (k, w) in [(0, 30.0), (1, 24), (2, 16)] as [(Int, CGFloat)] {
            var t = Path(); let ty = y - CGFloat(k) * 14 * s
            t.move(to: CGPoint(x: x - w * s, y: ty)); t.addLine(to: CGPoint(x: x, y: ty - 26 * s)); t.addLine(to: CGPoint(x: x + w * s, y: ty)); t.closeSubpath()
            inked(&ctx, t, Color(red: 0.36, green: 0.58, blue: 0.48), 1.4, alpha: 0.5)
        }
    }

    private func cabin(_ ctx: inout GraphicsContext, _ h: FindScene.House) {
        let w = h.wall
        inked(&ctx, Path(CGRect(x: w.maxX - 34, y: w.minY - h.roof + 2, width: 14, height: h.roof)), Color(red: 0.62, green: 0.60, blue: 0.62), 2)
        blobs(&ctx, [CGRect(x: w.maxX - 37, y: w.minY - h.roof - 4, width: 20, height: 9)], .white, 1)
        blobs(&ctx, [CGRect(x: w.maxX - 30, y: w.minY - h.roof - 22, width: 14, height: 11), CGRect(x: w.maxX - 22, y: w.minY - h.roof - 36, width: 18, height: 13)],
              Color(red: 0.88, green: 0.90, blue: 0.94), 1, alpha: 0.4)
        inked(&ctx, Path(w), h.wallC)
        for y in stride(from: w.minY + 10, to: w.maxY, by: 10) { line(&ctx, [CGPoint(x: w.minX + 2, y: y), CGPoint(x: w.maxX - 2, y: y)], FindScene.ink.opacity(0.3), 1.4) }
        for y in stride(from: w.minY + 5, to: w.maxY, by: 10) {
            for x in [w.minX - 3, w.maxX - 4] { inked(&ctx, Path(ellipseIn: CGRect(x: x, y: y - 3, width: 7, height: 7)), Color(red: 0.86, green: 0.70, blue: 0.52), 1.2) }
        }
        var roof = Path(); roof.move(to: CGPoint(x: w.minX - 12, y: w.minY + 2))
        roof.addLine(to: CGPoint(x: w.midX - w.width * 0.2, y: w.minY - h.roof)); roof.addLine(to: CGPoint(x: w.midX + w.width * 0.2, y: w.minY - h.roof))
        roof.addLine(to: CGPoint(x: w.maxX + 12, y: w.minY + 2)); roof.closeSubpath()
        inked(&ctx, roof, Color(red: 0.50, green: 0.34, blue: 0.28))
        var snowCap = Path(); snowCap.move(to: CGPoint(x: w.minX - 14, y: w.minY - 2))
        snowCap.addLine(to: CGPoint(x: w.midX - w.width * 0.22, y: w.minY - h.roof - 6))
        snowCap.addQuadCurve(to: CGPoint(x: w.midX + w.width * 0.22, y: w.minY - h.roof - 6), control: CGPoint(x: w.midX, y: w.minY - h.roof - 12))
        snowCap.addLine(to: CGPoint(x: w.maxX + 14, y: w.minY - 2))
        snowCap.addQuadCurve(to: CGPoint(x: w.minX - 14, y: w.minY - 2), control: CGPoint(x: w.midX, y: w.minY - h.roof * 0.35))
        inked(&ctx, snowCap, .white)
        for k in 0..<Int(w.width / 14) {                                         // icicles
            let x = w.minX - 6 + CGFloat(k) * 14 + 6
            var ic = Path(); ic.move(to: CGPoint(x: x - 3, y: w.minY + 1)); ic.addLine(to: CGPoint(x: x, y: w.minY + 8 + CGFloat(k % 3) * 3)); ic.addLine(to: CGPoint(x: x + 3, y: w.minY + 1))
            ctx.fill(ic, with: .color(Color(red: 0.88, green: 0.95, blue: 1.0)))
        }
        var door = Path(); door.move(to: CGPoint(x: w.midX - 11, y: w.maxY)); door.addLine(to: CGPoint(x: w.midX - 11, y: w.maxY - 28))
        door.addQuadCurve(to: CGPoint(x: w.midX + 11, y: w.maxY - 28), control: CGPoint(x: w.midX, y: w.maxY - 40))
        door.addLine(to: CGPoint(x: w.midX + 11, y: w.maxY)); door.closeSubpath()
        inked(&ctx, door, Color(red: 0.44, green: 0.30, blue: 0.24), 2)
        for x in [w.minX + 14, w.maxX - 34] {
            inked(&ctx, Path(roundedRect: CGRect(x: x, y: w.minY + 14, width: 20, height: 18), cornerRadius: 2), Color(red: 1, green: 0.86, blue: 0.52), 1.8)
            line(&ctx, [CGPoint(x: x + 10, y: w.minY + 14), CGPoint(x: x + 10, y: w.minY + 32)], FindScene.ink.opacity(0.7), 1.4)
            blobs(&ctx, [CGRect(x: x - 3, y: w.minY + 10, width: 26, height: 6)], .white, 0.8)
        }
    }

    private func stall(_ ctx: inout GraphicsContext, _ x: CGFloat, _ top: CGFloat, _ colors: (Color, Color)) {
        for px in [x + 4, x + 90] { inked(&ctx, Path(CGRect(x: px, y: top + 10, width: 6, height: 48)), woodDark, 1.6) }
        inked(&ctx, Path(CGRect(x: x + 8, y: top + 16, width: 84, height: 34)), Color(red: 0.98, green: 0.94, blue: 0.86), 1.6)
        let (a, b) = colors
        for i in 0..<6 {
            var s = Path(); let sx = x - 4 + CGFloat(i) * 18
            s.move(to: CGPoint(x: sx + 4, y: top)); s.addLine(to: CGPoint(x: sx + 22, y: top)); s.addLine(to: CGPoint(x: sx + 18, y: top + 16))
            s.addQuadCurve(to: CGPoint(x: sx, y: top + 16), control: CGPoint(x: sx + 9, y: top + 26)); s.closeSubpath()
            ctx.fill(s, with: .color(i % 2 == 0 ? a : b))
        }
        var edge = Path(); edge.move(to: CGPoint(x: x, y: top)); edge.addLine(to: CGPoint(x: x + 104, y: top)); edge.addLine(to: CGPoint(x: x + 104, y: top + 16))
        for i in (0..<6).reversed() { let sx = x - 4 + CGFloat(i) * 18; edge.addQuadCurve(to: CGPoint(x: sx, y: top + 16), control: CGPoint(x: sx + 9, y: top + 26)) }
        edge.closeSubpath()
        ctx.stroke(edge, with: .color(FindScene.ink), style: StrokeStyle(lineWidth: 2, lineJoin: .round))
    }

    private func lampPost(_ ctx: inout GraphicsContext, _ x: CGFloat, _ y: CGFloat) {
        inked(&ctx, Path(roundedRect: CGRect(x: x - 2.5, y: y - 50, width: 5, height: 50), cornerRadius: 2), Color(red: 0.30, green: 0.30, blue: 0.36), 1.4)
        inked(&ctx, Path(roundedRect: CGRect(x: x - 6, y: y - 3, width: 12, height: 4), cornerRadius: 2), Color(red: 0.30, green: 0.30, blue: 0.36), 1.4)
        inked(&ctx, Path(roundedRect: CGRect(x: x - 7, y: y - 64, width: 14, height: 16), cornerRadius: 3), Color(red: 1, green: 0.90, blue: 0.56), 1.6)
        var cap = Path(); cap.move(to: CGPoint(x: x - 9, y: y - 63)); cap.addLine(to: CGPoint(x: x, y: y - 71)); cap.addLine(to: CGPoint(x: x + 9, y: y - 63)); cap.closeSubpath()
        inked(&ctx, cap, Color(red: 0.30, green: 0.30, blue: 0.36), 1.4)
    }

    private func castle(_ ctx: inout GraphicsContext) {
        func tower(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ bottom: CGFloat, _ apex: CGFloat, flag: Color) {
            line(&ctx, [CGPoint(x: x + w / 2, y: apex), CGPoint(x: x + w / 2, y: apex - 26)], FindScene.ink, 2)
            var f = Path(); f.move(to: CGPoint(x: x + w / 2, y: apex - 26))
            f.addQuadCurve(to: CGPoint(x: x + w / 2 + 22, y: apex - 19), control: CGPoint(x: x + w / 2 + 12, y: apex - 30))
            f.addQuadCurve(to: CGPoint(x: x + w / 2, y: apex - 13), control: CGPoint(x: x + w / 2 + 10, y: apex - 10))
            f.closeSubpath()
            inked(&ctx, f, flag, 1.6)
            inked(&ctx, Path(CGRect(x: x, y: y, width: w, height: bottom - y)), stone)
            for k in 0..<Int((bottom - y) / 16) {                                   // a few stones
                let yy = y + 12 + CGFloat(k) * 16, xx = x + (k % 2 == 0 ? 8 : w - 22)
                ctx.stroke(Path(roundedRect: CGRect(x: xx, y: yy, width: 14, height: 7), cornerRadius: 2), with: .color(stoneShade), lineWidth: 1.4)
            }
            var roof = Path(); roof.move(to: CGPoint(x: x - 8, y: y + 2)); roof.addLine(to: CGPoint(x: x + w / 2, y: apex))
            roof.addLine(to: CGPoint(x: x + w + 8, y: y + 2)); roof.closeSubpath()
            inked(&ctx, roof, roofBlue)
            line(&ctx, [CGPoint(x: x + w / 2, y: apex + 10), CGPoint(x: x + w / 2 - 6, y: y)], Color.white.opacity(0.35), 3)
            window(x + w / 2 - 7, y + 26)
        }
        func window(_ x: CGFloat, _ y: CGFloat) {
            var p = Path(); p.move(to: CGPoint(x: x, y: y + 22)); p.addLine(to: CGPoint(x: x, y: y + 7))
            p.addQuadCurve(to: CGPoint(x: x + 14, y: y + 7), control: CGPoint(x: x + 7, y: y - 4)); p.addLine(to: CGPoint(x: x + 14, y: y + 22)); p.closeSubpath()
            inked(&ctx, p, Color(red: 0.36, green: 0.34, blue: 0.46), 1.8)
            ctx.fill(Path(ellipseIn: CGRect(x: x + 3, y: y + 6, width: 4, height: 4)), with: .color(flagGold.opacity(0.8)))
        }
        // the keep, behind the wall
        tower(500, 104, 100, 200, 36, flag: flagGold)
        window(526, 150); window(560, 150)
        // the curtain wall (its battlements are a prop — cats sit on the walkway behind them)
        inked(&ctx, Path(CGRect(x: 428, y: 190, width: 244, height: 124)), stone)
        for (x, y) in [(446.0, 214.0), (604, 222), (470, 270), (630, 276), (452, 296), (612, 300)] as [(CGFloat, CGFloat)] {
            ctx.stroke(Path(roundedRect: CGRect(x: x, y: y, width: 16, height: 8), cornerRadius: 2), with: .color(stoneShade), lineWidth: 1.4)
        }
        // banners
        for (x, c) in [(472.0, flagRed), (606, flagRed)] as [(CGFloat, Color)] {
            var b = Path(); b.move(to: CGPoint(x: x, y: 204)); b.addLine(to: CGPoint(x: x + 22, y: 204)); b.addLine(to: CGPoint(x: x + 22, y: 244))
            b.addLine(to: CGPoint(x: x + 11, y: 236)); b.addLine(to: CGPoint(x: x, y: 244)); b.closeSubpath()
            inked(&ctx, b, c, 1.8)
            ctx.fill(Path(ellipseIn: CGRect(x: x + 6, y: 214, width: 10, height: 10)), with: .color(flagGold))
        }
        // the gate
        var gate = Path(); gate.move(to: CGPoint(x: 522, y: 314)); gate.addLine(to: CGPoint(x: 522, y: 262))
        gate.addQuadCurve(to: CGPoint(x: 578, y: 262), control: CGPoint(x: 550, y: 230)); gate.addLine(to: CGPoint(x: 578, y: 314)); gate.closeSubpath()
        inked(&ctx, gate, Color(red: 0.42, green: 0.32, blue: 0.30))
        for x in stride(from: 532.0, through: 570, by: 10) { line(&ctx, [CGPoint(x: x, y: 254), CGPoint(x: x, y: 312)], Color(red: 0.28, green: 0.22, blue: 0.22), 2) }
        for y in [272.0, 292] as [CGFloat] { line(&ctx, [CGPoint(x: 524, y: y), CGPoint(x: 576, y: y)], Color(red: 0.28, green: 0.22, blue: 0.22), 2) }
        // the side towers
        tower(380, 126, 62, 316, 62, flag: flagRed)
        tower(658, 126, 62, 316, 62, flag: flagRed)
    }

    private func house(_ ctx: inout GraphicsContext, _ h: FindScene.House) {
        let w = h.wall
        // chimney first (the roof overlaps its foot)
        inked(&ctx, Path(CGRect(x: w.maxX - 34, y: w.minY - h.roof + 4, width: 14, height: h.roof - 4)), Color(red: 0.80, green: 0.52, blue: 0.44), 2)
        inked(&ctx, Path(CGRect(x: w.maxX - 37, y: w.minY - h.roof, width: 20, height: 6)), Color(red: 0.70, green: 0.44, blue: 0.38), 2)
        inked(&ctx, Path(w), h.wallC)
        var roof = Path(); roof.move(to: CGPoint(x: w.minX - 10, y: w.minY + 2))
        roof.addLine(to: CGPoint(x: w.midX - w.width * 0.2, y: w.minY - h.roof)); roof.addLine(to: CGPoint(x: w.midX + w.width * 0.2, y: w.minY - h.roof))
        roof.addLine(to: CGPoint(x: w.maxX + 10, y: w.minY + 2)); roof.closeSubpath()
        inked(&ctx, roof, h.roofC)
        for k in 1..<3 {                                                     // shingle rows
            let y = w.minY - h.roof * CGFloat(k) / 3, t = CGFloat(k) / 3
            line(&ctx, [CGPoint(x: w.minX - 10 + (w.width * 0.3 + 10) * t + 4, y: y), CGPoint(x: w.maxX + 10 - (w.width * 0.3 + 10) * t - 4, y: y)],
                 FindScene.ink.opacity(0.25), 1.4)
        }
        // door + windows with flower boxes
        var door = Path(); door.move(to: CGPoint(x: w.midX - 11, y: w.maxY)); door.addLine(to: CGPoint(x: w.midX - 11, y: w.maxY - 26))
        door.addQuadCurve(to: CGPoint(x: w.midX + 11, y: w.maxY - 26), control: CGPoint(x: w.midX, y: w.maxY - 40))
        door.addLine(to: CGPoint(x: w.midX + 11, y: w.maxY)); door.closeSubpath()
        inked(&ctx, door, woodDark, 2)
        ctx.fill(Path(ellipseIn: CGRect(x: w.midX + 4, y: w.maxY - 16, width: 3, height: 3)), with: .color(flagGold))
        for x in [w.minX + 12, w.maxX - 32] {
            inked(&ctx, Path(roundedRect: CGRect(x: x, y: w.minY + 12, width: 20, height: 18), cornerRadius: 2), Color(red: 0.70, green: 0.84, blue: 0.94), 1.8)
            line(&ctx, [CGPoint(x: x + 10, y: w.minY + 12), CGPoint(x: x + 10, y: w.minY + 30)], FindScene.ink.opacity(0.7), 1.4)
            line(&ctx, [CGPoint(x: x, y: w.minY + 21), CGPoint(x: x + 20, y: w.minY + 21)], FindScene.ink.opacity(0.7), 1.4)
            inked(&ctx, Path(roundedRect: CGRect(x: x - 2, y: w.minY + 30, width: 24, height: 5), cornerRadius: 1.5), woodDark, 1.4)
            for k in 0..<4 { ctx.fill(Path(ellipseIn: CGRect(x: x + CGFloat(k) * 6, y: w.minY + 27, width: 5, height: 5)), with: .color([flagRed, flagGold][k % 2])) }
        }
    }

    private func market(_ ctx: inout GraphicsContext) {
        // bunting strung across the square
        var str = Path(); str.move(to: CGPoint(x: 760, y: 372)); str.addQuadCurve(to: CGPoint(x: 1090, y: 372), control: CGPoint(x: 925, y: 396))
        ctx.stroke(str, with: .color(FindScene.ink.opacity(0.6)), lineWidth: 1.4)
        for k in 0..<11 {
            let t = CGFloat(k) / 10 * 0.92 + 0.04, x = 760 + 330 * t, y = 372 + 4 * 24 * t * (1 - t)
            var f = Path(); f.move(to: CGPoint(x: x - 7, y: y)); f.addLine(to: CGPoint(x: x + 7, y: y)); f.addLine(to: CGPoint(x: x, y: y + 14)); f.closeSubpath()
            inked(&ctx, f, [flagRed, flagGold, Color(red: 0.50, green: 0.70, blue: 0.92), leafLight][k % 4], 1.2)
        }
        // three stalls: posts, a back cloth and a striped, scalloped awning (their counters are props in front)
        let stripes: [(Color, Color)] = [(flagRed, .white), (Color(red: 0.42, green: 0.62, blue: 0.90), .white), (flagGold, .white)]
        for (k, x) in [766.0, 878, 990].enumerated() {
            let x = CGFloat(x)
            for px in [x + 4, x + 84] { inked(&ctx, Path(CGRect(x: px, y: 398, width: 6, height: 44)), woodDark, 1.6) }
            inked(&ctx, Path(CGRect(x: x + 8, y: 404, width: 78, height: 36)), Color(red: 0.98, green: 0.94, blue: 0.86), 1.6)
            let (a, b) = stripes[k]
            for i in 0..<6 {
                var s = Path(); let sx = x - 4 + CGFloat(i) * 17
                s.move(to: CGPoint(x: sx + 4, y: 388)); s.addLine(to: CGPoint(x: sx + 21, y: 388)); s.addLine(to: CGPoint(x: sx + 17, y: 404))
                s.addQuadCurve(to: CGPoint(x: sx, y: 404), control: CGPoint(x: sx + 8.5, y: 414)); s.closeSubpath()
                ctx.fill(s, with: .color(i % 2 == 0 ? a : b))
            }
            var edge = Path(); edge.move(to: CGPoint(x: x, y: 388)); edge.addLine(to: CGPoint(x: x + 98, y: 388))
            edge.addLine(to: CGPoint(x: x + 98, y: 404))
            for i in (0..<6).reversed() {
                let sx = x - 4 + CGFloat(i) * 17
                edge.addQuadCurve(to: CGPoint(x: sx, y: 404), control: CGPoint(x: sx + 8.5, y: 414))
            }
            edge.closeSubpath()
            ctx.stroke(edge, with: .color(FindScene.ink), style: StrokeStyle(lineWidth: 2, lineJoin: .round))
        }
    }
}

/// One prop, drawn in its own small canvas so it can sit between cats in the depth order.
struct FindPropView: View {
    let prop: FindProp
    var body: some View {
        let r = prop.rect
        Canvas { ctx, _ in
            ctx.translateBy(x: -r.minX, y: -r.minY)
            draw(&ctx, r)
        }
        .frame(width: r.width + 16, height: r.height + 16)
        .offset(x: r.minX - 8, y: r.minY - 8)
        .allowsHitTesting(false)
    }

    private func draw(_ ctx: inout GraphicsContext, _ r0: CGRect) {
        ctx.translateBy(x: 8, y: 8)
        let r = prop.rect
        switch prop.kind {
        case .parapet:
            inked(&ctx, Path(CGRect(x: r.minX, y: r.minY + 18, width: r.width, height: r.height - 18)), stone)
            for x in stride(from: r.minX, to: r.maxX - 10, by: 34) {
                inked(&ctx, Path(CGRect(x: x, y: r.minY, width: 20, height: 20)), stone)
            }
            ctx.fill(Path(CGRect(x: r.minX + 1.1, y: r.minY + 17, width: r.width - 2.2, height: 3)), with: .color(stone))
        case .bush:
            let w = r.width, h = r.height
            blobs(&ctx, [CGRect(x: r.minX, y: r.minY + h * 0.3, width: w * 0.45, height: h * 0.7),
                         CGRect(x: r.minX + w * 0.22, y: r.minY, width: w * 0.5, height: h * 0.9),
                         CGRect(x: r.minX + w * 0.52, y: r.minY + h * 0.18, width: w * 0.48, height: h * 0.82)], leaf, 1.1)
            for (fx, fy) in [(0.3, 0.45), (0.6, 0.35), (0.8, 0.6), (0.45, 0.7)] as [(CGFloat, CGFloat)] {
                ctx.fill(Path(ellipseIn: CGRect(x: r.minX + w * fx - 2.5, y: r.minY + h * fy - 2.5, width: 5, height: 5)), with: .color(Color(red: 1, green: 0.62, blue: 0.70)))
            }
            line(&ctx, [CGPoint(x: r.minX + w * 0.32, y: r.minY + h * 0.25), CGPoint(x: r.minX + w * 0.42, y: r.minY + h * 0.17)], leafLight, 3)
        case .crate:
            inked(&ctx, Path(roundedRect: r, cornerRadius: 3), wood)
            ctx.stroke(Path(roundedRect: r.insetBy(dx: 5, dy: 5), cornerRadius: 2), with: .color(woodDark.opacity(0.8)), lineWidth: 1.6)
            line(&ctx, [CGPoint(x: r.minX + 6, y: r.minY + 6), CGPoint(x: r.maxX - 6, y: r.maxY - 6)], woodDark.opacity(0.8), 2.4)
            line(&ctx, [CGPoint(x: r.maxX - 6, y: r.minY + 6), CGPoint(x: r.minX + 6, y: r.maxY - 6)], woodDark.opacity(0.8), 2.4)
        case .barrels:
            for k in 0..<2 {
                let b = CGRect(x: r.minX + CGFloat(k) * (r.width / 2), y: r.minY + CGFloat(k) * 4, width: r.width / 2, height: r.height - CGFloat(k) * 4)
                inked(&ctx, Path(roundedRect: b, cornerRadius: 9), Color(red: 0.76, green: 0.52, blue: 0.34))
                for f in [0.22, 0.78] as [CGFloat] {
                    line(&ctx, [CGPoint(x: b.minX + 2, y: b.minY + b.height * f), CGPoint(x: b.maxX - 2, y: b.minY + b.height * f)], Color(red: 0.42, green: 0.40, blue: 0.40), 2.6)
                }
                ctx.fill(Path(roundedRect: CGRect(x: b.minX + 7, y: b.minY + 6, width: 3, height: b.height - 12), cornerRadius: 1.5), with: .color(.white.opacity(0.25)))
            }
        case .hay:
            inked(&ctx, Path(roundedRect: r, cornerRadius: 10), Color(red: 0.97, green: 0.84, blue: 0.47))
            for k in 0..<7 {
                let x = r.minX + 8 + CGFloat(k) * 9
                line(&ctx, [CGPoint(x: x, y: r.minY + 8), CGPoint(x: x + 3, y: r.maxY - 8)], Color(red: 0.85, green: 0.66, blue: 0.30), 1.3)
            }
            line(&ctx, [CGPoint(x: r.minX + 2, y: r.midY), CGPoint(x: r.maxX - 2, y: r.midY)], Color(red: 0.75, green: 0.45, blue: 0.30), 2.4)
        case .counter:
            let c = prop.cover
            // goods on top
            switch prop.tint {
            case 0: for k in 0..<7 { blobs(&ctx, [CGRect(x: c.minX + 6 + CGFloat(k) * 11.5, y: c.minY - 9, width: 11, height: 11)], flagRed, 0.9) }
            case 1: for k in 0..<4 { blobs(&ctx, [CGRect(x: c.minX + 6 + CGFloat(k) * 21, y: c.minY - 10, width: 22, height: 12)], Color(red: 0.90, green: 0.68, blue: 0.40), 0.9) }
            case 3: for k in 0..<4 { fish(&ctx, CGPoint(x: c.minX + 14 + CGFloat(k) * 22, y: c.minY - 5), k % 2 == 0) }
            case 4: for k in 0..<5 {
                let x = c.minX + 8 + CGFloat(k) * 17
                blobs(&ctx, [CGRect(x: x, y: c.minY - 10, width: 15, height: 11)], Color(red: 0.94, green: 0.50, blue: 0.40), 0.9)
                for d in [3.0, 9] as [CGFloat] { ctx.fill(Path(ellipseIn: CGRect(x: x + d, y: c.minY - 8, width: 2.5, height: 2.5)), with: .color(FindScene.ink)) }
            }
            default: for k in 0..<5 { blobs(&ctx, [CGRect(x: c.minX + 6 + CGFloat(k) * 17, y: c.minY - 11, width: 16, height: 13)], Color(red: 0.66, green: 0.80, blue: 0.40), 0.9) }
            }
            inked(&ctx, Path(CGRect(x: c.minX, y: c.minY, width: c.width, height: c.height)), wood)
            for k in 1..<4 { line(&ctx, [CGPoint(x: c.minX + c.width * CGFloat(k) / 4, y: c.minY + 4), CGPoint(x: c.minX + c.width * CGFloat(k) / 4, y: c.maxY - 2)], woodDark.opacity(0.6), 1.4) }
            inked(&ctx, Path(CGRect(x: c.minX - 3, y: c.minY - 1, width: c.width + 6, height: 6)), woodDark, 1.8)
        case .well:
            let c = prop.cover
            for px in [c.minX + 6, c.maxX - 12] { inked(&ctx, Path(CGRect(x: px, y: r.minY + 14, width: 6, height: c.minY - r.minY - 10)), woodDark, 1.6) }
            var roof = Path(); roof.move(to: CGPoint(x: c.minX - 4, y: r.minY + 20)); roof.addLine(to: CGPoint(x: c.midX, y: r.minY))
            roof.addLine(to: CGPoint(x: c.maxX + 4, y: r.minY + 20)); roof.closeSubpath()
            inked(&ctx, roof, Color(red: 0.84, green: 0.45, blue: 0.36))
            line(&ctx, [CGPoint(x: c.midX, y: r.minY + 20), CGPoint(x: c.midX, y: c.minY - 10)], FindScene.ink.opacity(0.7), 1.4)
            inked(&ctx, Path(roundedRect: CGRect(x: c.midX - 6, y: c.minY - 14, width: 12, height: 10), cornerRadius: 2), Color(red: 0.62, green: 0.62, blue: 0.68), 1.6)
            inked(&ctx, Path(roundedRect: c, cornerRadius: 6), stone)
            inked(&ctx, Path(roundedRect: CGRect(x: c.minX - 3, y: c.minY - 2, width: c.width + 6, height: 9), cornerRadius: 4), stoneShade)
            for (x, y) in [(10.0, 16.0), (36, 14), (22, 26), (48, 28)] as [(CGFloat, CGFloat)] {
                ctx.stroke(Path(roundedRect: CGRect(x: c.minX + x, y: c.minY + y, width: 14, height: 7), cornerRadius: 2), with: .color(stoneShade), lineWidth: 1.4)
            }
        case .wall:
            inked(&ctx, Path(roundedRect: prop.cover, cornerRadius: 4), stone)
            var k = 0
            for y in stride(from: prop.cover.minY + 4, to: prop.cover.maxY - 6, by: 10) {
                for x in stride(from: prop.cover.minX + (k % 2 == 0 ? 4 : 14), to: prop.cover.maxX - 18, by: 22) {
                    ctx.stroke(Path(roundedRect: CGRect(x: x, y: y, width: 18, height: 8), cornerRadius: 2.5), with: .color(stoneShade), lineWidth: 1.3)
                }
                k += 1
            }
            for x in stride(from: prop.cover.minX + 6, to: prop.cover.maxX, by: 30) {     // a little moss
                blobs(&ctx, [CGRect(x: x, y: prop.cover.minY - 3, width: 12, height: 7)], leaf, 0.8)
            }
        case .tree:
            let c = prop.cover
            inked(&ctx, Path(roundedRect: c.insetBy(dx: 0, dy: -30).offsetBy(dx: 0, dy: -10), cornerRadius: 4), woodDark)
            let w = r.width, h = r.height - c.height - 10
            blobs(&ctx, [CGRect(x: r.minX, y: r.minY + h * 0.3, width: w * 0.55, height: h * 0.6),
                         CGRect(x: r.minX + w * 0.2, y: r.minY, width: w * 0.6, height: h * 0.65),
                         CGRect(x: r.minX + w * 0.45, y: r.minY + h * 0.25, width: w * 0.55, height: h * 0.65),
                         CGRect(x: r.minX + w * 0.15, y: r.minY + h * 0.45, width: w * 0.7, height: h * 0.55)], leaf, 1.1)
            for (fx, fy) in [(0.3, 0.3), (0.65, 0.42), (0.45, 0.65)] as [(CGFloat, CGFloat)] {
                ctx.fill(Path(ellipseIn: CGRect(x: r.minX + w * fx - 4, y: r.minY + h * fy - 4, width: 8, height: 8)), with: .color(flagRed))
            }
        case .boat:
            let c = prop.cover
            let hull: [Color] = [Color(red: 0.90, green: 0.48, blue: 0.38), Color(red: 0.42, green: 0.60, blue: 0.88), Color(red: 0.98, green: 0.80, blue: 0.36)]
            if prop.tint != 1 {                                                    // a mast with a furled sail and a pennant
                line(&ctx, [CGPoint(x: c.midX, y: c.minY + 4), CGPoint(x: c.midX, y: r.minY + 4)], woodDark, 3)
                inked(&ctx, Path(roundedRect: CGRect(x: c.midX - 4, y: r.minY + 18, width: 8, height: c.minY - r.minY - 26), cornerRadius: 4), Color(red: 0.98, green: 0.95, blue: 0.88), 1.4)
                var pen = Path(); pen.move(to: CGPoint(x: c.midX + 1, y: r.minY + 4)); pen.addLine(to: CGPoint(x: c.midX + 18, y: r.minY + 9)); pen.addLine(to: CGPoint(x: c.midX + 1, y: r.minY + 14)); pen.closeSubpath()
                inked(&ctx, pen, flagGold, 1.2)
            }
            var b = Path(); b.move(to: CGPoint(x: c.minX, y: c.minY))
            b.addLine(to: CGPoint(x: c.maxX, y: c.minY - 4))
            b.addQuadCurve(to: CGPoint(x: c.maxX - c.width * 0.18, y: c.maxY), control: CGPoint(x: c.maxX - 2, y: c.maxY - 6))
            b.addLine(to: CGPoint(x: c.minX + c.width * 0.14, y: c.maxY))
            b.addQuadCurve(to: CGPoint(x: c.minX, y: c.minY), control: CGPoint(x: c.minX + 2, y: c.maxY - 8))
            b.closeSubpath()
            inked(&ctx, b, hull[prop.tint % 3])
            ctx.drawLayer { l in
                l.clip(to: b)
                l.fill(Path(CGRect(x: c.minX, y: c.minY + 6, width: c.width, height: 6)), with: .color(.white.opacity(0.9)))
            }
            ctx.stroke(b, with: .color(FindScene.ink), style: StrokeStyle(lineWidth: 2.2, lineJoin: .round))
            line(&ctx, [CGPoint(x: c.minX - 2, y: c.minY), CGPoint(x: c.maxX + 2, y: c.minY - 4)], woodDark, 3.4)
            for x in [c.minX + c.width * 0.3, c.minX + c.width * 0.62] { ctx.fill(Path(ellipseIn: CGRect(x: x, y: c.minY + 20, width: 7, height: 7)), with: .color(FindScene.ink.opacity(0.55))) }
            for dx in [10.0, 40, 80] as [CGFloat] {
                var w = Path(); let x = c.minX + dx, y = c.maxY + 3
                w.move(to: CGPoint(x: x, y: y)); w.addQuadCurve(to: CGPoint(x: x + 16, y: y), control: CGPoint(x: x + 8, y: y + 4))
                ctx.stroke(w, with: .color(.white.opacity(0.8)), style: StrokeStyle(lineWidth: 2, lineCap: .round))
            }
        case .net:
            var m = Path(); m.move(to: CGPoint(x: r.minX, y: r.maxY))
            m.addQuadCurve(to: CGPoint(x: r.midX, y: r.minY), control: CGPoint(x: r.minX + 10, y: r.minY + 4))
            m.addQuadCurve(to: CGPoint(x: r.maxX, y: r.maxY), control: CGPoint(x: r.maxX - 8, y: r.minY + 6))
            m.closeSubpath()
            inked(&ctx, m, Color(red: 0.56, green: 0.64, blue: 0.50))
            ctx.drawLayer { l in
                l.clip(to: m)
                for k in 0..<14 {
                    let x = r.minX - 20 + CGFloat(k) * 12
                    l.stroke(Path { p in p.move(to: CGPoint(x: x, y: r.maxY)); p.addLine(to: CGPoint(x: x + 40, y: r.minY)) }, with: .color(FindScene.ink.opacity(0.35)), lineWidth: 1.1)
                    l.stroke(Path { p in p.move(to: CGPoint(x: x + 40, y: r.maxY)); p.addLine(to: CGPoint(x: x, y: r.minY)) }, with: .color(FindScene.ink.opacity(0.35)), lineWidth: 1.1)
                }
            }
            for (fx, fy) in [(0.3, 0.55), (0.62, 0.4), (0.8, 0.75)] as [(CGFloat, CGFloat)] {
                inked(&ctx, Path(ellipseIn: CGRect(x: r.minX + r.width * fx - 5, y: r.minY + r.height * fy - 4, width: 10, height: 8)), Color(red: 0.98, green: 0.62, blue: 0.30), 1.4)
            }
        case .pots:
            for k in 0..<2 {
                let b = CGRect(x: r.minX + CGFloat(k) * r.width / 2 + 2, y: r.minY + CGFloat(1 - k) * 6, width: r.width / 2 - 4, height: r.height - CGFloat(1 - k) * 6)
                inked(&ctx, Path(roundedRect: b, cornerRadius: 10), Color(red: 0.86, green: 0.72, blue: 0.52))
                for f in [0.3, 0.5, 0.7] as [CGFloat] { line(&ctx, [CGPoint(x: b.minX + b.width * f, y: b.minY + 3), CGPoint(x: b.minX + b.width * f, y: b.maxY - 3)], woodDark.opacity(0.7), 1.6) }
                line(&ctx, [CGPoint(x: b.minX + 3, y: b.midY), CGPoint(x: b.maxX - 3, y: b.midY)], woodDark.opacity(0.7), 1.6)
            }
        case .fishCrate:
            for k in 0..<3 { fish(&ctx, CGPoint(x: r.minX + 14 + CGFloat(k) * (r.width - 28) / 2, y: r.minY + 4), k % 2 == 1) }
            let b = CGRect(x: r.minX, y: r.minY + 6, width: r.width, height: r.height - 6)
            inked(&ctx, Path(roundedRect: b, cornerRadius: 3), Color(red: 0.62, green: 0.76, blue: 0.86))
            for f in [0.33, 0.66] as [CGFloat] { line(&ctx, [CGPoint(x: b.minX + 3, y: b.minY + b.height * f), CGPoint(x: b.maxX - 3, y: b.minY + b.height * f)], FindScene.ink.opacity(0.35), 1.5) }
        case .snowman:
            let w = r.width, h = r.height
            let snowW = Color(red: 0.99, green: 0.99, blue: 1.0)
            inked(&ctx, Path(ellipseIn: CGRect(x: r.minX, y: r.minY + h * 0.5, width: w, height: h * 0.5)), snowW)
            inked(&ctx, Path(ellipseIn: CGRect(x: r.minX + w * 0.16, y: r.minY + h * 0.3, width: w * 0.68, height: h * 0.3)), snowW)
            line(&ctx, [CGPoint(x: r.minX + w * 0.18, y: r.minY + h * 0.42), CGPoint(x: r.minX - w * 0.12, y: r.minY + h * 0.3)], woodDark, 2.4)
            line(&ctx, [CGPoint(x: r.maxX - w * 0.18, y: r.minY + h * 0.42), CGPoint(x: r.maxX + w * 0.12, y: r.minY + h * 0.28)], woodDark, 2.4)
            inked(&ctx, Path(ellipseIn: CGRect(x: r.minX + w * 0.25, y: r.minY + h * 0.12, width: w * 0.5, height: h * 0.24)), snowW)
            inked(&ctx, Path(roundedRect: CGRect(x: r.minX + w * 0.2, y: r.minY + h * 0.32, width: w * 0.6, height: 6), cornerRadius: 3), flagRed, 1.6)
            inked(&ctx, Path(roundedRect: CGRect(x: r.minX + w * 0.56, y: r.minY + h * 0.33, width: 7, height: 16), cornerRadius: 3), flagRed, 1.6)
            inked(&ctx, Path(roundedRect: CGRect(x: r.minX + w * 0.3, y: r.minY + h * 0.1, width: w * 0.4, height: 5), cornerRadius: 2), Color(red: 0.26, green: 0.26, blue: 0.32), 1.4)
            inked(&ctx, Path(roundedRect: CGRect(x: r.minX + w * 0.36, y: r.minY, width: w * 0.28, height: h * 0.12), cornerRadius: 2), Color(red: 0.26, green: 0.26, blue: 0.32), 1.4)
            for dx in [0.42, 0.58] as [CGFloat] { ctx.fill(Path(ellipseIn: CGRect(x: r.minX + w * dx - 2, y: r.minY + h * 0.2, width: 4, height: 4)), with: .color(FindScene.ink)) }
            var nose = Path(); nose.move(to: CGPoint(x: r.midX, y: r.minY + h * 0.24)); nose.addLine(to: CGPoint(x: r.midX + 12, y: r.minY + h * 0.26)); nose.addLine(to: CGPoint(x: r.midX, y: r.minY + h * 0.28)); nose.closeSubpath()
            inked(&ctx, nose, Color(red: 0.98, green: 0.58, blue: 0.24), 1.2)
            for k in 0..<3 { ctx.fill(Path(ellipseIn: CGRect(x: r.midX - 2, y: r.minY + h * (0.38 + CGFloat(k) * 0.07), width: 4, height: 4)), with: .color(FindScene.ink)) }
        case .sled:
            let c = prop.cover
            var run = Path(); run.move(to: CGPoint(x: c.minX + 2, y: c.maxY - 3)); run.addLine(to: CGPoint(x: c.maxX - 10, y: c.maxY - 3))
            run.addQuadCurve(to: CGPoint(x: c.maxX, y: c.maxY - 16), control: CGPoint(x: c.maxX + 2, y: c.maxY - 4))
            ctx.stroke(run, with: .color(FindScene.ink), style: StrokeStyle(lineWidth: 3, lineCap: .round))
            for x in [c.minX + 18, c.maxX - 30] { line(&ctx, [CGPoint(x: x, y: c.maxY - 3), CGPoint(x: x, y: c.minY + 16)], FindScene.ink, 2.4) }
            inked(&ctx, Path(roundedRect: CGRect(x: c.minX, y: c.minY + 4, width: c.width - 6, height: c.height - 14), cornerRadius: 5), prop.tint == 0 ? flagRed : Color(red: 0.42, green: 0.60, blue: 0.88))
            for f in [0.33, 0.66] as [CGFloat] { line(&ctx, [CGPoint(x: c.minX + (c.width - 6) * f, y: c.minY + 7), CGPoint(x: c.minX + (c.width - 6) * f, y: c.maxY - 13)], .white.opacity(0.5), 2) }
            var rope = Path(); rope.move(to: CGPoint(x: c.maxX - 6, y: c.minY + 8)); rope.addQuadCurve(to: CGPoint(x: c.maxX + 4, y: c.maxY - 2), control: CGPoint(x: c.maxX + 12, y: c.minY + 4))
            ctx.stroke(rope, with: .color(woodDark), lineWidth: 1.6)
        case .drift:
            let w = r.width, h = r.height
            blobs(&ctx, [CGRect(x: r.minX, y: r.minY + h * 0.35, width: w * 0.5, height: h * 0.65),
                         CGRect(x: r.minX + w * 0.22, y: r.minY, width: w * 0.52, height: h),
                         CGRect(x: r.minX + w * 0.5, y: r.minY + h * 0.22, width: w * 0.5, height: h * 0.78)], Color(red: 0.98, green: 0.99, blue: 1.0), 1.1, alpha: 0.55)
            ctx.fill(Path(ellipseIn: CGRect(x: r.minX + w * 0.1, y: r.maxY - h * 0.3, width: w * 0.8, height: h * 0.28)), with: .color(Color(red: 0.86, green: 0.91, blue: 0.98).opacity(0.8)))
            for (fx, fy) in [(0.34, 0.3), (0.66, 0.42)] as [(CGFloat, CGFloat)] { blobs(&ctx, [CGRect(x: r.minX + w * fx - 3, y: r.minY + h * fy - 3, width: 6, height: 6)], flagRed, 0.6) }
        case .woodpile:
            let rad: CGFloat = 9
            for row in 0..<3 {
                let n = 4 - row
                for k in 0..<n {
                    let cx = r.minX + rad + 4 + CGFloat(row) * rad + CGFloat(k) * rad * 2.1, cy = r.maxY - rad - CGFloat(row) * rad * 1.7
                    inked(&ctx, Path(ellipseIn: CGRect(x: cx - rad, y: cy - rad, width: rad * 2, height: rad * 2)), Color(red: 0.86, green: 0.68, blue: 0.48), 1.6)
                    ctx.stroke(Path(ellipseIn: CGRect(x: cx - 4, y: cy - 4, width: 8, height: 8)), with: .color(woodDark.opacity(0.7)), lineWidth: 1.2)
                }
            }
            blobs(&ctx, [CGRect(x: r.minX + 26, y: r.minY - 2, width: 40, height: 10)], .white, 0.8)
        case .pine:
            let c = prop.cover
            inked(&ctx, Path(CGRect(x: c.minX, y: c.minY - 8, width: c.width, height: c.height + 8)), woodDark)
            for (k, f) in [(0, 1.0), (1, 0.8), (2, 0.58)] as [(Int, CGFloat)] {
                let base = c.minY - 4 - CGFloat(k) * (r.height - c.height) * 0.27, half = r.width / 2 * f
                var t = Path(); t.move(to: CGPoint(x: r.midX - half, y: base)); t.addLine(to: CGPoint(x: r.midX, y: base - (r.height - c.height) * 0.44))
                t.addLine(to: CGPoint(x: r.midX + half, y: base)); t.closeSubpath()
                inked(&ctx, t, Color(red: 0.32, green: 0.56, blue: 0.44))
                var sn = Path(); sn.move(to: CGPoint(x: r.midX - half * 0.8, y: base - 3))
                sn.addQuadCurve(to: CGPoint(x: r.midX + half * 0.8, y: base - 3), control: CGPoint(x: r.midX, y: base - 12))
                ctx.stroke(sn, with: .color(.white), style: StrokeStyle(lineWidth: 4, lineCap: .round))
            }
        case .cart:
            let c = prop.cover
            inked(&ctx, Path(roundedRect: c, cornerRadius: 4), Color(red: 0.70, green: 0.48, blue: 0.70))
            for k in 1..<4 { line(&ctx, [CGPoint(x: c.minX + 3, y: c.minY + CGFloat(k) * 10), CGPoint(x: c.maxX - 3, y: c.minY + CGFloat(k) * 10)], FindScene.ink.opacity(0.3), 1.3) }
            line(&ctx, [CGPoint(x: c.maxX - 2, y: c.minY + 14), CGPoint(x: c.maxX + 4, y: c.minY + 4)], woodDark, 3)
            for x in [c.minX + 20, c.maxX - 20] {
                inked(&ctx, Path(ellipseIn: CGRect(x: x - 12, y: c.maxY - 10, width: 24, height: 24)), wood)
                ctx.fill(Path(ellipseIn: CGRect(x: x - 3, y: c.maxY - 1, width: 6, height: 6)), with: .color(woodDark))
            }
        }
    }
}

/// A little silver fish lying on its side.
private func fish(_ ctx: inout GraphicsContext, _ at: CGPoint, _ flip: Bool) {
    var c = ctx
    c.translateBy(x: at.x, y: at.y)
    if flip { c.scaleBy(x: -1, y: 1) }
    var tail = Path(); tail.move(to: CGPoint(x: 7, y: 0)); tail.addLine(to: CGPoint(x: 13, y: -4)); tail.addLine(to: CGPoint(x: 13, y: 4)); tail.closeSubpath()
    inked(&c, tail, Color(red: 0.62, green: 0.72, blue: 0.84), 1.2)
    inked(&c, Path(ellipseIn: CGRect(x: -10, y: -4.5, width: 19, height: 9)), Color(red: 0.74, green: 0.82, blue: 0.90), 1.2)
    c.fill(Path(ellipseIn: CGRect(x: -7, y: -2, width: 2.6, height: 2.6)), with: .color(FindScene.ink))
}

/// The kingdom at night: what still glows above the darkness — stars, the moon, lit windows, lanterns.
struct FindNightLights: View {
    var body: some View {
        Canvas { ctx, _ in
            var h: UInt64 = 3
            for _ in 0..<60 {
                h = h &* 6364136223846793005 &+ 1442695040888963407
                let x = CGFloat((h >> 33) % 1000) / 1000 * 1100, y = CGFloat((h >> 13) % 1000) / 1000 * 230 + 6, r = CGFloat(1 + Double(h % 3) * 0.8)
                var st = Path()
                st.move(to: CGPoint(x: x, y: y - r * 2)); st.addQuadCurve(to: CGPoint(x: x + r * 2, y: y), control: CGPoint(x: x, y: y))
                st.addQuadCurve(to: CGPoint(x: x, y: y + r * 2), control: CGPoint(x: x, y: y)); st.addQuadCurve(to: CGPoint(x: x - r * 2, y: y), control: CGPoint(x: x, y: y))
                st.addQuadCurve(to: CGPoint(x: x, y: y - r * 2), control: CGPoint(x: x, y: y))
                ctx.fill(st, with: .color(Color(red: 1, green: 0.97, blue: 0.85).opacity(0.9)))
            }
            glow(&ctx, CGPoint(x: 968, y: 84), 70, 0.25)
            inked(&ctx, Path(ellipseIn: CGRect(x: 940, y: 56, width: 56, height: 56)), Color(red: 1, green: 0.97, blue: 0.84), 2, alpha: 0.4)
            for (x, y, r) in [(956.0, 72.0, 7.0), (976, 92, 5), (962, 96, 3.5)] as [(CGFloat, CGFloat, CGFloat)] {
                ctx.fill(Path(ellipseIn: CGRect(x: x, y: y, width: r * 2, height: r * 2)), with: .color(Color(red: 0.92, green: 0.88, blue: 0.74)))
            }
            let warm = Color(red: 1, green: 0.84, blue: 0.42)
            for hs in FindScene.houses {
                for x in [hs.wall.minX + 12, hs.wall.maxX - 32] {
                    let w = CGRect(x: x, y: hs.wall.minY + 12, width: 20, height: 18)
                    glow(&ctx, CGPoint(x: w.midX, y: w.midY), 26, 0.35)
                    inked(&ctx, Path(roundedRect: w, cornerRadius: 2), warm, 1.8)
                    line(&ctx, [CGPoint(x: x + 10, y: w.minY), CGPoint(x: x + 10, y: w.maxY)], FindScene.ink.opacity(0.7), 1.4)
                    line(&ctx, [CGPoint(x: x, y: w.midY), CGPoint(x: x + 20, y: w.midY)], FindScene.ink.opacity(0.7), 1.4)
                }
            }
            for (x, y) in [(404.0, 152.0), (682, 152), (543, 130), (526, 150), (560, 150)] as [(CGFloat, CGFloat)] {
                glow(&ctx, CGPoint(x: x + 7, y: y + 12), 22, 0.35)
                var p = Path(); p.move(to: CGPoint(x: x, y: y + 22)); p.addLine(to: CGPoint(x: x, y: y + 7))
                p.addQuadCurve(to: CGPoint(x: x + 14, y: y + 7), control: CGPoint(x: x + 7, y: y - 4)); p.addLine(to: CGPoint(x: x + 14, y: y + 22)); p.closeSubpath()
                inked(&ctx, p, warm, 1.8)
            }
            for (x, y) in [(452.0, 418.0), (640, 418)] as [(CGFloat, CGFloat)] {          // lamp posts
                glow(&ctx, CGPoint(x: x, y: y - 56), 46, 0.4)
                inked(&ctx, Path(roundedRect: CGRect(x: x - 7, y: y - 64, width: 14, height: 16), cornerRadius: 3), warm, 1.6)
            }
            for x in [812.0, 924, 1036] as [CGFloat] {                                     // stall lanterns
                glow(&ctx, CGPoint(x: x, y: 416), 30, 0.4)
                line(&ctx, [CGPoint(x: x, y: 404), CGPoint(x: x, y: 410)], FindScene.ink, 1.4)
                inked(&ctx, Path(ellipseIn: CGRect(x: x - 6, y: 409, width: 12, height: 14)), Color(red: 1, green: 0.62, blue: 0.40), 1.4)
            }
        }
        .frame(width: FindScene.size.width, height: FindScene.size.height)
        .allowsHitTesting(false)
    }
    private func glow(_ ctx: inout GraphicsContext, _ c: CGPoint, _ r: CGFloat, _ a: Double) {
        ctx.fill(Path(ellipseIn: CGRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2)),
                 with: .radialGradient(Gradient(colors: [Color(red: 1, green: 0.85, blue: 0.5).opacity(a), .clear]), center: c, startRadius: 0, endRadius: r))
    }
}

// MARK: — fair hiding (owner 13 Oct 2026, playing as Snow: the whole cat sat behind a snowman — "i cant even see its
// ears or toes". Blending into the scene is fine; being covered is not.)

/// Every placement of YOUR cat (and of its lookalikes) must be findable: the face and ears show above whatever it hides
/// behind, enough of the body shows, and it never hides behind a snowman. A white cat in white snow is fair game.
@MainActor enum FindFair {
    /// Box y (1×, the 46×62 character box) below which the eyes, nose and mouth all sit.
    static let faceLine: CGFloat = 28

    /// Does the face clear whatever the cat hides behind?
    static func faceShows(_ cat: FindCat) -> Bool {
        guard let top = cat.top else { return true }
        return top >= cat.feet.y + (faceLine - 49) * cat.scale
    }
    /// How much of the cat's height shows.
    static func shown(_ cat: FindCat) -> CGFloat { cat.visible.height / max(1, cat.rect.height) }

    /// The whole rule.
    static func fair(_ cat: FindCat, _ place: FindPlace, minShow: CGFloat) -> Bool {
        faceShows(cat) && shown(cat) >= minShow - 0.001
            && !FindScene.props(place).contains { $0.kind == .snowman && $0.cover == cat.cover }
    }
}
