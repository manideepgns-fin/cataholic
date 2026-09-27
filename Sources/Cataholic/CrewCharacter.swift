import SwiftUI

// CrewCharacter — the character skin in ONE file (mock CREW-MOCK.html §chibi):
// pin + short string, round head, hair, eyes (open/shut), cheeks, small body
// with a glyph ("₹" for Billing). A sign with the count for needs (held on
// the LEFT — the right half hangs off-screen), a book for working, shut eyes
// + breathing + a drifting "z" for sleeping, shut eyes for reading.
// The owner may swap to cats later: the skin is ONE self-contained view
// taking (state, badge, colors) — CrewView owns pose/motion, never the drawing.
struct CrewSkinColors {
    var hair: Color = Color(red: 0.23, green: 0.18, blue: 0.29)
    var body: Color = Color(red: 0.79, green: 0.64, blue: 0.42)
    var glyph: String = "♥"
}

enum CrewSkinState { case needs, working, sleeping, reading, running, jump, hi, big, stretch, yawn, groom, look, clingy, alert, nap }

struct CrewCharacter: View {
    var state: CrewSkinState
    var badge: String          // needs count, e.g. "3" — empty hides the sign number
    var colors: CrewSkinColors
    var blinkShut = false      // working blink end state (posed by the parent)
    var breatheY: CGFloat = 0  // sleeping breathing offset
    var zLift: (CGFloat, Double) = (0, 0)  // (rise, opacity) for the drifting z
    var runPhase: Double = 0   // running: gallop phase in radians (0 = standing)
    // Life layer (cat-life, 26 Sep 2026): posed by CrewView's TimelineView.
    var breathe: CGFloat = 1        // body scale ~1.0–1.015 on a 3–4 s sine
    var tailSway: CGFloat = 0       // slow tail sway offset (pt)
    var earTwitch: Double = 0       // 0 = still, 1 = left ear flicked
    var pupil: CGSize = .zero       // look-at-pointer offset, clamped ≤ 1.5 pt
    var eyeHalf = false             // clingy: eyes half-closed
    var headTurn: CGFloat = 0       // look-around: head-feature shift (pt)
    var headTilt: CGFloat = 0       // look-around / clingy: head tilt (deg)
    var jumpCrouch = false          // jump take-off: crouched low before the leap
    var bodyTilt: CGFloat = 0       // clingy lean toward the pointer (deg)
    var pin = true                  // the pin + string over the head (the ring takes it off)
    var happy = false               // petted: "^ ^" eyes + rosy cheeks
    var stride: Double = 38         // leg swing (deg) on the side-on walker: 38 = gallop, ~22 = walk
    var coatOverride: CatCoat? = nil  // snapshots draw every coat side by side; live = the picked coat

    // The cat (owner 25 Sep 2026: "and cat please") — a ginger tabby, original drawing.
    // The coat (owner 27 Sep 2026: "3 4 styles of cats … we are officially cataholic"): the soft, lazy flat-vector
    // look — warm fur, thin brown line, cream muzzle — in four coats picked from the right-click menu (CatCoat).
    private var coat: CatCoat { coatOverride ?? CatCoat.current }
    private var fur: Color { coat.fur }
    private var stripe: Color { coat.stripe }          // tabby markings; .clear on untabby coats (never paints over a spot)
    private var shade: Color { coat.shade }            // the far legs / far ear, a step darker than the fur
    private var cream: Color { coat.cream }
    private let pink = Color(red: 0.97, green: 0.65, blue: 0.63)
    private let collar = Color(red: 0.33, green: 0.44, blue: 0.84)
    private let gold = Color(red: 0.93, green: 0.76, blue: 0.34)
    private var eye: Color { coat.eye }
    private let paper = Color(red: 1.0, green: 0.98, blue: 0.94)
    private let amber = Color(red: 0.91, green: 0.69, blue: 0.29)

