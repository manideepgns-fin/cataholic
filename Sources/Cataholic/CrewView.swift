import AppKit
import SwiftUI

// CrewView — the cat on the right edge: mostly tucked off-screen (≈30pt
// visible), fully out on hover. Motion: working → gentle ±2.5° sway around
// the pin; needs → one small wave when the count goes up, then still;
// sleeping → slow breathing + a drifting "z". Reduced motion (or snapshots):
// poses only, no sway/wave/z. Tooltip via .help (native — renders outside the
// narrow panel); a click pets it, a double-click opens Find my cat;
// a file drop does nothing (intake moved to the web desk).
// Cat-life (owner, 26 Sep 2026): one TimelineView (.animation, paused while
// hidden) drives the always-on life layer — breathing ~1.5% on a 3–4 s sine,
// slow tail sway with a random phase, an ear twitch every 8–25 s (random),
// blinks at random 2–7 s intervals (occasional double), and pupils that look
// toward the pointer within ~300 pt (≤ 1.5 pt offset). Quiet mode + Reduce
// Motion turn off every autonomous action and movement; only static + blink.
struct CrewView: View {
    @ObservedObject var model: CrewModel
    let agent: CrewModel.Agent
    var body: some View {
        Group {
            if lifeOffSchedule || !moving {
                TimelineView(.explicit([])) { tl in
                    lifeFrame(date: tl.date)
                }
            } else {
                TimelineView(.animation(minimumInterval: 1 / 30)) { tl in
                    lifeFrame(date: tl.date)
                }
            }
        }
    }

