import AppKit
import Combine
import CoreGraphics
import SwiftUI

// CrewModel — the cat's state, driven by the SAME MailStore the web Billing
// Desk reads. Poses = status from the store:
//   needs    — yourMove non-empty (eyes open, holds a sign with the count)
//   working  — watch running and nothing for you (eyes open + blink, holds a book)
//   sleeping — nothing for you, not running (eyes shut, breathing, a small z)
//   reading  — the agent is mid-sweep with a queue count under the cat
// The web Billing Desk does the work now; the Mac pane is gone (26 Sep 2026):
// no selection, no chat sessions, no book/snooze/done — those live on the web.
@MainActor
final class CrewModel: ObservableObject {
    enum Pose: Equatable { case needs, working, sleeping, reading }
    struct Agent: Identifiable {
        let id = "cat"
        let name = "Cataholic"
    }
    let agents = [Agent()]
    @Published var hoveredId: String? = nil    // slides fully out + tooltip
    @Published var runFacing = 0               // zoomies: 0 = not running · 1 = running right · -1 = left
    @Published var runPhase: Double = 0        // zoomies gallop phase — driven per frame, never animated
    @Published var runStride: Double = 38      // leg swing: 38 = zoomies gallop · 22 = a walk (CrewPanel.glide)
    @Published var runLean: Double = 0         // zoomies: + leans into the scramble, − leans back in a skid
    @Published var airborne = false            // falling / flung (CrewPanel.drop): wide eyes, no pin
    @Published var squash: CGFloat = 1         // landing squish: < 1 = squashed flat (springs back to 1)
    @Published var petUntil = Date.distantPast // petted: "^ ^" eyes until then
    /// A squish from the feet that springs back — landings, a pet, being put down.
    func land(_ amount: CGFloat = 0.8) {
        guard !Fx.reduced else { return }
        squash = amount
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.09) { self.squash = 1 }
    }
    /// A click (owner 27 Sep 2026: click = pet): happy eyes, a purr, a little squish.
    func pet() {
        petUntil = Date().addingTimeInterval(1.8)
        land(0.9)
        CrewSound.purrStart()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) {
            if Date() >= self.petUntil.addingTimeInterval(-0.25) { CrewSound.purrStop() }
        }
    }
    @Published var whisper: String? = nil
    private var lastNeeds = -1
    private var subs = Set<AnyCancellable>()
    private var whisperWork: DispatchWorkItem? = nil

    init() {
        let store = MailStore.shared
        // @Published fires on willSet — read the new value on the next turn of the run loop, or every rise
        // is seen one step late (the "something new" alert never fired before 0.3.1).
        store.$cases.sink { [weak self] _ in DispatchQueue.main.async { self?.retick() } }.store(in: &subs)
        store.$watchRunning.sink { [weak self] _ in DispatchQueue.main.async { self?.retick() } }.store(in: &subs)
        store.$agentNow.sink { [weak self] _ in DispatchQueue.main.async { self?.retick() } }.store(in: &subs)
        retick()
        scheduleNext(initial: true)
        Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tickSecond() }
        }
    }

    // Snapshots only: park the pose (fixtures always derive needs, so
    // sleeping/working can never arise from fixture data alone). Never set
    // in production — the pose always derives from the store there.
    var forcedPose: Pose? = nil
    var pose: Pose {
        if let f = forcedPose { return f }
        let store = MailStore.shared
        // Reading while the agent works: .agent_now.json state=reading (fresh)
        // or watch running — the reading pose, with the queue count under it.
        if store.agentNow.state == "reading" { return .reading }
        if !store.yourMove.isEmpty { return .needs }
        if store.watchRunning { return .reading }
        return .sleeping
    }
    var needsCount: Int { MailStore.shared.signCount }
    // "N ✉" under the character while it works (mock step 3).
    var queueCount: Int {
        if let f = forcedQueue { return f }
        let n = MailStore.shared.agentNow
        if n.state == "reading", n.left > 0 { return n.left }
        return 0
    }
    // Hover peek: "Reading: <who>" + "N more in the queue".
    var peek: (who: String, left: Int)? {
        if let f = forcedPeek { return f }
        let n = MailStore.shared.agentNow
        guard n.state == "reading", !n.who.isEmpty else { return nil }
        return (n.who, n.left)
    }
    // Snapshot-only peek override (fixtures carry no .agent_now.json, so
    // shots would never show the peek bubble).
    var forcedPeek: (who: String, left: Int)? = nil
    // Snapshot-only "N ✉" count (fixtures carry no .agent_now.json).
    var forcedQueue: Int? = nil

    /// The text overlays showing right now — peek bubble, whisper, "N ✉". They
    /// hang outside the 46×62 character box, so the panel grows to hold them
    /// (CrewLayout.spec) and CrewView draws exactly these. The hover ring is the
    /// active affordance while it is open, so they step aside for it.
    var overlays: [CrewLayout.Overlay] {
        guard !ringOpen else { return [] }
        var o: [CrewLayout.Overlay] = []
        // (the "Reading: …" peek bubble on hover is gone — nothing appears on hover, owner 27 Sep 2026)
        if whisper != nil { o.append(.whisper) }
        if pose == .reading, queueCount > 0 { o.append(.caption) }
        return o
    }

    func retick() {
        let n = MailStore.shared.yourMove.count
        if lastNeeds >= 0, n > lastNeeds {
            // Step 3 whisper: yourMove grew while the web desk is closed — one line for 3 s, and the cat bumps
            // where it sits (owner 29 Sep 2026: "wherever it is, bumps and says you have a new notification from
            // so and so… and goes back"). Never while the desk is open (tracked via deskOpen).
            if !CrewPanel.deskOpen, let first = MailStore.shared.yourMove.first {
                let (who, _) = MailClearanceFmt.splitTitle(first.title, fallback: first.next)
                whisper = who.isEmpty ? "New notification" : "New notification from \(who)"
                CrewPanel.refit()                 // pick the bubble's side before the view draws it (CrewLayout.whisperOnRight)
                CrewPanel.bump()
                let w = DispatchWorkItem { [weak self] in
                    Task { @MainActor in self?.whisper = nil }
                }
                whisperWork?.cancel()
                whisperWork = w
                DispatchQueue.main.asyncAfter(deadline: .now() + 3, execute: w)
            }
        }
        lastNeeds = n
        objectWillChange.send()
    }
    func dismissWhisper() {
        whisper = nil
        whisperWork?.cancel()
    }

    // MARK: — cat-life behaviour scheduler (owner, 26 Sep 2026)

    /// All tunables in ONE struct so the owner can tune them. Weights pick the
    /// next scheduled action; work reactions + clingy are event-driven.
    struct Tune {
        static var autonomous = false            // Cataholic 0.3.1: an idle cat sits still (owner 28 Sep 2026)
        static var meanWait: Double = 360        // mean ~6 min between actions
        static var minGap: Double = 90           // never < 90 s between actions
        static var hiMaxPer30Min = 1             // "hi" at most once per 30 min
        static var wHabit = 45, wHi = 15, wJump = 15, wBig = 10
        static var idleGrace: Double = 300       // Mac in use? skip if idle > 5 min
        static var welcomeBackAfter: Double = 600 // idle ≥ 10 min → "hi" on return
        static var clingDist: CGFloat = 200      // pointer still within ~200 pt…
        static var clingStill: Double = 2        // …for > 2 s → walk over
        static var clingLeave: Double = 10       // walks home after pointer gone 10 s
        static var lookDist: CGFloat = 300       // eyes follow the pointer within ~300 pt
    }
    enum Action: Equatable { case none, hi, big, stretch, yawn, groom, spin, look, jump, clingy, nap }
    /// Ring order (owner, 26 Sep 2026): Hi · Grow big · Stretch · Yawn · Groom · Chase tail · Look around · Jump · Nap.
    static let ringActions: [(Action, String, String)] = [
        (.hi, "Hi", "hand.wave"),
        (.big, "Grow big", "arrow.up.left.and.arrow.down.right"),
        (.stretch, "Stretch", "figure.flexibility"),
        (.yawn, "Yawn", "mouth.fill"),
        (.groom, "Groom", "comb"),
        (.spin, "Chase tail", "arrow.triangle.2.circlepath"),
        (.look, "Look around", "eye"),
        (.jump, "Jump", "hare"),
        (.nap, "Nap", "bed.double"),
    ]
    @Published var action: Action = .none       // the behaviour in progress
    @Published var ringOpen = false             // hover ring of emote buttons around the cat
    @Published var hiBubble: String? = nil      // "hi" bubble (1.5 s), separate from the whisper
    @Published var bigScale: Double = 1         // grow-big ~1.8× puff
    @Published var jumpLift: Double = 0         // jump arc height (pt)
    @Published var jumpCrouch = false           // the 0.2 s take-off crouch, before the leap
    @Published var jumpFacing: Double = 1       // 1 = leaps right · -1 = leaps left (the drawing is mirrored)
    @Published var spinDeg: Double = 0          // tail-chase spin (deg) — snapshots only; live, CrewView reads the clock
    private var schedWork: DispatchWorkItem? = nil
    private var actionWork: DispatchWorkItem? = nil
    private var lastActionAt = Date.distantPast
    /// When the current action started (drives the jump crouch → leap split).
    var lastActionStarted = Date.distantPast
    private var lastHiAt = Date.distantPast
    private var lastIdleAt = Date()              // last time input was seen
    private var wasIdleLong = false
    private var lastPointer = NSPoint.zero
    private var pointerStillSince: Date? = nil
    private var clingAway = false               // clingy moved it off its home spot; it walks back when the pointer leaves
    private var clingLeaveSince: Date? = nil
    /// Slid fully out of its tuck: hovered, ring open, or mid-action.
    var isOut: Bool { hoveredId != nil || ringOpen || action != .none }

    // MARK: — scheduler machinery

    /// Any autonomous action in progress stops instantly on a click (the click wins).
    func stopAction() {
        actionWork?.cancel()
        actionWork = nil
        action = .none
        ringOpen = false
        hiBubble = nil
        bigScale = 1
        jumpLift = 0
        jumpCrouch = false
        jumpFacing = 1
        spinDeg = 0
        clingAway = false
        CrewPanel.refit()
    }

    private var lifeOff: Bool { CrewPrefs.quiet || Fx.reduced || Fx.snapshot }
    private func macIdle() -> Double {
        Double(CGEventSource.secondsSinceLastEventType(.combinedSessionState,
                                                       eventType: .mouseMoved))
    }
    private var userTypingOrDragging: Bool {
        (NSEvent.pressedMouseButtons != 0)
            || (CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: .keyDown) < 2)
    }

    private func scheduleNext(initial: Bool = false) {
        schedWork?.cancel()
        // Weighted mean ~6 min (180–540 s), always ≥ 90 s after the last action.
        var wait = Double.random(in: 180...540)
        if initial { wait = Double.random(in: 60...180) }
        wait = max(wait, Tune.minGap - Date().timeIntervalSince(lastActionAt))
        let w = DispatchWorkItem { [weak self] in
            Task { @MainActor in
                self?.fireNext()
                self?.scheduleNext()
            }
        }
        schedWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + wait, execute: w)
    }

    private func fireNext() {
        guard Tune.autonomous, !lifeOff, action == .none, !ringOpen, !CrewPanel.zooming else { return }
        guard macIdle() < Tune.idleGrace else { return }   // Mac in use only
        let hiOK = Date().timeIntervalSince(lastHiAt) > 1800
        var pool: [(Action, Int)] = []
        pool += [(.stretch, 9), (.yawn, 9), (.groom, 9), (.spin, 9), (.look, 9)]  // habits 45
        if hiOK { pool.append((.hi, Tune.wHi)) }
        pool += [(.jump, Tune.wJump), (.big, Tune.wBig)]
        let total = pool.map(\.1).reduce(0, +)
        var r = Int.random(in: 0..<total)
        var pick: Action = .look
        for (a, wgt) in pool { if r < wgt { pick = a; break }; r -= wgt }
        if CrewPanel.zooming { return }
        startAction(pick)    }

    /// Duration per action; visuals reset when it ends. Jump moves the panel
    /// (CrewPanel.lifeJump); spin rotates the drawing one full turn.
    /// One function per action: the hover ring and the scheduler BOTH call
    /// startAction (owner, 26 Sep 2026) — playHi/playBig/… own the visuals.
    func startAction(_ a: Action, dur overrideDur: Double? = nil, user userInitiated: Bool = false) {
        guard action == .none, !CrewPanel.zooming else { return }
        // Autonomous runs need a quiet, still Mac; a direct click on the ring
        // is the user asking NOW — userInitiated skips the quiet/idle gates
        // (Reduce Motion still plays it as a still pose via `still` below).
        guard userInitiated || !lifeOff else { return }
        action = a
        lastActionAt = Date()
        lastActionStarted = Date()
        CrewPanel.refit()    // the panel grows to the pose BEFORE it moves or scales (no clipped first frame)
        // Reduce Motion (user-initiated): the ring still works, but the action
        // plays as a STILL pose for its duration instead of moving.
        let still = Fx.reduced
        let dur: Double
        switch a {
        case .hi: dur = playHi(still: still)
        case .big: dur = playBig(still: still)
        case .stretch: dur = playStretch(still: still)
        case .yawn: dur = playYawn(still: still)
        case .groom: dur = playGroom(still: still)
        case .spin: dur = playSpin(still: still)
        case .look: dur = playLook(still: still)
        case .jump: dur = playJump(still: still)
        case .clingy: dur = playClingy(still: still)
        case .nap: dur = playNap(still: still)
        case .none: return
        }
        let end = overrideDur ?? dur
        actionWork?.cancel()
        let w = DispatchWorkItem { [weak self] in
            Task { @MainActor in self?.endAction() }
        }
        actionWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + end, execute: w)
    }

    private func playHi(still: Bool) -> Double {
        lastHiAt = Date()
        hiBubble = "hi"
        return 2.0
    }
    private func playBig(still: Bool) -> Double {
        if !still { bigScale = 1.8 }
        return 1.8
    }
    private func playStretch(still: Bool) -> Double { 1.5 }
    private func playYawn(still: Bool) -> Double { 1.0 }
    private func playGroom(still: Bool) -> Double { 2.0 }
    /// Chase tail: one full turn over `spinDuration`. CrewView turns the cat from the
    /// clock through `spinDeg(at:)` (the flip is cos(deg), so an animated `spinDeg`
    /// would tween cos(0) → cos(360) = no motion).
    static let spinDuration = 1.0
    static func spinDeg(at elapsed: Double) -> Double { min(max(elapsed / spinDuration, 0), 1) * 360 }
    private func playSpin(still: Bool) -> Double { Self.spinDuration }
    private func playLook(still: Bool) -> Double { 3.0 }
    private func playJump(still: Bool) -> Double {
        if !still {
            jumpCrouch = true        // lifeJump clears it after 0.2 s, when the leap starts
            CrewPanel.lifeJump()
        }
        return 1.6
    }
    private func playClingy(still: Bool) -> Double { 2.0 }
    private func playNap(still: Bool) -> Double { 4.0 }

    // MARK: — hover ring dwell (owner, 26 Sep 2026)

    private var hoverWork: DispatchWorkItem? = nil
    private var leaveWork: DispatchWorkItem? = nil
    /// Pointer rested on the cat 0.4 s → show the ring (hover ring dwell).
    func hoverDwell() {
        leaveWork?.cancel()
        leaveWork = nil
        guard !ringOpen, action == .none, !CrewPanel.zooming else { return }
        hoverWork?.cancel()
        let w = DispatchWorkItem { [weak self] in
            Task { @MainActor in
                guard let self, !self.ringOpen, self.action == .none, !CrewPanel.zooming else { return }
                self.ringOpen = true
                self.objectWillChange.send()
                CrewPanel.refit()
            }
        }
        hoverWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4, execute: w)
    }
    /// Pointer left cat + ring: 0.5 s grace, then fade out (leave dwell).
    func hoverLeave() {
        hoverWork?.cancel()
        hoverWork = nil
        guard ringOpen else { return }
        leaveWork?.cancel()
        let w = DispatchWorkItem { [weak self] in
            Task { @MainActor in
                self?.ringOpen = false
                self?.objectWillChange.send()
                CrewPanel.refit()
            }
        }
        leaveWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5, execute: w)
    }
    func cancelLeave() {
        leaveWork?.cancel()
        leaveWork = nil
    }
    /// A ring tap: hide the ring and do the action NOW, through the same
    /// startAction the scheduler calls. Never opens the Billing Desk.
    func ringTap(_ a: Action) {
        hoverWork?.cancel()
        leaveWork?.cancel()
        ringOpen = false
        startAction(a, user: true)
    }

    private func endAction() {
        let wasBig = action == .big
        actionWork = nil
        hiBubble = nil
        bigScale = 1
        jumpLift = 0
        jumpCrouch = false
        jumpFacing = 1
        spinDeg = 0
        if wasBig {
            // The spring-back needs its room: keep the pose (and the panel) until it settles.
            let w = DispatchWorkItem { [weak self] in
                Task { @MainActor in
                    self?.action = .none
                    CrewPanel.refit()
                }
            }
            actionWork = w
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6, execute: w)
        } else {
            action = .none
            CrewPanel.refit()
        }
    }

    /// Once-a-second watch: welcome-back hi + clingy proximity. No work when
    /// hidden — CrewPanel hides the panel, and tickSecond early-outs there.
    func tickSecond() {
        guard Tune.autonomous, !lifeOff else { return }
        let idle = macIdle()
        if idle >= Tune.welcomeBackAfter { wasIdleLong = true; lastIdleAt = Date(); return }
        if wasIdleLong, idle < 5 {
            // The user is back after ≥ 10 min idle → "hi" once.
            wasIdleLong = false
            lastIdleAt = Date()
            if action == .none, !CrewPanel.zooming,
               Date().timeIntervalSince(lastHiAt) > 1800 {
                startAction(.hi)
            }
            return
        }
        guard idle < Tune.idleGrace, !CrewPanel.zooming else { return }
        // Clingy: pointer still within ~200 pt of the cat for > 2 s → walk
        // over and rub (2 s sway, half-closed eyes), settle next to it; walk
        // home after the pointer leaves for 10 s. Never while dragging/typing.
        let p = NSEvent.mouseLocation
        if p.x == lastPointer.x, p.y == lastPointer.y {
            if pointerStillSince == nil { pointerStillSince = Date() }
        } else {
            pointerStillSince = nil
        }
        lastPointer = p
        if clingAway {
            // It stays settled next to the pointer; home again once the pointer
            // has been away from the cat for 10 s.
            if let c = CrewPanel.catCenter(), hypot(p.x - c.x, p.y - c.y) < Tune.clingDist {
                clingLeaveSince = nil
            } else if clingLeaveSince == nil {
                clingLeaveSince = Date()
            } else if Date().timeIntervalSince(clingLeaveSince!) > Tune.clingLeave {
                CrewPanel.lifeGlideHome()
                clingAway = false
                clingLeaveSince = nil
                if action == .clingy { endAction() }
            }
        }
        guard action == .none, !ringOpen, !userTypingOrDragging else { return }
        if !clingAway,
           let still = pointerStillSince,
           Date().timeIntervalSince(still) > Tune.clingStill,
           let c = CrewPanel.catCenter(),
           hypot(p.x - c.x, p.y - c.y) < Tune.clingDist {
            clingAway = true
            CrewPanel.lifeCling(to: p)
            startAction(.clingy)
        }
    }
}

// Quiet + show toggles live in UserDefaults (same pattern as the notch toggle).
@MainActor
enum CrewPrefs {
    static var showCrew: Bool {
        UserDefaults.standard.object(forKey: "crewEnabled") as? Bool ?? true
    }
    static func setShowCrew(_ on: Bool) {
        UserDefaults.standard.set(on, forKey: "crewEnabled")
        if on { CrewPanel.show() } else { CrewPanel.hide() }
        CrewHerd.shared.restart()
    }
    static var quiet: Bool {
        UserDefaults.standard.object(forKey: "crewQuiet") as? Bool ?? false
    }
    static func setQuiet(_ on: Bool) {
        UserDefaults.standard.set(on, forKey: "crewQuiet")
        CrewPanel.applyPrefs()
        CrewHerd.shared.restart()                  // quiet mode sends the herd home
    }
}