    var body: some View {
        if state == .running { runner } else { hanging }
    }
    /// Zoomies (owner 25 Sep 2026: "like orange cat going all over the place") — the same tabby seen
    /// from the SIDE, facing right (the parent mirrors it to run left): off the pin, legs galloping
    /// on `runPhase`, tail streaming behind, ears back.
    private var runner: some View {
        Canvas { ctx, size in
            let s = size.width / 46
            func P(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x * s, y: y * s) }
            func oval(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> Path {
                Path(ellipseIn: CGRect(x: x * s, y: y * s, width: w * s, height: h * s))
            }
            // Chunky (owner 27 Sep 2026: "the chunky structure is not visible" in zoomies): a round barrel of
            // a body on short stubby legs, a big round head overlapping it. A walk is a diagonal gait with
            // the tail up; a gallop bounds, tail streaming, the body stretching and squashing each stride.
            let walk = stride < 30
            let swing = sin(runPhase) * stride, swing2 = sin(runPhase + (walk ? .pi : 0.7)) * stride
            func leg(_ hip: CGPoint, _ deg: Double, _ c: Color) {
                let a = deg * .pi / 180, L: CGFloat = 5.5
                let foot = CGPoint(x: hip.x + CGFloat(sin(a)) * L, y: hip.y + CGFloat(cos(a)) * L)
                var p = Path(); p.move(to: P(hip.x, hip.y)); p.addLine(to: P(foot.x, foot.y))
                T(&ctx, p, 5.6, c, s)
                F(&ctx, oval(foot.x - 2.6, foot.y - 1.4, 5.2, 3), cream, s)
            }
            let bob = CGFloat(abs(sin(runPhase))) * (walk ? 0.8 : 1.4)
            ctx.translateBy(x: 0, y: -bob * s)
            if !walk && runPhase != 0 {               // gallop: stretch on the reach, squash on the gather
                let k = CGFloat(sin(runPhase * 2)) * 0.06
                ctx.translateBy(x: P(20, 48).x, y: P(20, 48).y)
                ctx.scaleBy(x: 1 + k, y: 1 - k)
                ctx.translateBy(x: -P(20, 48).x, y: -P(20, 48).y)
            }
            let back = CGPoint(x: 11, y: 42.5), front = CGPoint(x: 27, y: 42.5)
            leg(back, swing2, shade); leg(front, -swing2, shade)          // far legs, in shade
            var tail = Path()
            tail.move(to: P(6, 33))
            if walk {                                   // tail up, a question-mark hook at the tip
                tail.addQuadCurve(to: P(3.5 + CGFloat(sin(runPhase * 0.5)) * 1.2, 18), control: P(1.5, 29))
                tail.addQuadCurve(to: P(7.5, 15.5), control: P(4.5, 14))
            } else {
                tail.addQuadCurve(to: P(1.6, 24 + CGFloat(sin(runPhase)) * 2.5), control: P(0.5, 33))
            }
            T(&ctx, tail, 4.6, fur, s)
            F(&ctx, oval(3, 23, 33, 24), fur, s)                            // the barrel
            ctx.fill(oval(8.5, 38.5, 22, 7.5), with: .color(cream))         // belly
            for (x, y) in [(11.5, 24.2), (16.5, 23.4), (21.5, 23.6)] as [(CGFloat, CGFloat)] {
                var st = Path(); st.move(to: P(x, y)); st.addQuadCurve(to: P(x - 1.2, y + 5), control: P(x + 0.6, y + 2.8))
                ctx.stroke(st, with: .color(stripe), style: StrokeStyle(lineWidth: 1.8 * s, lineCap: .round))
            }
            leg(back, swing, fur); leg(front, -swing, fur)                  // near legs over the body
            // Head: ears (far one shaded), a big round head, muzzle
            var earB = Path(); earB.move(to: P(25.5, 18)); earB.addQuadCurve(to: P(27.5, 8), control: P(24.5, 11)); earB.addQuadCurve(to: P(32.5, 14), control: P(30.5, 9.5)); earB.closeSubpath()
            F(&ctx, earB, shade, s)
            var earF = Path(); earF.move(to: P(32, 15)); earF.addQuadCurve(to: P(36, 6.5), control: P(32, 9)); earF.addQuadCurve(to: P(41, 14.5), control: P(39.5, 8.5)); earF.closeSubpath()
            F(&ctx, earF, fur, s)
            var inF = Path(); inF.move(to: P(34, 13.5)); inF.addLine(to: P(36, 9)); inF.addLine(to: P(38.8, 13.5)); inF.closeSubpath()
            ctx.fill(inF, with: .color(pink))
            F(&ctx, oval(22.5, 12.5, 21.5, 20.5), fur, s)
            for (x, y) in [(28.5, 13.6), (31.5, 13.2)] as [(CGFloat, CGFloat)] {
                var st = Path(); st.move(to: P(x, y)); st.addLine(to: P(x + 0.4, y + 3.2))
                ctx.stroke(st, with: .color(stripe), style: StrokeStyle(lineWidth: 1.5 * s, lineCap: .round))
            }
            ctx.fill(oval(34.5, 22.5, 9, 7.5), with: .color(cream))
            ctx.fill(oval(41.6, 23, 2.4, 1.9), with: .color(pink))                         // nose
            var mouth = Path(); mouth.move(to: P(42.6, 25.2)); mouth.addQuadCurve(to: P(39.8, 27), control: P(42.4, 27.2))
            ctx.stroke(mouth, with: .color(eye), lineWidth: 0.8 * s)
            var wh = Path()
            wh.move(to: P(39.5, 25)); wh.addLine(to: P(45.5, 23.6))
            wh.move(to: P(39.5, 26.2)); wh.addLine(to: P(45.5, 27.4))
            ctx.stroke(wh, with: .color(eye.opacity(0.45)), lineWidth: 0.6 * s)
            ctx.fill(oval(30.5, 24, 3.8, 2.1), with: .color(pink.opacity(0.55)))           // cheek
            if walk || runPhase == 0 {                  // a walk (or standing): calm round eye
                ctx.fill(oval(35.2, 17, 3.8, 4.6), with: .color(eye))
                ctx.fill(oval(36.4, 17.7, 1.5, 1.5), with: .color(.white))
            } else {                                    // zoomies: huge eye, it is having the time of its life
                ctx.fill(oval(34.8, 16.2, 4.6, 5.6), with: .color(eye))
                ctx.fill(oval(36.1, 16.9, 1.8, 1.8), with: .color(.white))
                ctx.fill(oval(37.6, 20, 0.9, 0.9), with: .color(.white))
            }
            if coat.glasses {                           // side-on reading glasses: one round lens and its arm
                ctx.stroke(oval(33.4, 15.4, 7.6, 7.6), with: .color(line), lineWidth: 1.1 * s)
                var arm = Path(); arm.move(to: P(33.5, 18.6)); arm.addLine(to: P(28.5, 17.4))
                ctx.stroke(arm, with: .color(line), style: StrokeStyle(lineWidth: 1.1 * s, lineCap: .round))
            }
            var col = Path(); col.move(to: P(27, 30)); col.addLine(to: P(29.6, 35.5))
            ctx.stroke(col, with: .color(collar), style: StrokeStyle(lineWidth: 2.4 * s, lineCap: .round))
            ctx.fill(oval(28.2, 34, 3.6, 3.6), with: .color(gold))
        }
        .frame(width: 46 * CrewLayout.zoom, height: 62 * CrewLayout.zoom)
    }