    private func lifeFrame(date: Date) -> some View {
        let t = motionT(date: date)
        let pose = model.pose
        let out = model.isOut
        let running = model.runFacing != 0
        // Zoomies face the way they run; a leap (and its crouch) faces the way it jumps; a peek over an edge faces out.
        let facing: Double = running ? (model.runFacing < 0 ? -1 : 1) : (model.action == .jump || model.action == .look ? model.jumpFacing : 1)
        let breatheScale = lifeBreatheScale(t)
        // An emote is a performance on the clock (Emote.frame): read once per frame from the timeline's date.
        // Quiet mode / Reduce Motion / snapshots: the identity frame — the action's still pose.
        let animating = model.action != .none && !lifeOffSchedule && !Fx.snapshot
        let f = animating ? Emote.frame(model.action, at: date.timeIntervalSince(model.lastActionStarted),
                                             hanging: CrewPanel.hanging && !model.airborne, hold: model.napHold) : EmoteFrame()
        let spinScale = model.action == .spin ? cos((Fx.snapshot ? model.spinDeg : f.spin) * .pi / 180) : cos(f.spin * .pi / 180)
        let skin: CrewSkinState
        switch f.skin {
        case .own: skin = skinState(pose)
        case .rest: skin = restSkin(pose)
        case .alert: skin = .alert
        case .crouch, .leap: skin = .jump
        case .yawn: skin = .yawn
        }
        let crouch = f.skin == .crouch ? true : (f.skin == .leap ? false : model.jumpCrouch)
        let snapLook = Fx.snapshot && model.action == .look      // snapshots park the look mid-turn
        let feet = UnitPoint(x: 0.5, y: 49 / 62)
        let tilt = running ? sin(model.runPhase) * 4 + model.runLean
            : swayDeg(pose, t) + clingSway(t)
        let badge = pose == .needs ? "\(model.needsCount)" : ""
        let shut = (pose == .working && blinkShut(t)) || lifeBlink(t)
        let breatheYOff: CGFloat = pose == .sleeping ? breathe(t) : 0
        let z: (CGFloat, Double) = pose == .sleeping ? zLift(t) : (0, 0)
        let ringed = model.ringOpen && model.action == .none
        // The panel is sized by CrewLayout (pose bounds + 4 pt, or the ring);
        // this ZStack is the panel content. Everything below is relative to the
        // 46×62 character box, which sits at `L.box` inside the panel; the ring
        // is laid out in panel space around the cat.
        let L = CrewLayout.spec(for: model)
        let overlays = model.overlays
        // No tuck (owner 27 Sep 2026: the docked cat vanished into the corner and slid out on hover).
        let tuck: CGFloat = 0
        let u = CrewLayout.zoom   // drawn at native size, never stretched (a scaled layer is blurry)
        return ZStack(alignment: .topLeading) {
            ZStack(alignment: .topLeading) {
            ZStack(alignment: .topLeading) {
                CrewCharacter(state: running ? .running : skin,
                              badge: badge,
                              colors: CrewSkinColors(),
                              blinkShut: shut,
                              breatheY: breatheYOff,
                              zLift: model.action == .nap ? f.z : z,
                              runPhase: model.runPhase,
                              breathe: breatheScale,
                              tailSway: animating ? f.tail : lifeTail(t),
                              earTwitch: animating ? f.earTwitch : lifeEar(t),
                              pupil: animating ? CGSize(width: f.pupil, height: 0) : lifePupil(),
                              headTurn: snapLook ? 3 : f.headTurn,
                              headTilt: snapLook ? 6 : f.headTilt,
                              jumpCrouch: crouch,
                              bodyTilt: clingLean,
                              pin: !ringed && CrewPanel.hanging && !model.airborne && f.skin != .alert,
                              happy: date < model.petUntil,
                              stride: model.runStride,
                              pawRaise: f.paw, pawWave: f.wave, groomPaw: f.groomPaw, tongue: f.tongue,
                              mouthOpen: f.mouth, earsBack: f.ears, eyes: f.eyes, pawWrist: f.wrist,
                              napBreath: f.breath, napPeek: f.peek,
                              runSpeed: model.runSpeed, runGait: model.runGait)
                    .scaleEffect(x: facing * f.sx, y: f.sy, anchor: running ? .center : feet)   // squash & stretch (and grow big) from the feet
                    .scaleEffect(x: spinScale, y: 1)
                    // Chunky squish on landing / pet: flatter and wider from the feet, springs back.
                    .scaleEffect(x: 1 + (1 - model.squash) * 0.6, y: model.squash, anchor: feet)
                    .rotationEffect(.degrees(f.rot * facing), anchor: feet)
                    .rotationEffect(.degrees(tilt),
                                    anchor: running ? .center : UnitPoint(x: 0.5, y: 2 / 62))
                    .rotationEffect(.degrees(running ? model.climbRot : 0))         // climbing: upright against the wall
                    // the side-on runner touches both edges of its box; 7 % smaller it can lean into a scramble or a skid
                    // without its tail, whiskers or dust being cut off by the panel (which stays the 46×62 box while running)
                    .scaleEffect(running ? 0.93 : 1)
            }
            .frame(width: 46 * u, height: 62 * u)
                .offset(x: tuck)
                .offset(y: -f.lift * u)
                .animation(Fx.reduced ? nil : .easeOut(duration: 0.35), value: out)
                .animation(Fx.reduced ? nil : .spring(response: 0.28, dampingFraction: 0.42), value: model.squash)
                // Nothing appears on hover (owner 27 Sep 2026: "remove the emote layer when I hover"):
                // the cat stays easy to grab; the emotes live in the right-click menu below.
                .onHover { h in model.hoveredId = h ? agent.id : nil; if h { CrewPanel.slip() } }   // on her own: knocked off a perch
                // click = pet it (owner 27 Sep 2026) — or catch it mid-run · double-click = Find my cat (owner 13 Oct 2026).
                // Zoomies moved to the right-click menu. Any autonomous action stops instantly — the click wins.
                .gesture(TapGesture(count: 2).onEnded { model.stopAction(); CrewPanel.stopLife(); CrewSound.meow(); CrewPanel.openGame() }
                    .exclusively(before: TapGesture().onEnded {
                        if CrewPanel.zooming { CrewSound.meow(); CrewPanel.catchCat(); return }
                        model.stopAction(); CrewPanel.stopLife()
                        model.pet()
                    }))
                // moving the mouse ≥ 4 pt while pressed slides the character along the edge
                .gesture(DragGesture(minimumDistance: 4)
                    .onChanged { _ in CrewMove.drag() }
                    .onEnded { _ in CrewMove.end() })
                .help(tip)
                // Right-click: the cat menu panel (CatMenuPanel, installed at launch).
                // Step 3: quiet while it works — "N ✉" under the character.
                // The three overlays draw at their CrewLayout.Overlay rects — fixed frames,
                // the panel grows to hold exactly these (model.overlays is the shared list).
                if overlays.contains(.caption) {
                    let R = CrewLayout.Overlay.caption.rect
                    Text("\(model.queueCount) ✉")
                        .font(.system(size: 10 * u, design: .monospaced))
                        .foregroundStyle(Color(nsColor: .secondaryLabelColor))
                        .lineLimit(1).minimumScaleFactor(0.6)
                        .frame(width: R.width * u, height: R.height * u)
                        .background(Color.black.opacity(0.35), in: RoundedRectangle(cornerRadius: 999))
                        .offset(x: tuck + R.minX * u, y: R.minY * u)
                }
                // Whisper: one line for 3s when yourMove grows, web desk closed.
                if overlays.contains(.whisper), let w = model.whisper {
                    let R = CrewLayout.Overlay.whisper.rect
                    // Notification-sized (owner 28 Sep 2026: "too big") — 13 pt, hugging its text, next to the cat.
                    Text(w)
                        .font(.system(size: 13, weight: .medium))
                        .foregroundStyle(Color(red: 0.13, green: 0.12, blue: 0.15))
                        .lineLimit(1).truncationMode(.tail)
                        .padding(.horizontal, 10).padding(.vertical, 6)
                        .background(Color(red: 0.984, green: 0.973, blue: 0.945),
                                    in: RoundedRectangle(cornerRadius: 10))
                        .shadow(color: .black.opacity(0.18), radius: 3, y: 1)
                        .frame(width: R.width * u, height: R.height * u, alignment: CrewLayout.whisperOnRight ? .leading : .trailing)
                        .offset(x: R.minX * u, y: R.minY * u)
                        .onTapGesture { model.dismissWhisper() }
                }
                // Say-hi bubble: sits just above-left of the head (≤ 6 pt from the ear)
                // with a small tail pointing at the cat. Fixed 34×26 so its bounds
                // (CrewLayout.drawn(.hi)) do not depend on font metrics.
                if let h = model.hiBubble {
                    let paper = Color(red: 0.984, green: 0.973, blue: 0.945)
                    ZStack(alignment: .topLeading) {
                        RoundedRectangle(cornerRadius: 13 * u).fill(paper)
                        Path { p in
                            p.move(to: CGPoint(x: 24 * u, y: 25 * u))
                            p.addLine(to: CGPoint(x: 32.5 * u, y: 25 * u))
                            p.addLine(to: CGPoint(x: 39 * u, y: 34.5 * u))
                            p.closeSubpath()
                        }
                        .fill(paper)
                        Text(h)
                            .font(.system(size: 13 * u, weight: .semibold))
                            .foregroundStyle(Color(red: 0.13, green: 0.12, blue: 0.15))
                            .frame(width: 34 * u, height: 26 * u)
                    }
                    .frame(width: 34 * u, height: 26 * u, alignment: .topLeading)
                    .scaleEffect(f.bubble, anchor: UnitPoint(x: 39 / 34, y: 34.5 / 26))   // pops in from its tail
                    .opacity(f.bubbleAlpha)
                    .offset(x: -26.5 * u, y: -20.5 * u)
                    .allowsHitTesting(false)
                }
            }
            .frame(width: 46 * u, height: 62 * u, alignment: .topLeading)
            .offset(x: L.box.x * u, y: L.box.y * u)
                // Hover ring: one round button per action, evenly spaced on a
                // circle around the cat (12 o'clock first). Click → the cat does it NOW.
                if ringed {
                    RingView(model: model, size: CGSize(width: L.size.width * u, height: L.size.height * u),
                             origin: CGPoint(x: L.box.x * u, y: L.box.y * u))
                        .transition(.opacity)
                }
            }
            .frame(width: L.size.width * u, height: L.size.height * u, alignment: .topLeading)
    }

