import AppKit
import SwiftUI

// The lookalike visibility self-check (`Cataholic --check-odds`, run by scripts/release.sh before every release).
// Find my cat's lookalikes are "your cat with one detail wrong" — but a detail can be wrong and still invisible (13 Oct 2026:
// Patches' ink spot landed inside her dark ear patch). So, for every coat × every FindOdd, draw the real cat and the
// lookalike the way the game does, at the smallest size a cat ever appears, and count the pixels that differ noticeably.
// Rule: a pixel differs when any channel is off by more than 40/255; a cell passes with at least 5 such pixels.
// Why 5: measured at that size the faintest details the game has always had are the tail tip (6 px) and the spot on most coats
// (9–10); the one that was invisible read 2. Five sits between "as faint as the tail tip" and "not there".
// For an odd that showsFromAbove only the head (character box y < 26) is counted — all a peeking cat shows.

@MainActor enum FindOddsCheck {
    /// The smallest cat in the game: level 5 and endless `mine` (0.46) × the smallest lookalike jitter (0.98) × a rooftop /
    /// pier cat's perspective (0.92) — at 1× zoom, one point per pixel (the camera never zooms out below 1×).
    static let scale: CGFloat = 0.46 * 0.98 * 0.92
    static let channelStep = 40, minPixels = 5
    private static let W = 48, H = 64

    /// RGBA pixels of one cat on a flat grey ground, drawn as FindCatView draws it.
    private static func pixels(_ cat: FindCat) -> [UInt8] {
        let view = findCharacter(cat)
            .frame(width: 46 * CrewLayout.zoom, height: 62 * CrewLayout.zoom)
            .scaleEffect(scale / CrewLayout.zoom)
            .frame(width: 46 * scale, height: 62 * scale)
            .frame(width: CGFloat(W), height: CGFloat(H))
            .background(Color(red: 0.5, green: 0.5, blue: 0.5))
        let r = ImageRenderer(content: view)
        r.scale = 1
        var px = [UInt8](repeating: 0, count: W * H * 4)
        guard let img = r.cgImage, let ctx = CGContext(data: &px, width: W, height: H, bitsPerComponent: 8, bytesPerRow: W * 4,
                                                       space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                                       bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return px }
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: W, height: H))
        return px
    }

    /// How many pixels of the lookalike differ noticeably from the real cat (head only when `headOnly`).
    static func differing(_ coat: CatCoat, _ odd: FindOdd) -> Int {
        let real = FindCat(id: 0, coat: coat, state: .look, feet: .zero, scale: 1, flip: false)
        var twin = FindCat(id: 1, coat: FindGame.variant(of: coat, odd), state: .look, feet: .zero, scale: 1, flip: false)
        twin.odd = odd
        let a = pixels(real), b = pixels(twin)
        let top = (CGFloat(H) - 62 * scale) / 2
        var n = 0
        for y in 0..<H {
            if odd.showsFromAbove && (CGFloat(y) + 0.5 - top) / scale >= 26 { continue }
            for x in 0..<W {
                let i = (y * W + x) * 4
                if (0..<3).contains(where: { abs(Int(a[i + $0]) - Int(b[i + $0])) > channelStep }) { n += 1 }
            }
        }
        return n
    }

    static func run() -> Int32 {
        print(String(format: "Find my cat lookalike check — every coat × every odd at the smallest size (scale %.3f ≈ %.0f × %.0f px); pass = ≥ %d pixels differ by > %d/255",
                     scale, 46 * scale, 62 * scale, minPixels, channelStep))
        let name = { (o: FindOdd) in "\(o)" }
        print("coat      " + FindOdd.allCases.map { name($0).padding(toLength: 8, withPad: " ", startingAt: 0) }.joined())
        var bad: [String] = []
        for coat in CatCoat.all {
            var row = coat.id.padding(toLength: 10, withPad: " ", startingAt: 0)
            for odd in FindOdd.allCases {
                let n = differing(coat, odd)
                row += String(n).padding(toLength: 8, withPad: " ", startingAt: 0)
                if n < minPixels { bad.append("\(coat.id)/\(odd) (\(n))") }
            }
            print(row)
        }
        if !bad.isEmpty { print("FAIL — too faint to spot: " + bad.joined(separator: ", ")); return 1 }
        print("PASS — every lookalike's wrong detail shows")
        return 0
    }
}