    private var hanging: some View {
        ZStack(alignment: .topLeading) {
            Canvas { ctx, size in
                let s = size.width / 46
                func P(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x * s, y: y * s) }
                func oval(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> Path {
                    Path(ellipseIn: CGRect(x: x * s, y: y * s, width: w * s, height: h * s))
                }
                // Side-on poses are off the pin: the leap and the stretch. The 0.2 s
                // take-off crouch before the leap is drawn below, front-on.
                if state == .jump && !jumpCrouch { leap(ctx: &ctx, P: P, oval: oval, s: s); return }
                if state == .stretch { bow(ctx: &ctx, P: P, oval: oval, s: s); return }
                if (state == .sleeping || state == .nap) && !pin { melt(ctx: &ctx, P: P, oval: oval, s: s); return }
                // Pin + string (the grown cat, the crouching cat and the ring step off it)
                if pin && state != .big && state != .jump {
                    ctx.stroke(Path { p in p.move(to: P(23, 0)); p.addLine(to: P(23, 8)) },
                               with: .color(.gray.opacity(0.8)), lineWidth: 1)
                    ctx.fill(Path(roundedRect: CGRect(x: 19 * s, y: 3 * s, width: 8 * s, height: 5 * s),
                                   cornerRadius: 1.5 * s), with: .color(Color(red: 0.72, green: 0.74, blue: 0.80)))
                }
                let crouching = state == .jump && jumpCrouch
                let leaning = state == .clingy
                // Whole-body lean for clingy: rotate the cat (not the string) ~8° toward the pointer.
                if leaning { ctx.translateBy(x: P(23, 46).x, y: P(23, 46).y); ctx.rotate(by: .degrees(bodyTilt)); ctx.translateBy(x: -P(23, 46).x, y: -P(23, 46).y) }
                // Tail — curls up behind the body (life: slow sway).
                // Alert: up with a hook at the tip.
                // Chunky (owner 27 Sep 2026: "a chunky cat"): a fat, short tail that wraps the loaf.
                var tail = Path()
                if state == .alert {
                    tail.move(to: P(31, 45)); tail.addQuadCurve(to: P(37.5 + tailSway, 32), control: P(38, 44))
                    tail.addQuadCurve(to: P(34 + tailSway, 27.5), control: P(38.5, 28))
                } else {
                    tail.move(to: P(31, 46)); tail.addQuadCurve(to: P(37.5 + tailSway, 37), control: P(38.5, 46.5))
                    tail.addQuadCurve(to: P(35 + tailSway, 32.5), control: P(38.5, 33))
                }
                T(&ctx, tail, 4.4, fur, s)
                // Body + cream belly + back paws (life: breathing scale about the base).
                // Crouch: body low, haunches up, take-off pose.
                ctx.translateBy(x: P(23, 48).x, y: P(23, 48).y)
                ctx.scaleBy(x: breathe, y: breathe)
                ctx.translateBy(x: -P(23, 48).x, y: -P(23, 48).y)
                if crouching {
                    F(&ctx, oval(11, 35.5, 24, 13.5), fur, s)    // low, coiled, round
                    ctx.fill(oval(16, 39, 14, 8), with: .color(cream))
                    F(&ctx, oval(13, 45.5, 7, 4), cream, s)
                    F(&ctx, oval(26, 45.5, 7, 4), cream, s)
                    F(&ctx, oval(25, 33.5, 10, 8), fur, s)    // haunches up
                } else {
                    // A loaf: wide round body, hips bulging at the base. Alert sits a touch taller.
                    let bh: CGFloat = state == .alert ? 23 : 21
                    let by: CGFloat = state == .alert ? 26.5 : 28.5
                    F(&ctx, oval(10.5, by, 25, bh), fur, s)
                    F(&ctx, oval(9, 37, 9, 11.5), fur, s)        // left haunch
                    F(&ctx, oval(28, 37, 9, 11.5), fur, s)       // right haunch
                    spots(&ctx, clip: oval(10.5, by, 25, bh), s, head: false)
                    ctx.fill(oval(16, 33, 14, 14.5), with: .color(cream))    // round belly
                    F(&ctx, oval(12.5, 45.2, 8, 4.3), cream, s)  // stubby paws
                    F(&ctx, oval(25.5, 45.2, 8, 4.3), cream, s)
                    var toes = Path()
                    for x in [15.2, 17.8, 28.2, 30.8] as [CGFloat] { toes.move(to: P(x, 47.6)); toes.addLine(to: P(x, 49)) }
                    ctx.stroke(toes, with: .color(line.opacity(0.35)), style: StrokeStyle(lineWidth: 0.6 * s, lineCap: .round))
                }
                // Ears (drawn before the head so the head overlaps their base; life: twitch flicks the left tip).
                // Alert: taller, forward ears (drawn INSTEAD of the base pair, not over it).
                // Crouch: ears flattened back. Look: the whole head group (ears + face)
                // shifts ~3 pt and tilts ~6° via headT below.
                let hdx = (state == .look || state == .clingy) ? headTurn : 0
                if state == .alert {
                    var alL = Path(); alL.move(to: P(11 + hdx, 17)); alL.addLine(to: P(12.5 + hdx, 3.5)); alL.addLine(to: P(19.5 + hdx, 10.5)); alL.closeSubpath()
                    var alR = Path(); alR.move(to: P(35 + hdx, 17)); alR.addLine(to: P(33.5 + hdx, 3.5)); alR.addLine(to: P(26.5 + hdx, 10.5)); alR.closeSubpath()
                    F(&ctx, alL, fur, s); F(&ctx, alR, fur, s)
                    var aiL = Path(); aiL.move(to: P(13.5 + hdx, 14)); aiL.addLine(to: P(14 + hdx, 7)); aiL.addLine(to: P(17.5 + hdx, 11)); aiL.closeSubpath()
                    var aiR = Path(); aiR.move(to: P(32.5 + hdx, 14)); aiR.addLine(to: P(32 + hdx, 7)); aiR.addLine(to: P(28.5 + hdx, 11)); aiR.closeSubpath()
                    ctx.fill(aiL, with: .color(pink)); ctx.fill(aiR, with: .color(pink))
                } else if crouching {
                    var flL = Path(); flL.move(to: P(11.5, 18)); flL.addLine(to: P(6, 12)); flL.addLine(to: P(14, 11)); flL.closeSubpath()
                    var flR = Path(); flR.move(to: P(34.5, 18)); flR.addLine(to: P(40, 12)); flR.addLine(to: P(32, 11)); flR.closeSubpath()
                    F(&ctx, flL, fur, s); F(&ctx, flR, fur, s)
                } else {
                    // Rounded, wide-set ears (a chunky head wears small soft ears).
                    var earL = Path(); earL.move(to: P(9 + hdx, 19)); earL.addQuadCurve(to: P(12 + hdx - earTwitch * 2.2, 7.5 - earTwitch * 1.6), control: P(8.5 + hdx, 11)); earL.addQuadCurve(to: P(20 + hdx, 12.5), control: P(15.5 + hdx, 8)); earL.closeSubpath()
                    var earR = Path(); earR.move(to: P(37 + hdx, 19)); earR.addQuadCurve(to: P(34 + hdx, 7.5), control: P(37.5 + hdx, 11)); earR.addQuadCurve(to: P(26 + hdx, 12.5), control: P(30.5 + hdx, 8)); earR.closeSubpath()
                    F(&ctx, earL, coat.earL ?? fur, s); F(&ctx, earR, coat.earR ?? fur, s)
                    var inL = Path(); inL.move(to: P(11.5 + hdx, 16)); inL.addLine(to: P(12.8 + hdx - earTwitch * 1.6, 10.5 - earTwitch * 1.2)); inL.addLine(to: P(17.5 + hdx, 13)); inL.closeSubpath()
                    var inR = Path(); inR.move(to: P(34.5 + hdx, 16)); inR.addLine(to: P(33.2 + hdx, 10.5)); inR.addLine(to: P(28.5 + hdx, 13)); inR.closeSubpath()
                    ctx.fill(inL, with: .color(pink)); ctx.fill(inR, with: .color(pink))
                }
                // Head group: shift ~3 pt sideways and tilt ~6° for look / clingy.
                ctx.translateBy(x: P(23 + hdx, 21).x, y: P(23 + hdx, 21).y)
                if state == .look || state == .clingy { ctx.rotate(by: .degrees(headTilt)) }
                ctx.translateBy(x: -P(23, 21).x, y: -P(23, 21).y)
                let hx: CGFloat = hdx
                // Head, forehead stripes, muzzle
                // A wide, squishy head (wider than tall).
                F(&ctx, oval(7 + hx, 10.5, 32, 21.5), fur, s)
                spots(&ctx, clip: oval(7 + hx, 10.5, 32, 21.5), s, head: true, dx: hx)
                for (x0, x1) in [(20.3, 20.8), (23, 23), (25.7, 25.2)] as [(CGFloat, CGFloat)] {
                    var st = Path(); st.move(to: P(x0 + hx, 11.6)); st.addLine(to: P(x1 + hx, 15))
                    ctx.stroke(st, with: .color(stripe), style: StrokeStyle(lineWidth: 1.5 * s, lineCap: .round))
                }
                ctx.fill(oval(16.5 + hx, 21.5, 13, 8.5), with: .color(cream))
                // Collar + gold tag carrying the agent's glyph (on the BODY — outside the head-group tilt)
                var nose = Path(); nose.move(to: P(21.8 + hx, 23)); nose.addLine(to: P(24.2 + hx, 23)); nose.addLine(to: P(23 + hx, 24.5)); nose.closeSubpath()
                ctx.fill(nose, with: .color(pink))
                if state == .yawn { yawnMouth(ctx: &ctx, P: P, s: s, dx: hx) }
                else if state == .groom {
                    // tongue peeks out mid-lick
                    ctx.fill(oval(22.1 + hx, 25.2, 1.8, 2.6), with: .color(pink))
                } else {
                    var mouth = Path()
                    mouth.move(to: P(23 + hx, 24.5)); mouth.addQuadCurve(to: P(21 + hx, 26), control: P(22.8 + hx, 26.3))
                    mouth.move(to: P(23 + hx, 24.5)); mouth.addQuadCurve(to: P(25 + hx, 26), control: P(23.2 + hx, 26.3))
                    ctx.stroke(mouth, with: .color(eye), lineWidth: 0.8 * s)
                }
                var wh = Path()
                wh.move(to: P(17.5 + hx, 24.5)); wh.addLine(to: P(5.5 + hx, 23))
                wh.move(to: P(17.5 + hx, 26)); wh.addLine(to: P(5.8 + hx, 27.4))
                wh.move(to: P(28.5 + hx, 24.5)); wh.addLine(to: P(40.5 + hx, 23))
                wh.move(to: P(28.5 + hx, 26)); wh.addLine(to: P(40.2 + hx, 27.4))
                ctx.stroke(wh, with: .color(eye.opacity(0.45)), lineWidth: 0.6 * s)
                ctx.fill(oval(11.5 + hx, 22.4, 4.4, 2.4), with: .color(pink.opacity(happy ? 0.85 : 0.5)))
                ctx.fill(oval(30.1 + hx, 22.4, 4.4, 2.4), with: .color(pink.opacity(happy ? 0.85 : 0.5)))
                if happy { happyEyes(ctx: &ctx, P: P, s: s, dx: hx) } else {
                switch state {
                case .working, .reading:   // at work over the letter: content "^ ^" eyes (owner's style pick, 27 Sep 2026)
                    happyEyes(ctx: &ctx, P: P, s: s, dx: hx)
                case .needs, .jump, .hi, .big, .look:
                    if blinkShut { shutEyes(ctx: &ctx, P: P, s: s, dx: hx) } else { openEyes(ctx: &ctx, P: P, s: s, pupil: pupil, half: false, dx: headTurn + pupil.width * 0.4) }
                case .alert:
                    // eyes WIDE: bigger whites + bigger pupils
                    openEyes(ctx: &ctx, P: P, s: s, pupil: .zero, half: false, dx: 0, wide: true)
                case .clingy:
                    openEyes(ctx: &ctx, P: P, s: s, pupil: pupil, half: true, dx: 0)
                    // purr mark: two short curved lines near the head (no text)
                    var p1 = Path(); p1.move(to: P(34 + hx, 14)); p1.addQuadCurve(to: P(36 + hx, 17), control: P(35.5 + hx, 15.5))
                    var p2 = Path(); p2.move(to: P(37 + hx, 12)); p2.addQuadCurve(to: P(39 + hx, 15), control: P(38.5 + hx, 13.5))
                    ctx.stroke(p1, with: .color(eye.opacity(0.55)), lineWidth: 0.9 * s)
                    ctx.stroke(p2, with: .color(eye.opacity(0.55)), lineWidth: 0.9 * s)
                case .sleeping, .running, .yawn, .nap, .stretch:   // stretch is drawn by bow() and never gets here
                    shutEyes(ctx: &ctx, P: P, s: s, dx: hx)
                case .groom:
                    shutEyes(ctx: &ctx, P: P, s: s, only: .left, dx: hx)
                    openEyes(ctx: &ctx, P: P, s: s, pupil: .zero, half: false, dx: 0, only: .right)
                }
                }
                if coat.glasses && state != .sleeping && state != .nap && state != .yawn { glasses(ctx: &ctx, P: P, s: s, dx: hx) }
                // Head-group transform ends here — paws, letter, sign stay on the body.
                ctx.translateBy(x: P(23, 21).x, y: P(23, 21).y)
                if state == .look || state == .clingy { ctx.rotate(by: .degrees(-headTilt)) }
                ctx.translateBy(x: -P(23 + hdx, 21).x, y: -P(23 + hdx, 21).y)
                // Collar + gold tag carrying the agent's glyph (on the body, unturned)
                ctx.fill(Path(roundedRect: CGRect(x: 14 * s, y: 30.2 * s, width: 18 * s, height: 2.8 * s),
                               cornerRadius: 1.4 * s), with: .color(collar))
                ctx.fill(oval(19.8, 31.2, 6.4, 6.4), with: .color(gold))
                let glyph = Text(colors.glyph).font(.system(size: 4.6 * s, weight: .heavy))
                    .foregroundColor(Color.black.opacity(0.55))
                ctx.draw(glyph, at: P(23, 34.5), anchor: .center)
                if state == .working || state == .reading {
                    // Holding a letter with both front paws
                    ctx.fill(Path(roundedRect: CGRect(x: 15 * s, y: 37 * s, width: 16 * s, height: 10 * s),
                                   cornerRadius: 1.5 * s), with: .color(paper))
                    var flap = Path(); flap.move(to: P(15.5, 37.5)); flap.addLine(to: P(23, 42)); flap.addLine(to: P(30.5, 37.5))
                    ctx.stroke(flap, with: .color(Color(red: 0.81, green: 0.78, blue: 0.72)), lineWidth: 0.8 * s)
                    F(&ctx, oval(11.5, 38, 6.5, 5), fur, s)      // chubby paws on the letter
                    F(&ctx, oval(28, 38, 6.5, 5), fur, s)
                }
                if state == .hi {
                    // wave: the paw lifts high
                    var paw = Path()
                    paw.move(to: P(17, 36)); paw.addQuadCurve(to: P(8.5, 22), control: P(11, 30))
                    T(&ctx, paw, 3, fur, s)
                    F(&ctx, oval(6.6, 18.5, 4.4, 3.6), cream, s)
                }
                if state == .needs {
                    // A paw holds the sign up on the LEFT (the side facing into the screen)
                    var arm = Path()
                    arm.move(to: P(17, 36)); arm.addQuadCurve(to: P(11.5, 29.5), control: P(13.5, 34.5))
                    T(&ctx, arm, 3, fur, s)
                    // x ≥ 0: a Canvas crops anything drawn outside its frame
                    let sign = CGRect(x: 0.5 * s, y: 17 * s, width: 13 * s, height: 13 * s)
                    ctx.fill(Path(roundedRect: sign, cornerRadius: 3 * s), with: .color(paper))
                    ctx.stroke(Path(roundedRect: sign, cornerRadius: 3 * s), with: .color(amber), lineWidth: 1.2 * s)
                    F(&ctx, oval(9.2, 27.5, 4.2, 3.4), cream, s)   // the paw on the sign
                    if !badge.isEmpty {
                        let shown = (Int(badge) ?? 0) > 9 ? "9+" : badge
                        let t = Text(shown).font(.system(size: 9.5 * s, weight: .bold, design: .monospaced))
                            .foregroundColor(Color(red: 0.48, green: 0.33, blue: 0.06))
                        ctx.draw(t, at: CGPoint(x: 7 * s, y: 23 * s), anchor: .center)
                    }
                }
                if state == .groom {
                    // a paw lifted to the mouth for the mid-lick
                    var paw = Path()
                    paw.move(to: P(29, 36)); paw.addQuadCurve(to: P(27.5, 27.5), control: P(29.5, 32))
                    T(&ctx, paw, 3, fur, s)
                    F(&ctx, oval(25.4, 24.4, 4.2, 3.4), cream, s)
                }
            }
            .frame(width: 46 * CrewLayout.zoom, height: 62 * CrewLayout.zoom)
            .offset(y: breatheY)
            if state == .sleeping {
                Text("z")
                    .font(.system(size: 10 * CrewLayout.zoom, weight: .semibold, design: .monospaced))
                    .foregroundStyle(Color(nsColor: .secondaryLabelColor).opacity(zLift.1))
                    .offset(x: (2 - zLift.0 * 0.8) * CrewLayout.zoom, y: (6 - zLift.0) * CrewLayout.zoom)
            }
        }
        .frame(width: 46 * CrewLayout.zoom, height: 62 * CrewLayout.zoom)
    }