    // MARK: — hover ring (owner, 26 Sep 2026)

    /// The ring buttons: one per action, evenly spaced on CrewLayout.ringRadius
    /// around the cat's body centre. Real buttons with accessibility labels;
    /// RingButtonStyle keeps the pill inside the button
    /// (check_dead_edge_buttons.py: no .plain + padding + background).
    private struct RingView: View {
        @ObservedObject var model: CrewModel
        let size: CGSize
        let origin: CGPoint          // the character box's top-left in the panel (native points)
        var body: some View {
            let u = CrewLayout.zoom, rects = CrewLayout.pillRects()
            ZStack(alignment: .topLeading) {
                ForEach(0..<CrewModel.ringActions.count, id: \.self) { i in
                    let (action, name, symbol) = CrewModel.ringActions[i]
                    let r = rects[i]
                    Button {
                        model.ringTap(action)
                    } label: {
                        HStack(spacing: 5) {
                            Image(systemName: symbol)
                                .font(.system(size: 11, weight: .semibold))
                                .frame(width: 14)
                            Text(name)
                                .font(.system(size: 12, weight: .medium))
                                .lineLimit(1)
                        }
                        .foregroundStyle(Color(red: 0.96, green: 0.96, blue: 0.965))
                        .padding(.horizontal, 9)
                        .frame(width: r.width * u, height: r.height * u, alignment: .leading)
                        .background(Capsule().fill(Color(red: 0.067, green: 0.067, blue: 0.078)))
                        .overlay(Capsule().strokeBorder(Color(red: 0.196, green: 0.196, blue: 0.216), lineWidth: 1))
                    }
                    .buttonStyle(RingButtonStyle())
                    .accessibilityLabel(name)
                    .offset(x: origin.x + r.minX * u, y: origin.y + r.minY * u)
                }
            }
            .frame(width: size.width, height: size.height, alignment: .topLeading)
            .allowsHitTesting(true)
            .opacity(model.ringOpen ? 1 : 0)
            .animation(Fx.reduced ? nil : .easeOut(duration: 0.15), value: model.ringOpen)
            .onHover { h in
                // The pills are one hover surface with the cat: leaving both
                // starts the 0.5 s fade; coming back cancels it.
                if h { model.cancelLeave() } else { model.hoverLeave() }
            }
        }
    }