    /// The sticker outline (owner 27 Sep 2026 references: every chunky cat wears a dark line — it is what
    /// makes the round shape read). A shape = outline stroke, then fill: a later shape's line lands on the
    /// earlier fill, so the head draws a chin line on the body and the haunches draw leg lines.
    private var line: Color { coat.line }
    private static let lineW: CGFloat = 1.25
    private func F(_ ctx: inout GraphicsContext, _ p: Path, _ c: Color, _ s: CGFloat) {
        ctx.stroke(p, with: .color(line), style: StrokeStyle(lineWidth: Self.lineW * s, lineJoin: .round))
        ctx.fill(p, with: .color(c))
    }
    /// A thick stroked limb/tail with the same outline around it.
    private func T(_ ctx: inout GraphicsContext, _ p: Path, _ w: CGFloat, _ c: Color, _ s: CGFloat) {
        ctx.stroke(p, with: .color(line), style: StrokeStyle(lineWidth: (w + Self.lineW) * s, lineCap: .round, lineJoin: .round))
        ctx.stroke(p, with: .color(c), style: StrokeStyle(lineWidth: w * s, lineCap: .round, lineJoin: .round))
    }

    /// Round reading glasses over both eyes (the billing cat at work), arms to the edge of the head.
    private func glasses(ctx: inout GraphicsContext, P: (CGFloat, CGFloat) -> CGPoint, s: CGFloat, dx: CGFloat) {
        var g = Path()
        for cx in [17.5, 28.5] as [CGFloat] { g.addEllipse(in: CGRect(x: (cx - 5.1 + dx) * s, y: 13.9 * s, width: 10.2 * s, height: 10.2 * s)) }
        g.move(to: P(22.6 + dx, 18.4)); g.addQuadCurve(to: P(23.4 + dx, 18.4), control: P(23 + dx, 17.6))
        g.move(to: P(12.4 + dx, 18.2)); g.addLine(to: P(8.6 + dx, 17.2))
        g.move(to: P(33.6 + dx, 18.2)); g.addLine(to: P(37.4 + dx, 17.2))
        ctx.fill(Path(ellipseIn: CGRect(x: (12.4 + dx) * s, y: 13.9 * s, width: 10.2 * s, height: 10.2 * s)), with: .color(.white.opacity(0.18)))
        ctx.fill(Path(ellipseIn: CGRect(x: (23.4 + dx) * s, y: 13.9 * s, width: 10.2 * s, height: 10.2 * s)), with: .color(.white.opacity(0.18)))
        ctx.stroke(g, with: .color(line), style: StrokeStyle(lineWidth: 1.2 * s, lineCap: .round))
    }
    /// Calico spots, painted inside a fur shape (head or body) so they follow its edge.
    private func spots(_ ctx: inout GraphicsContext, clip: Path, _ s: CGFloat, head: Bool, dx: CGFloat = 0,
                       from list: [(Color, CGRect)]? = nil) {
        let ps = (list ?? coat.patches).filter { head ? $0.1.minY < 26 : $0.1.minY >= 26 }
        guard !ps.isEmpty else { return }
        ctx.drawLayer { l in
            l.clip(to: clip)
            for (c, r) in ps {
                l.fill(Path(ellipseIn: CGRect(x: (r.minX + dx) * s, y: r.minY * s, width: r.width * s, height: r.height * s)), with: .color(c))
            }
        }
    }
    /// The lazy nap (owner's style pick, 27 Sep 2026): melted flat on its side, head resting on its paws,
    /// eyes shut, back legs stretched out behind. Standing cats nap like this; a cat on its pin naps sitting.
    private func melt(ctx: inout GraphicsContext, P: (CGFloat, CGFloat) -> CGPoint,
                      oval: (CGFloat, CGFloat, CGFloat, CGFloat) -> Path, s: CGFloat) {
        let breathe = CGFloat(breatheY) * 0.15
        var tail = Path(); tail.move(to: P(38.5, 46.5)); tail.addQuadCurve(to: P(45, 46), control: P(42.5, 49.5))
        T(&ctx, tail, 3.2, fur, s)
        F(&ctx, oval(32, 45, 11, 4.4), fur, s)                                  // back legs, stretched out
        F(&ctx, oval(8.5, 37.5 - breathe, 32, 12 + breathe), fur, s)            // the melted body (breathing)
        spots(&ctx, clip: oval(8.5, 37.5, 32, 12), s, head: false, from: coat.napPatches)
        ctx.fill(oval(14, 46, 22, 3), with: .color(cream))
        for x in [20.0, 25.0, 30.0, 35.0] as [CGFloat] {
            var st = Path(); st.move(to: P(x, 38.6)); st.addQuadCurve(to: P(x - 0.6, 42.4), control: P(x + 0.8, 40.6))
            ctx.stroke(st, with: .color(stripe), style: StrokeStyle(lineWidth: 1.3 * s, lineCap: .round))
        }
        F(&ctx, oval(1, 46, 9, 3.6), fur, s)                                    // front paws, the head rests on them
        F(&ctx, oval(7.5, 46.6, 9, 3.2), fur, s)
        var earL = Path(); earL.move(to: P(3, 36.5)); earL.addLine(to: P(3.2, 28.2)); earL.addLine(to: P(10, 32.6)); earL.closeSubpath()
        var earR = Path(); earR.move(to: P(13.5, 32.2)); earR.addLine(to: P(19.5, 28)); earR.addLine(to: P(20, 36)); earR.closeSubpath()
        F(&ctx, earL, coat.earL ?? fur, s); F(&ctx, earR, coat.earR ?? fur, s)
        F(&ctx, oval(1.5, 29.5, 20, 16.5), fur, s)                              // the head, resting
        spots(&ctx, clip: oval(1.5, 29.5, 20, 16.5), s, head: true, from: coat.napPatches)
        for (x0, x1) in [(10.0, 10.3), (12.8, 12.8)] as [(CGFloat, CGFloat)] {
            var st = Path(); st.move(to: P(x0, 30.4)); st.addLine(to: P(x1, 33))
            ctx.stroke(st, with: .color(stripe), style: StrokeStyle(lineWidth: 1.2 * s, lineCap: .round))
        }
        ctx.fill(oval(7.8, 39.6, 5, 3.6), with: .color(cream)); ctx.fill(oval(11.4, 39.6, 5, 3.6), with: .color(cream))
        ctx.fill(oval(2.8, 39, 3.2, 1.7), with: .color(pink.opacity(0.7))); ctx.fill(oval(17.2, 39, 3.2, 1.7), with: .color(pink.opacity(0.7)))
        var eyes = Path()
        eyes.move(to: P(5.2, 36.4)); eyes.addQuadCurve(to: P(9.2, 36.4), control: P(7.2, 38.2))
        eyes.move(to: P(13.8, 36.4)); eyes.addQuadCurve(to: P(17.8, 36.4), control: P(15.8, 38.2))
        ctx.stroke(eyes, with: .color(eye), style: StrokeStyle(lineWidth: 1.1 * s, lineCap: .round))
        var nose = Path(); nose.move(to: P(10.6, 39)); nose.addLine(to: P(12.4, 39)); nose.addLine(to: P(11.5, 40)); nose.closeSubpath()
        ctx.fill(nose, with: .color(pink))
    }