    private struct RingButtonStyle: ButtonStyle {
        func makeBody(configuration: Configuration) -> some View {
            configuration.label
                .contentShape(Capsule())
                .opacity(configuration.isPressed ? 0.7 : 1)
                .scaleEffect(configuration.isPressed ? 0.92 : 1)
        }
    }

    private var tip: String {
        let n = model.needsCount
        switch model.pose {
        case .needs: return "\(n) waiting · \(MailStore.shared.note.isEmpty ? "needs you" : MailStore.shared.note)"
        case .working: return "\(agent.name) · working"
        case .sleeping: return "\(agent.name) · double-click: Find my cat"
        case .reading:
            if let p = model.peek { return "\(agent.name) · reading \(p.who)" }
            return "\(agent.name) · reading…"
        }
    }
    private func skinState(_ p: CrewModel.Pose) -> CrewSkinState {
        // An in-progress behaviour overrides the store pose (running zoomies win over all).
        if model.airborne { return .alert }          // falling / flung: eyes wide, ears up
        switch model.action {
        case .hi: return .hi
        case .big: return .big
        case .stretch: return .stretch
        case .yawn: return .yawn
        case .groom: return .groom
        case .spin: return .needs == p ? .needs : .working  // spin on the base pose
        case .look: return .look
        case .jump: return .jump
        case .clingy: return .clingy
        case .nap: return .nap
        case .none: break
        }
        return restSkin(p)
    }
    /// The resting drawing for the pose — what an emote settles into before it ends.
    private func restSkin(_ p: CrewModel.Pose) -> CrewSkinState {
        if model.airborne { return .alert }
        if p == .sleeping, CataholicIdle.seconds < 300 { return .look }   // Cataholic: awake while you're around
        switch p {
        case .needs: return .needs
        case .working: return .working
        case .sleeping: return .sleeping
        case .reading: return .reading
        }
    }
    // One TimelineView drives the life layer; paused while the panel hides
    // (CrewNSPanel is character-sized and orderOut hides it — no per-frame
    // work then). Quiet/Reduce Motion park on an empty explicit schedule:
    // static pose + blink only.
    private var lifeOffSchedule: Bool { Fx.reduced || CrewPrefs.quiet }
    /// Frames only while something moves (owner 28 Sep 2026: "if its idle its idle, no theatrics"). At rest the cat
    /// is ONE still frame — no breathing, tail sway, blinks or eyes on the pointer — and redraws only when its state
    /// changes (the model publishes). A 30 fps life layer cost ~12% CPU and leaked memory every frame. A held nap is still too.
    private var moving: Bool {
        (model.action != .none && !model.napStill) || model.runFacing != 0 || model.airborne || model.jumpCrouch || model.squash != 1
    }
    private func lifeBreatheScale(_ t: Double) -> CGFloat {
        guard lifeBreathing else { return 1 }
        return 1 + 0.0075 * CGFloat(0.5 + 0.5 * sin(t * 2 * .pi / lifeBreathPeriod))
    }
    // Random-but-stable life constants (per launch): tail phase, breath period.
    private var lifeTailPhase: Double { 1.7 }
    private var lifeBreathPeriod: Double { 3.4 }
    private var lifeOff: Bool { Fx.reduced || Fx.snapshot || CrewPrefs.quiet }
    /// Breathing pauses while an action overrides the pose — the puff/spring
    /// owns the scale then.
    private var lifeBreathing: Bool { model.action == .none }
    private func lifeTail(_ t: Double) -> CGFloat {
        guard !lifeOff, model.action == .none else { return 0 }
        return CGFloat(sin(t * 2 * .pi / 7 + lifeTailPhase) * 2.2)
    }
    /// Ear twitch every 8–25 s (random): a 0.35 s flick on a slot hash.
    private func lifeEar(_ t: Double) -> Double {
        guard !lifeOff, model.action == .none else { return 0 }
        let slot = Int(t / 8)
        let jitter = Double((slot * 2654435761) % 170) / 10   // 0–17 s extra → 8–25 s
        let start = Double(slot) * 8 + jitter
        let u = t - start
        if u >= 0, u < 0.35 { return sin(u / 0.35 * .pi) }
        return 0
    }
    /// Blinks at random 2–7 s intervals (not metronomic; ~1 in 6 is a double).
    private func lifeBlink(_ t: Double) -> Bool {
        guard !lifeOff else { return false }
        if model.pose == .sleeping { return false }   // shut already
        let slot = Int(t / 2)
        let jitter = Double((slot * 40503 + 7) % 50) / 10   // 0–5 s extra → 2–7 s
        let start = Double(slot) * 2 + jitter
        let u = t - start
        let dbl = (slot * 97) % 6 == 0
        if u >= 0, u < 0.12 { return true }
        if dbl, u >= 0.22, u < 0.34 { return true }
        return false
    }
    /// Eyes/head look toward the pointer within ~300 pt (pupil ≤ 1.5 pt).
    private func lifePupil() -> CGSize {
        guard !lifeOff, model.action == .none || model.action == .look else { return .zero }
        guard let c = CrewPanel.catCenter() else { return .zero }
        let p = NSEvent.mouseLocation
        let dx = p.x - c.x, dy = p.y - c.y
        guard hypot(dx, dy) < CrewModel.Tune.lookDist else { return .zero }
        return CGSize(width: max(-1.5, min(1.5, dx / 200 * 1.5)),
                      height: max(-1.5, min(1.5, -dy / 200 * 1.5)))
    }
    /// Clingy: lean the whole body ~8° toward the pointer side.
    private var clingLean: CGFloat {
        guard model.action == .clingy else { return 0 }
        if Fx.snapshot { return 8 }
        guard let c = CrewPanel.catCenter() else { return 8 }
        return NSEvent.mouseLocation.x >= c.x ? 8 : -8
    }
    /// Clingy rub: a gentle ±3° sway while it leans on the pointer.
    private func clingSway(_ t: Double) -> Double {
        guard model.action == .clingy, !lifeOff else { return 0 }
        return 3 * sin(t * 2 * .pi / 1.2)
    }
    // Frozen clock under reduced motion / snapshots: poses only. Snapshots
    // park mid-cycle (t=1.2) so the sleeping "z" is caught mid-drift instead
    // of at its invisible cycle start.
    private func motionT(date: Date) -> Double {
        if Fx.reduced { return 0 }
        if Fx.snapshot { return 1.2 }
        return date.timeIntervalSinceReferenceDate
    }
    private func swayDeg(_ p: CrewModel.Pose, _ t: Double) -> Double {
        guard p == .working, !Fx.reduced else { return 0 }
        return 2.5 * sin(t * 2 * .pi / 5.5)
    }
    private func blinkShut(_ t: Double) -> Bool {
        (t.truncatingRemainder(dividingBy: 4.5)) > 4.3
    }
    private func breathe(_ t: Double) -> CGFloat {
        CGFloat(0.5 + 0.5 * sin(t * 2 * .pi / 6 - .pi / 2))
    }
    private func zLift(_ t: Double) -> (CGFloat, Double) {
        let u = t.truncatingRemainder(dividingBy: 4) / 4
        let op = u < 0.3 ? u / 0.3 * 0.8 : max(0, 0.8 * (1 - (u - 0.3) / 0.7))
        return (CGFloat(u * 10), op)
    }
}