    private enum Eye { case left, right }
    private func openEyes(ctx: inout GraphicsContext, P: (CGFloat, CGFloat) -> CGPoint, s: CGFloat,
                          pupil: CGSize = .zero, half: Bool = false, dx: CGFloat = 0, only: Eye? = nil,
                          wide: Bool = false) {
        let w: CGFloat = wide ? 5.8 : 5.0
        let lx: CGFloat = 17.5 - w / 2 + dx, rx: CGFloat = 28.5 - w / 2 + dx
        let h: CGFloat = wide ? 6.6 : (half ? 3.0 : 5.6)
        let y: CGFloat = wide ? 15.4 : (half ? 17.8 : 16.2)
        if only == nil || only == .left {
            ctx.fill(Path(ellipseIn: CGRect(x: lx * s, y: y * s, width: w * s, height: h * s)), with: .color(eye))
            ctx.fill(Path(ellipseIn: CGRect(x: (lx + 1.4 + pupil.width) * s, y: (y + 0.8 + pupil.height) * s, width: 1.9 * s, height: 1.9 * s)), with: .color(.white))
        }
        if only == nil || only == .right {
            ctx.fill(Path(ellipseIn: CGRect(x: rx * s, y: y * s, width: w * s, height: h * s)), with: .color(eye))
            ctx.fill(Path(ellipseIn: CGRect(x: (rx + 1.4 + pupil.width) * s, y: (y + 0.8 + pupil.height) * s, width: 1.9 * s, height: 1.9 * s)), with: .color(.white))
        }
    }
    private func shutEyes(ctx: inout GraphicsContext, P: (CGFloat, CGFloat) -> CGPoint, s: CGFloat, only: Eye? = nil, dx: CGFloat = 0) {
        if only == nil || only == .left {
            var l = Path()
            l.move(to: P(15 + dx, 19.5)); l.addQuadCurve(to: P(20 + dx, 19.5), control: P(17.5 + dx, 21.3))
            ctx.stroke(l, with: .color(eye), lineWidth: 1.3 * s)
        }
        if only == nil || only == .right {
            var r = Path()
            r.move(to: P(26 + dx, 19.5)); r.addQuadCurve(to: P(31 + dx, 19.5), control: P(28.5 + dx, 21.3))
            ctx.stroke(r, with: .color(eye), lineWidth: 1.3 * s)
        }
    }
    /// Petted: content "^ ^" eyes.
    private func happyEyes(ctx: inout GraphicsContext, P: (CGFloat, CGFloat) -> CGPoint, s: CGFloat, dx: CGFloat = 0) {
        var e = Path()
        e.move(to: P(15 + dx, 19.8)); e.addQuadCurve(to: P(20 + dx, 19.8), control: P(17.5 + dx, 16.4))
        e.move(to: P(26 + dx, 19.8)); e.addQuadCurve(to: P(31 + dx, 19.8), control: P(28.5 + dx, 16.4))
        ctx.stroke(e, with: .color(eye), style: StrokeStyle(lineWidth: 1.4 * s, lineCap: .round))
    }
    private func yawnMouth(ctx: inout GraphicsContext, P: (CGFloat, CGFloat) -> CGPoint, s: CGFloat, dx: CGFloat = 0) {
        ctx.fill(Path(ellipseIn: CGRect(x: (20.4 + dx) * s, y: 24.5 * s, width: 5.2 * s, height: 6.5 * s)), with: .color(Color(red: 0.35, green: 0.16, blue: 0.18)))
        ctx.fill(Path(ellipseIn: CGRect(x: (21.6 + dx) * s, y: 28 * s, width: 2.8 * s, height: 2 * s)), with: .color(pink))
    }
    /// The classic stretch, SIDE-ON (facing right, like the leap): forelegs flat
    /// forward on the ground, chest low, head lowered between them, rear end
    /// raised, tail straight up, eyes shut. Drawn in the 46×62 box; feet ≈ y 49.
    private func bow(ctx: inout GraphicsContext, P: (CGFloat, CGFloat) -> CGPoint,
                     oval: (CGFloat, CGFloat, CGFloat, CGFloat) -> Path, s: CGFloat) {
        func line(_ ax: CGFloat, _ ay: CGFloat, _ bx: CGFloat, _ by: CGFloat, _ w: CGFloat, _ c: Color) {
            var p = Path(); p.move(to: P(ax, ay)); p.addLine(to: P(bx, by))
            if w >= 2 { T(&ctx, p, w, c, s) } else { ctx.stroke(p, with: .color(c), style: StrokeStyle(lineWidth: w * s, lineCap: .round)) }
        }
        // Tail straight up from the raised rump
        var tail = Path(); tail.move(to: P(9, 32)); tail.addQuadCurve(to: P(4.5, 11), control: P(3.5, 24))
        T(&ctx, tail, 3.2, fur, s)
        // Far legs (shaded) first
        line(14.5, 37, 14, 47.4, 3.2, shade)
        line(28, 43, 38, 46.6, 3.2, shade)
        ctx.fill(oval(11.8, 45.9, 4.8, 3.2), with: .color(cream))
        ctx.fill(oval(36.4, 45.9, 4.4, 3.2), with: .color(cream))
        // Body: a capsule from the raised hips down to the low chest, cream belly, stripes
        var back = Path(); back.move(to: P(13, 33)); back.addQuadCurve(to: P(27, 41.5), control: P(22, 33.5))
        T(&ctx, back, 11, fur, s)
        var belly = Path(); belly.move(to: P(15, 38.2)); belly.addQuadCurve(to: P(26, 45.4), control: P(21, 40.4))
        ctx.stroke(belly, with: .color(cream), style: StrokeStyle(lineWidth: 3.4 * s, lineCap: .round))
        for (ax, ay, bx, by) in [(18.7, 28.7, 17.9, 31.4), (23.7, 31.0, 22.3, 33.4), (28.0, 34.3, 26.0, 36.3)] as [(CGFloat, CGFloat, CGFloat, CGFloat)] {
            line(ax, ay, bx, by, 1.3, stripe)
        }
        // Near legs: hind under the raised rump, fore flat forward on the ground
        line(11.5, 36.5, 10.5, 47.4, 3.4, fur)
        line(26, 44, 40.2, 47.4, 3.4, fur)
        ctx.fill(oval(7.8, 45.9, 5.0, 3.2), with: .color(cream))
        ctx.fill(oval(38.4, 45.9, 4.8, 3.2), with: .color(cream))
        // Head lowered at the front, ears up, eyes shut in bliss
        var earB = Path(); earB.move(to: P(28.5, 35)); earB.addLine(to: P(27, 28)); earB.addLine(to: P(33, 33.5)); earB.closeSubpath()
        F(&ctx, earB, shade, s)
        var earF = Path(); earF.move(to: P(33, 34)); earF.addLine(to: P(32.5, 27)); earF.addLine(to: P(37.5, 33.5)); earF.closeSubpath()
        F(&ctx, earF, fur, s)
        var earIn = Path(); earIn.move(to: P(33.8, 32.8)); earIn.addLine(to: P(33.5, 29.2)); earIn.addLine(to: P(36, 32.6)); earIn.closeSubpath()
        ctx.fill(earIn, with: .color(pink))
        F(&ctx, oval(24.5, 33.5, 15, 12.5), fur, s)
        ctx.fill(oval(33, 39.5, 7, 5.5), with: .color(cream))
        var nose = Path(); nose.move(to: P(39, 40.3)); nose.addLine(to: P(40.8, 40.3)); nose.addLine(to: P(39.9, 41.6)); nose.closeSubpath()
        ctx.fill(nose, with: .color(pink))
        var eyes = Path()
        eyes.move(to: P(31.2, 38.4)); eyes.addQuadCurve(to: P(34.6, 38.4), control: P(32.9, 40))
        eyes.move(to: P(36.2, 38.3)); eyes.addQuadCurve(to: P(38.4, 38.3), control: P(37.3, 39.5))
        ctx.stroke(eyes, with: .color(eye), style: StrokeStyle(lineWidth: 1.2 * s, lineCap: .round))
        var wh = Path()
        wh.move(to: P(36.5, 42.4)); wh.addLine(to: P(42.4, 41.2))
        wh.move(to: P(36.5, 43.4)); wh.addLine(to: P(42.4, 44.4))
        ctx.stroke(wh, with: .color(eye.opacity(0.45)), lineWidth: 0.6 * s)
        ctx.fill(oval(29.4, 41, 3, 1.8), with: .color(pink.opacity(0.5)))
        // Collar at the neck
        line(24.6, 36.6, 25.6, 44.2, 2.2, collar)
        ctx.fill(oval(24, 43.6, 3.2, 3.2), with: .color(gold))
    }
    /// Jump mid-air: the tabby stretched long and LEVEL, front legs reaching
    /// forward, back legs trailing, tail streaming up-back, ears back. Drawn in
    /// the 46×62 box (the panel mover owns the travel).
    private func leap(ctx: inout GraphicsContext, P: (CGFloat, CGFloat) -> CGPoint,
                      oval: (CGFloat, CGFloat, CGFloat, CGFloat) -> Path, s: CGFloat) {
        // Tail streams up-back
        var tail = Path()
        tail.move(to: P(12, 40)); tail.addQuadCurve(to: P(2, 24), control: P(4, 38))
        T(&ctx, tail, 3, fur, s)
        // Back legs trail behind
        var b1 = Path(); b1.move(to: P(14, 42)); b1.addLine(to: P(5, 46))
        var b2 = Path(); b2.move(to: P(16, 44)); b2.addLine(to: P(8, 49))
        T(&ctx, b1, 3, fur, s)
        T(&ctx, b2, 3, fur, s)
        ctx.fill(oval(3, 44.8, 4.4, 3.2), with: .color(cream))
        ctx.fill(oval(6, 47.8, 4.4, 3.2), with: .color(cream))
        // Long level body + belly
        F(&ctx, oval(8.5, 31.5, 27, 15), fur, s)
        ctx.fill(oval(12.5, 40.5, 19, 5.5), with: .color(cream))
        for x in [17.0, 21.0, 25.0] as [CGFloat] {
            var st = Path(); st.move(to: P(x, 32.4)); st.addLine(to: P(x - 0.8, 36.4))
            ctx.stroke(st, with: .color(stripe), style: StrokeStyle(lineWidth: 1.3 * s, lineCap: .round))
        }
        // Front legs reach forward
        var f1 = Path(); f1.move(to: P(30, 38)); f1.addLine(to: P(40, 34))
        var f2 = Path(); f2.move(to: P(30, 41)); f2.addLine(to: P(40, 39))
        T(&ctx, f1, 3, fur, s)
        T(&ctx, f2, 3, fur, s)
        ctx.fill(oval(38, 32.5, 4.4, 3.2), with: .color(cream))
        ctx.fill(oval(38, 37.5, 4.4, 3.2), with: .color(cream))
        // Head at the front, ears swept back
        var earB = Path(); earB.move(to: P(29, 32)); earB.addLine(to: P(25.5, 26)); earB.addLine(to: P(31, 27.5)); earB.closeSubpath()
        F(&ctx, earB, shade, s)
        var earF = Path(); earF.move(to: P(32, 31)); earF.addLine(to: P(29.5, 24.5)); earF.addLine(to: P(34.5, 27)); earF.closeSubpath()
        F(&ctx, earF, fur, s)
        F(&ctx, oval(28, 27, 15, 12), fur, s)
        ctx.fill(oval(35.5, 32.5, 7, 5.5), with: .color(cream))
        var nose = Path(); nose.move(to: P(41, 33.5)); nose.addLine(to: P(42.8, 33.5)); nose.addLine(to: P(41.9, 34.8)); nose.closeSubpath()
        ctx.fill(nose, with: .color(pink))
        // Eyes wide open mid-leap
        ctx.fill(oval(33.5, 29.5, 3.2, 3.8), with: .color(eye))
        ctx.fill(oval(34.6, 30.1, 1.1, 1.1), with: .color(.white))
        ctx.fill(oval(37.5, 29.5, 2.6, 3.2), with: .color(eye))
        // Collar + tag at the neck
        var col = Path(); col.move(to: P(29.5, 37)); col.addLine(to: P(31.5, 41))
        ctx.stroke(col, with: .color(collar), style: StrokeStyle(lineWidth: 2 * s, lineCap: .round))
        ctx.fill(oval(30.2, 40.2, 3.2, 3.2), with: .color(gold))
    }
}

/// The four coats. `stripe: .clear` hides the tabby markings; `patches` paints calico spots (clipped to the head
/// and body); `glasses` puts the round reading glasses on (the billing cat wears them at work).
struct CatCoat: Equatable {
    let id: String, name: String
    let fur: Color, stripe: Color, shade: Color, cream: Color, line: Color, eye: Color
    var patches: [(Color, CGRect)] = []     // character-box points, painted inside the fur shapes
    var glasses = false
    var earL: Color? = nil, earR: Color? = nil
    /// The same coat laid flat for the melted nap (the sitting spots would miss the lying body).
    var napPatches: [(Color, CGRect)] = []
    static func == (a: CatCoat, b: CatCoat) -> Bool { a.id == b.id }

    static let ginger = CatCoat(id: "ginger", name: "Ginger",
        fur: Color(red: 0.97, green: 0.70, blue: 0.40), stripe: Color(red: 0.91, green: 0.53, blue: 0.23),
        shade: Color(red: 0.90, green: 0.58, blue: 0.30), cream: Color(red: 1.0, green: 0.96, blue: 0.90),
        line: Color(red: 0.54, green: 0.29, blue: 0.13), eye: Color(red: 0.35, green: 0.20, blue: 0.13), glasses: true)
    static let smokey = CatCoat(id: "smokey", name: "Smokey",
        fur: Color(red: 0.70, green: 0.71, blue: 0.75), stripe: Color(red: 0.49, green: 0.50, blue: 0.56),
        shade: Color(red: 0.60, green: 0.61, blue: 0.66), cream: Color(red: 0.96, green: 0.95, blue: 0.94),
        line: Color(red: 0.27, green: 0.27, blue: 0.32), eye: Color(red: 0.20, green: 0.20, blue: 0.24))
    static let patches = CatCoat(id: "patches", name: "Patches",
        fur: Color(red: 1.0, green: 0.97, blue: 0.93), stripe: .clear,
        shade: Color(red: 0.90, green: 0.87, blue: 0.83), cream: Color(red: 1.0, green: 0.99, blue: 0.97),
        line: Color(red: 0.36, green: 0.27, blue: 0.22), eye: Color(red: 0.25, green: 0.18, blue: 0.15),
        patches: [(Color(red: 0.95, green: 0.62, blue: 0.30), CGRect(x: 6, y: 8, width: 16, height: 14)),     // orange over the left ear
                  (Color(red: 0.24, green: 0.20, blue: 0.19), CGRect(x: 27, y: 9, width: 12, height: 10)),    // dark over the right ear
                  (Color(red: 0.95, green: 0.62, blue: 0.30), CGRect(x: 24, y: 29, width: 11, height: 8)),    // orange on the shoulder
                  (Color(red: 0.24, green: 0.20, blue: 0.19), CGRect(x: 12, y: 30, width: 7, height: 7))],    // dark on the chest side
        earL: Color(red: 0.95, green: 0.62, blue: 0.30), earR: Color(red: 0.24, green: 0.20, blue: 0.19),
        napPatches: [(Color(red: 0.95, green: 0.62, blue: 0.30), CGRect(x: 1, y: 25.5, width: 10, height: 9)),   // orange over the left ear
                     (Color(red: 0.24, green: 0.20, blue: 0.19), CGRect(x: 13, y: 25.5, width: 8, height: 7)),  // dark over the right ear
                     (Color(red: 0.95, green: 0.62, blue: 0.30), CGRect(x: 20, y: 36, width: 13, height: 8)),   // orange saddle on the back
                     (Color(red: 0.24, green: 0.20, blue: 0.19), CGRect(x: 32, y: 38, width: 8, height: 6))])   // dark on the rump
    static let tux = CatCoat(id: "tux", name: "Tux",
        fur: Color(red: 0.20, green: 0.20, blue: 0.23), stripe: .clear,
        shade: Color(red: 0.14, green: 0.14, blue: 0.16), cream: Color(red: 0.98, green: 0.98, blue: 0.97),
        line: Color(red: 0.08, green: 0.08, blue: 0.10), eye: Color(red: 0.98, green: 0.84, blue: 0.35))
    static let butter = CatCoat(id: "butter", name: "Butter",
        fur: Color(red: 0.99, green: 0.86, blue: 0.62), stripe: Color(red: 0.93, green: 0.72, blue: 0.43),
        shade: Color(red: 0.93, green: 0.78, blue: 0.54), cream: Color(red: 1.0, green: 0.98, blue: 0.93),
        line: Color(red: 0.55, green: 0.38, blue: 0.20), eye: Color(red: 0.36, green: 0.24, blue: 0.14))
    static let cocoa = CatCoat(id: "cocoa", name: "Cocoa",
        fur: Color(red: 0.55, green: 0.37, blue: 0.26), stripe: Color(red: 0.38, green: 0.24, blue: 0.16),
        shade: Color(red: 0.46, green: 0.30, blue: 0.21), cream: Color(red: 0.96, green: 0.90, blue: 0.82),
        line: Color(red: 0.22, green: 0.13, blue: 0.08), eye: Color(red: 0.16, green: 0.10, blue: 0.07))
    static let snow = CatCoat(id: "snow", name: "Snow",
        fur: Color(red: 0.98, green: 0.98, blue: 0.99), stripe: .clear,
        shade: Color(red: 0.88, green: 0.89, blue: 0.92), cream: Color(red: 1.0, green: 1.0, blue: 1.0),
        line: Color(red: 0.40, green: 0.42, blue: 0.50), eye: Color(red: 0.22, green: 0.35, blue: 0.62))
    static let lilac = CatCoat(id: "lilac", name: "Lilac",
        fur: Color(red: 0.78, green: 0.74, blue: 0.84), stripe: Color(red: 0.64, green: 0.59, blue: 0.72),
        shade: Color(red: 0.70, green: 0.66, blue: 0.77), cream: Color(red: 0.98, green: 0.96, blue: 0.99),
        line: Color(red: 0.36, green: 0.31, blue: 0.44), eye: Color(red: 0.30, green: 0.24, blue: 0.38))
    static let all = [ginger, smokey, patches, tux, butter, cocoa, snow, lilac]
    static var current: CatCoat {
        let id = UserDefaults.standard.string(forKey: "crewCoat") ?? "ginger"
        return all.first { $0.id == id } ?? ginger
    }
    static func set(_ c: CatCoat) { UserDefaults.standard.set(c.id, forKey: "crewCoat") }
}
