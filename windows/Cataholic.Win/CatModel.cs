using System;
using System.Collections.Generic;
using System.Windows;

namespace Cataholic;

// CatModel — the cat's state: what she is doing, the movers' per-frame channels (run phase, lean…), and the scheduler
// that makes her act on her own. Mirrors CrewModel.swift. There is no inbox on Windows: her store pose is `sleeping`
// — which the view draws as `look` while you are at the PC and as the idle nap after 5 minutes away — until a
// cataholic://sign link makes it `needs` (the sign, and the whisper: Sign.cs). Every property that changes how she is DRAWN goes through Set(): it publishes, and the panel redraws
// (and refits) once per turn of the dispatcher.
sealed partial class CatModel
{
    public enum Action { None, Hi, Big, Stretch, Yawn, Groom, Spin, Look, Jump, Clingy, Nap }
    /// Grid order (owner, 26 Sep 2026): Hi · Grow big · Stretch · Yawn · Groom · Chase tail · Look around · Jump · Nap.
    public static readonly (Action Action, string Name)[] RingActions =
    {
        (Action.Hi, "Hi"), (Action.Big, "Grow big"), (Action.Stretch, "Stretch"), (Action.Yawn, "Yawn"), (Action.Groom, "Groom"),
        (Action.Spin, "Chase tail"), (Action.Look, "Look around"), (Action.Jump, "Jump"), (Action.Nap, "Nap"),
    };

    /// All tunables in ONE struct so the owner can tune them.
    public static class Tune
    {
        public static bool Autonomous = false;            // Cataholic 0.3.1: an idle cat sits still (owner 28 Sep 2026)
        public static double MinGap = 90;                 // never < 90 s between actions
        public static int WHi = 15, WJump = 15, WBig = 10;
        public static double IdleGrace = 300;             // PC in use? skip if idle > 5 min
        public static double WelcomeBackAfter = 600;      // idle ≥ 10 min → "hi" on return
        public static double ClingDist = 200;             // pointer still within ~200 pt…
        public static double ClingStill = 2;              // …for > 2 s → walk over
        public static double ClingLeave = 10;             // walks home after pointer gone 10 s
        public static double LookDist = 300;              // eyes follow the pointer within ~300 pt
    }

    public event System.Action? Changed;
    public void Publish() => Changed?.Invoke();
    void Set<T>(ref T field, T value) { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; Publish(); } }

    bool hovered; public bool Hovered { get => hovered; set => Set(ref hovered, value); }
    int runFacing; public int RunFacing { get => runFacing; set => Set(ref runFacing, value); }      // zoomies: 0 = not running · 1 = running right · -1 = left
    double runPhase; public double RunPhase { get => runPhase; set => Set(ref runPhase, value); }   // gallop phase — driven per frame
    double runStride = 38; public double RunStride { get => runStride; set => Set(ref runStride, value); }   // 38 = gallop · 22 = a walk
    double runLean; public double RunLean { get => runLean; set => Set(ref runLean, value); }       // + leans into the scramble, − leans back in a skid
    double runSpeed; public double RunSpeed { get => runSpeed; set => Set(ref runSpeed, value); }   // 0 … 1 of top speed
    int runGait; public int RunGait { get => runGait; set => Set(ref runGait, value); }             // 0 run · 1 scramble · 2 skid
    double climbRot; public double ClimbRot { get => climbRot; set => Set(ref climbRot, value); }   // ±90° turns the runner upright against the wall
    bool airborne; public bool Airborne { get => airborne; set => Set(ref airborne, value); }       // falling / flung: wide eyes, no pin
    DateTime petUntil = DateTime.MinValue; public DateTime PetUntil { get => petUntil; set => Set(ref petUntil, value); }   // petted: "^ ^" eyes until then
    Action action; public Action Act { get => action; private set => Set(ref action, value); }
    string? hiBubble; public string? HiBubble { get => hiBubble; set => Set(ref hiBubble, value); }
    bool jumpCrouch; public bool JumpCrouch { get => jumpCrouch; set => Set(ref jumpCrouch, value); }   // the take-off crouch, before the leap
    double jumpFacing = 1; public double JumpFacing { get => jumpFacing; set => Set(ref jumpFacing, value); }   // 1 = leaps right · -1 = left
    /// A longer nap (on her own): seconds the curled-up middle is held — as ONE still frame (Emote.Frame `hold`).
    double napHold; public double NapHold { get => napHold; set => Set(ref napHold, value); }

    // ── a squish from the feet that springs back (SwiftUI spring: response 0.28 s, damping 0.42) ──
    double squashTo = 1, squashFrom = 1; DateTime squashAt = DateTime.MinValue;
    public double SquashNow()
    {
        double e = (DateTime.Now - squashAt).TotalSeconds;
        if (e <= 0) return squashFrom;
        double w = 2 * Math.PI / 0.28, z = 0.42, wd = w * Math.Sqrt(1 - z * z);
        double k = Math.Exp(-z * w * e) * (Math.Cos(wd * e) + z * w / wd * Math.Sin(wd * e));
        return squashTo + (squashFrom - squashTo) * k;
    }
    public bool SquashActive => squashTo != 1 || (DateTime.Now - squashAt).TotalSeconds < 1.3;
    void SetSquash(double v) { squashFrom = SquashNow(); squashTo = v; squashAt = DateTime.Now; Publish(); }
    /// A squish from the feet that springs back — landings, a pet, being put down.
    public void Land(double amount = 0.8)
    {
        if (Fx.Reduced) return;
        SetSquash(amount);
        Later.After(0.09, () => SetSquash(1));
    }
    /// A click (owner 27 Sep 2026: click = pet): happy eyes, a purr, a little squish.
    public void Pet()
    {
        PetUntil = DateTime.Now.AddSeconds(1.8);
        Land(0.9);
        CatSound.PurrStart();
        Later.After(1.6, () => { if (DateTime.Now >= PetUntil.AddSeconds(-0.25)) CatSound.PurrStop(); });
        Later.After(1.85, Publish);                                    // the "^ ^" eyes end: redraw the still frame
    }

    public CatModel()
    {
        InitSign();                                                    // cataholic:// links
        ScheduleNext(initial: true);
        if (Tune.Autonomous) Later.Every(1.0, _ => TickSecond());
        // The idle nap: the still frame is drawn awake (`look`) while you are around and asleep after 5 minutes away. A
        // still frame never redraws by itself, so look every 15 s whether that line was crossed (one cheap check, no drawing).
        bool away = Win32.IdleSeconds >= 300;
        Later.Every(15, _ => { bool a = Win32.IdleSeconds >= 300; if (a != away) { away = a; Publish(); } });
    }

    // ── cat-life behaviour scheduler (owner, 26 Sep 2026) ──
    Later? schedWork, actionWork;
    DateTime lastActionAt = DateTime.MinValue;
    /// When the current action started (drives the jump crouch → leap split and every emote's clock).
    public DateTime LastActionStarted = DateTime.MinValue;
    DateTime lastHiAt = DateTime.MinValue, lastIdleAt = DateTime.Now;
    bool wasIdleLong;
    Point lastPointer;
    DateTime? pointerStillSince;
    bool clingAway;                                // clingy moved it off its home spot; it walks back when the pointer leaves
    DateTime? clingLeaveSince;
    /// Slid fully out of its tuck: hovered or mid-action.
    public bool IsOut => Hovered || Act != Action.None;

    /// Right now inside a held nap — the view draws a still frame (no 30 fps clock).
    public bool NapStill
    {
        get
        {
            if (Act != Action.Nap || NapHold <= 0) return false;
            double e = (DateTime.Now - LastActionStarted).TotalSeconds;
            return e >= Emote.NapHoldAt && e < Emote.NapHoldAt + NapHold;
        }
    }
    /// The frame changes with the clock (an emote, the crouch, a landing squish) — not only when a mover publishes.
    public bool ClockDriven => (Act != Action.None && !NapStill) || JumpCrouch || SquashActive;

    /// Any autonomous action in progress stops instantly on a click (the click wins).
    public void StopAction()
    {
        Touched();
        actionWork?.Cancel(); actionWork = null;
        Act = Action.None;
        NapHold = 0; HiBubble = null; JumpCrouch = false; JumpFacing = 1;
        clingAway = false;
        CatPanel.Refit();
    }

    bool LifeOff => CatPrefs.Quiet || Fx.Reduced || Fx.Snapshot;
    static double MacIdle() => Win32.IdleSeconds;
    static bool UserTypingOrDragging => Win32.AnyMouseButton;

    /// On her own, continuous (owner 13 Oct 2026: "no 10 or 20 sec also, just keep doing something"): every point where
    /// she settles calls this, and the next thing starts after a short beat. After a touch (a click, a drag, a menu pick)
    /// the beat is 3–5 s, so she doesn't run off from under your hand.
    public void Chain(double lo = 0.3, double hi = 1.5)
    {
        if (!CatPrefs.OnHerOwn) return;
        ScheduleNext(after: Math.Max(Rnd.In(lo, hi), (touchQuiet - DateTime.Now).TotalSeconds));
    }
    /// A touch: the next thing waits 3–5 s (StopAction is the one funnel every click / drag / menu pick goes through).
    DateTime touchQuiet = DateTime.MinValue;
    public void Touched()
    {
        touchQuiet = DateTime.Now.AddSeconds(Rnd.In(3, 5));
        Chain();
    }
    bool awayIdle;                                 // on her own: you've been away > 5 min — she stops until you're back

    public void ScheduleNext(bool initial = false, double? after = null)
    {
        schedWork?.Cancel();
        if (CatPrefs.OnHerOwn)
        {
            // `after` = a beat from Chain(); else the watchdog — a finish that never reports (StopLife cancels a glide
            // without calling `done`) never stalls her: OwnTurn re-arms it after every try.
            schedWork = Later.After(after ?? (initial ? 1 : 4), OwnTurn);
            return;
        }
        // Weighted mean ~6 min (180–540 s), always ≥ 90 s after the last action.
        double wait = Rnd.In(180, 540);
        if (initial) wait = Rnd.In(60, 180);
        wait = Math.Max(wait, Tune.MinGap - (DateTime.Now - lastActionAt).TotalSeconds);
        schedWork = Later.After(wait, () => { FireNext(); ScheduleNext(); });
    }

    /// One turn on her own: pick the next thing (CatOwn) and start it. Silent, like every life action (owner 27 Sep 2026:
    /// sounds "only when you interact"); never while you hold her, point at her, have her menu open, mid-fall, hidden, in
    /// Sleep mode / Reduce Motion, or over a full-screen app. Away > 5 min → she stops, and looks every 5 s for you.
    void OwnTurn()
    {
        double idle = MacIdle();
        if (idle > Tune.IdleGrace) awayIdle = true;
        if (awayIdle && idle < 2) awayIdle = false;
        if (awayIdle) { ScheduleNext(after: 5); return; }
        if (!(CatPrefs.ShowCrew && !LifeOff && Act == Action.None && !CatPanel.PointerOnHer && !CatPanel.Zooming && !Airborne
              && !CatPanel.Busy && !CatMove.Holding && CatPanel.Visible && !CatMenuPanel.IsOpen
              && (touchQuiet - DateTime.Now).TotalSeconds <= 0 && !CatPanel.FullScreenInFront()))
        { ScheduleNext(after: 1); return; }
        ScheduleNext();                                    // the watchdog; whatever she starts re-arms it sooner (Chain)
        CatOwn.Turn(this, hiOK: (DateTime.Now - lastHiAt).TotalSeconds > 1800);
    }

    void FireNext()
    {
        if (!Tune.Autonomous || LifeOff || Act != Action.None || CatPanel.Zooming) return;
        if (MacIdle() >= Tune.IdleGrace) return;           // PC in use only
        bool hiOK = (DateTime.Now - lastHiAt).TotalSeconds > 1800;
        var pool = new List<(Action, int)> { (Action.Stretch, 9), (Action.Yawn, 9), (Action.Groom, 9), (Action.Spin, 9), (Action.Look, 9) };
        if (hiOK) pool.Add((Action.Hi, Tune.WHi));
        pool.Add((Action.Jump, Tune.WJump)); pool.Add((Action.Big, Tune.WBig));
        int total = 0; foreach (var p in pool) total += p.Item2;
        int r = Rnd.R.Next(total);
        var pick = Action.Look;
        foreach (var (a, wgt) in pool) { if (r < wgt) { pick = a; break; } r -= wgt; }
        if (CatPanel.Zooming) return;
        StartAction(pick);
    }

    /// Duration per action; visuals reset when it ends. Jump moves the panel (CatPanel.LifeJump). One function per
    /// action: the menu and the scheduler BOTH call StartAction — the Play… helpers own the side effects.
    public void StartAction(Action a, double? overrideDur = null, bool user = false)
    {
        if (Act != Action.None || CatPanel.Zooming) return;
        // Autonomous runs need a quiet, still PC; a direct pick from the menu is the user asking NOW — `user` skips the
        // quiet gates (Reduce Motion still plays it as a still pose).
        if (!(user || !LifeOff)) return;
        Act = a;
        lastActionAt = DateTime.Now;
        LastActionStarted = DateTime.Now;
        CatPanel.Refit();    // the panel grows to the pose BEFORE it moves or scales (no clipped first frame)
        bool still = Fx.Reduced;
        double dur;
        switch (a)
        {
            case Action.Hi: lastHiAt = DateTime.Now; HiBubble = "hi"; dur = Emote.Duration(a); break;
            case Action.Jump:
                if (!still) { JumpCrouch = true; CatPanel.LifeJump(); }   // LifeJump clears the crouch at Emote.JumpTakeoff, when the leap starts
                dur = Emote.Duration(a); break;
            case Action.Clingy: dur = 2.0; break;
            case Action.None: return;
            default: dur = Emote.Duration(a); break;
        }
        double end = overrideDur ?? dur;
        if (a == Action.Nap && end > dur && !still)
        {
            // the view only re-reads `NapStill` when the model publishes: once as the hold starts, once as it ends
            NapHold = end - dur;
            foreach (double at in new[] { Emote.NapHoldAt + 0.05, Emote.NapHoldAt + NapHold + 0.01 }) Later.After(at, Publish);
        }
        actionWork?.Cancel();
        actionWork = Later.After(end, EndAction);
    }

    /// A menu pick: do the action NOW, through the same StartAction the scheduler calls.
    public void Tap(Action a)
    {
        Touched();
        StartAction(a, user: true);
    }

    void EndAction()
    {
        actionWork = null;
        HiBubble = null; JumpCrouch = false; JumpFacing = 1;
        Act = Action.None;          // every performance has already settled back to the resting pose by now
        NapHold = 0;
        CatPanel.Refit();
        Chain();
    }

    /// Once-a-second watch: welcome-back hi + clingy proximity (only with Tune.Autonomous — an idle cat sits still).
    void TickSecond()
    {
        if (!Tune.Autonomous || LifeOff) return;
        double idle = MacIdle();
        if (idle >= Tune.WelcomeBackAfter) { wasIdleLong = true; lastIdleAt = DateTime.Now; return; }
        if (wasIdleLong && idle < 5)
        {
            // The user is back after ≥ 10 min idle → "hi" once.
            wasIdleLong = false; lastIdleAt = DateTime.Now;
            if (Act == Action.None && !CatPanel.Zooming && (DateTime.Now - lastHiAt).TotalSeconds > 1800) StartAction(Action.Hi);
            return;
        }
        if (!(idle < Tune.IdleGrace && !CatPanel.Zooming)) return;
        // Clingy: pointer still within ~200 pt of the cat for > 2 s → walk over and rub (2 s sway, half-closed eyes),
        // settle next to it; walk home after the pointer leaves for 10 s. Never while dragging.
        var p = Display.Pointer();
        if (p == lastPointer) { pointerStillSince ??= DateTime.Now; } else pointerStillSince = null;
        lastPointer = p;
        if (clingAway)
        {
            // It stays settled next to the pointer; home again once the pointer has been away from the cat for 10 s.
            if (CatPanel.CatCenter() is Point c && (p - c).Length < Tune.ClingDist) clingLeaveSince = null;
            else if (clingLeaveSince == null) clingLeaveSince = DateTime.Now;
            else if ((DateTime.Now - clingLeaveSince.Value).TotalSeconds > Tune.ClingLeave)
            {
                CatPanel.LifeGlideHome();
                clingAway = false; clingLeaveSince = null;
                if (Act == Action.Clingy) EndAction();
            }
        }
        if (!(Act == Action.None && !UserTypingOrDragging)) return;
        if (!clingAway && pointerStillSince is DateTime still && (DateTime.Now - still).TotalSeconds > Tune.ClingStill
            && CatPanel.CatCenter() is Point c2 && (p - c2).Length < Tune.ClingDist)
        {
            clingAway = true;
            CatPanel.LifeCling(p);
            StartAction(Action.Clingy);
        }
    }
}

// Sleep + show toggles live in the prefs file (same pattern as the notch toggle on the Mac).
static class CatPrefs
{
    public static bool ShowCrew => Prefs.Bool("crewEnabled", true);
    public static void SetShowCrew(bool on)
    {
        Prefs.Set("crewEnabled", on);
        if (on) CatPanel.Show(); else CatPanel.Hide();
        CatHerd.Shared.Restart();
    }
    /// Sleep mode (the Mac's "Quiet", renamed in 0.6.2): she lies curled up and still.
    public static bool Quiet => Prefs.Bool("crewQuiet", false);
    public static void SetQuiet(bool on)
    {
        Prefs.Set("crewQuiet", on);
        CatPanel.ApplyPrefs();
        CatHerd.Shared.Restart();                  // Sleep mode sends the herd home
    }
    /// On her own: the cat does the "Do something" actions and walks about while you work (menu toggle, off by default).
    public static bool OnHerOwn => Prefs.Bool("crewOnHerOwn", false);
    public static void SetOnHerOwn(bool on)
    {
        Prefs.Set("crewOnHerOwn", on);
        CatPanel.CurrentModel?.ScheduleNext(initial: true);     // on: the first one right away
    }
}

// ── emotes as little performances (owner 13 Oct 2026: "redesign how emotes work, like the movements can we little more
// animated") ──
//
// Each of the nine actions is a short piece of cartoon timing on the clock — anticipation → action → follow-through →
// settle — instead of a held pose. `Emote.Frame(action, t)` is a pure function: CatView calls it on every frame of the
// 30 Hz clock that already runs while an action plays (an idle cat draws one still frame). Every performance ends on the
// resting skin with every channel at rest, so the end of an action is not a pop. Calm: no wobble faster than ~3 Hz.

/// One frame of an emote: what CatView applies to the cat this instant. The defaults are today's still pose.
sealed class EmoteFrame
{
    public enum SkinKind { Own, Rest, Alert, Crouch, Leap, Yawn }
    public SkinKind Skin = SkinKind.Own;        // own = the action's drawing · rest = the resting cat · the jump's two drawings
    public double Sx = 1, Sy = 1;               // squash & stretch about the feet
    public double Rot;                          // lean about the feet (deg, + = clockwise; mirrored with the jump)
    public double Lift;                         // up off the ground (1× pt)
    public double Spin;                         // chase tail: the turn (deg) — the cat's width is cos(spin)
    public double HeadTurn, HeadTilt;
    public double Pupil;                        // eyes to the side (pt)
    public double Paw = 1, Wave;                // hi: arm raise (0 down … 1 up), wave (deg)
    public double Wrist;                        // hi / groom: the mitten's wrist bend (deg)
    public double Breath;                       // nap: the curled back rising (1) / falling (0)
    public bool Peek;                           // nap: one eye opens before it wakes
    public Point? GroomPaw = new Point(27.5, 27.5);   // groom: the licking paw (null = down)
    public bool Tongue = true;                  // groom: tongue out
    public double Mouth = 1;                    // yawn: 0 shut … 1 wide
    public double Ears;                         // 0 up … 1 flattened back
    public double EarTwitch;
    public double Tail;                         // tail sway (pt)
    public CatEyes? Eyes;
    public double Bubble = 1, BubbleAlpha = 1;  // the "hi" bubble pops in and fades
    public (double Rise, double Opacity) Z;     // nap: the drifting z (rise, opacity)
}

static class Emote
{
    /// The jump's crouch, then its flight — CatPanel.LifeJump starts the travel at `JumpTakeoff` and glides `JumpFlight`.
    public const double JumpTakeoff = 0.35;
    public const double JumpFlight = 0.9;
    /// A held nap freezes here: curled up, mid-breath, the z gone (CatModel.NapHold).
    public const double NapHoldAt = 3.1;

    public static double Duration(CatModel.Action a) => a switch
    {
        CatModel.Action.Hi => 2.0,
        CatModel.Action.Big => 2.3,
        CatModel.Action.Stretch => 2.4,
        CatModel.Action.Yawn => 1.9,
        CatModel.Action.Groom => 2.5,
        CatModel.Action.Spin => 2.2,
        CatModel.Action.Look => 2.6,
        CatModel.Action.Jump => JumpTakeoff + JumpFlight + 0.5,
        CatModel.Action.Nap => 4.6,
        CatModel.Action.Clingy => 2.0,
        _ => 0,
    };

    // Timing curves. u runs 0 → 1 across a segment.
    static double Seg(double t, double a, double b) => Math.Min(Math.Max((t - a) / (b - a), 0), 1);
    static double Smooth(double u) => u * u * (3 - 2 * u);
    static double EaseIn(double u) => u * u * u;
    static double EaseInOut(double u) => u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
    /// Overshoots a little and lands on 1 (k = how far past).
    static double Back(double u, double k = 1.7) { double v = u - 1; return 1 + v * v * ((k + 1) * v + k); }
    /// 0 → 1 → 0.
    static double Bump(double u) => Math.Sin(Math.Min(Math.Max(u, 0), 1) * Math.PI);
    /// A settle that starts at 1, rings `cycles` times and is exactly 0 at u = 1 (only inside its segment).
    static double Ring(double u, double cycles, double decay = 3.5) =>
        u >= 1 ? 0 : Math.Cos(u * cycles * 2 * Math.PI) * Math.Exp(-decay * u) * (1 - u);
    /// A shake that starts and ends at 0 and dies away.
    static double Wob(double u, double cycles, double decay = 3) =>
        u <= 0 || u >= 1 ? 0 : Math.Sin(u * cycles * 2 * Math.PI) * Math.Exp(-decay * u) * (1 - u);
    static Point Mix(Point a, Point b, double k) => new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);

    public static EmoteFrame Frame(CatModel.Action a, double t0, bool hanging = false, double hold = 0)
    {
        var f = new EmoteFrame();
        // A held nap: the clock stops at NapHoldAt for `hold` seconds, then the wake-up plays as usual.
        double t = t0; bool held = false;
        if (a == CatModel.Action.Nap && hold > 0 && t0 >= NapHoldAt) { if (t0 < NapHoldAt + hold) { t = NapHoldAt; held = true; } else t = t0 - hold; }
        if (t >= Duration(a)) { f.Skin = EmoteFrame.SkinKind.Rest; return f; }
        switch (a)
        {
            case CatModel.Action.Hi:
            {
                // dip → the paw shoots up (overshoot) → 2½ waves, body and head swaying a beat behind → paw drops → settle
                double dip = Bump(Seg(t, 0, 0.2)) * 0.06;
                double down = Smooth(Seg(t, 1.4, 1.7));
                f.Paw = Back(Seg(t, 0.12, 0.42), 1.4) * (1 - down);
                double w = Seg(t, 0.38, 1.4), env = Math.Min(1, w * 5) * Math.Min(1, (1 - w) * 5), ph = w * 2.5 * 2 * Math.PI;
                f.Wave = Math.Sin(ph) * 22 * env;
                // the mitten trails the arm (wrist lag), then one quick flick before it tucks away
                f.Wrist = Math.Sin(ph - 1.0) * 28 * env + Wob(Seg(t, 1.32, 1.62), 1.5, 2) * 40;
                double reach = Bump(Seg(t, 0.15, 0.5)) * 0.05, settle = Wob(Seg(t, 1.62, 2.0), 1.5);
                f.Sy = 1 - dip + reach - settle * 0.05;
                f.Sx = 1 + dip * 0.7 - reach * 0.5 + settle * 0.035;
                f.Rot = Math.Sin(ph - 0.7) * 2.2 * env;
                f.HeadTilt = -4 * f.Paw * (1 - down) - Math.Sin(ph - 0.35) * 3 * env;
                f.EarTwitch = Bump(Seg(t, 0.42, 0.62));
                f.Tail = Math.Sin(t * 2 * Math.PI * 1.1) * 2.4 * (1 - Smooth(Seg(t, 1.5, 1.95)));
                if (t > 0.75 && t < 1.15) f.Eyes = CatEyes.Happy;
                double pop = Seg(t, 0.18, 0.45);
                f.Bubble = 0.4 + 0.6 * Back(pop, 2.0);
                f.BubbleAlpha = Math.Min(1, pop * 4) * (1 - Smooth(Seg(t, 1.6, 1.9)));
                if (t > 1.7) f.Skin = EmoteFrame.SkinKind.Rest;
                break;
            }
            case CatModel.Action.Big:
            {
                // crouch, ears flat → PUFF (fur up, eyes wide, overshoot) → a held shiver → deflate → squash, shake it off
                double c = Smooth(Seg(t, 0, 0.28)) * (1 - Smooth(Seg(t, 0.28, 0.42)));
                double p = Seg(t, 0.28, 0.58), d = Seg(t, 1.4, 1.78);
                double S = 1 + 0.8 * Back(p, 1.6) - 0.86 * EaseIn(d);
                f.Sx = S + 0.1 * EaseIn(d) + c * 0.1;
                f.Sy = S - c * 0.14;
                f.Ears = c;
                f.Rot = Math.Sin((t - 0.58) * 2 * Math.PI * 2.5) * 1.0 * Bump(Seg(t, 0.58, 1.4)) + Wob(Seg(t, 1.85, 2.3), 2) * 3;
                f.Tail = Math.Sin(t * 2 * Math.PI * 1.4) * 1.5 * Bump(Seg(t, 0.5, 1.5));
                if (t >= 1.78)
                {
                    double r = Ring(Seg(t, 1.78, 2.3), 1.2);
                    f.Sx = 1 + 0.04 * r; f.Sy = 1 - 0.06 * r;
                }
                if (t >= 0.3 && t < 1.62) f.Skin = EmoteFrame.SkinKind.Alert; else if (t >= 1.62) f.Skin = EmoteFrame.SkinKind.Rest;
                break;
            }
            case CatModel.Action.Stretch:
                // lean back → down into the bow, front paws reaching (overshoot), tail waving → gather up → sit, shake it off
                if (t < 0.3)
                {
                    double k = Smooth(Seg(t, 0, 0.3));
                    f.Skin = EmoteFrame.SkinKind.Rest; f.Rot = -3 * k; f.Sx = 1 + 0.03 * k; f.Sy = 1 - 0.04 * k; f.Eyes = CatEyes.Shut;
                }
                else if (t < 1.75)
                {
                    double k = Seg(t, 0.3, 0.65), g = Smooth(Seg(t, 1.5, 1.75)), hold2 = Bump(Seg(t, 0.65, 1.5));
                    f.Sx = 0.84 + 0.22 * Back(k, 1.5) + 0.035 * Math.Sin((t - 0.65) * 2 * Math.PI * 1.2) * hold2 - 0.12 * g;   // two slow luxurious pushes
                    f.Sy = 1.05 - 0.07 * Smooth(k) + 0.04 * g - 0.02 * hold2;
                    f.Rot = -2.5 * hold2;
                    f.Tail = Math.Sin((t - 0.3) * 2 * Math.PI * 0.8) * 2.5;
                }
                else
                {
                    double u = Seg(t, 1.75, 2.4), r = Ring(u, 1.0);
                    f.Skin = EmoteFrame.SkinKind.Rest; f.Rot = Wob(u, 2.5, 2.5) * 4.5; f.Sy = 1 - 0.05 * Math.Max(r, 0); f.Sx = 1 + 0.04 * r;   // never taller: the pin sits at the panel's top edge
                    if (t < 2.15) f.Eyes = CatEyes.Happy;
                }
                break;
            case CatModel.Action.Yawn:
            {
                // a small dip, eyes squeeze → the mouth opens wide as it stretches tall, ears back → hold → snap shut → sleepy blink
                double open = Smooth(Seg(t, 0.2, 0.7)) * (1 - EaseIn(Seg(t, 1.05, 1.3)));
                f.Mouth = open * (1 + 0.05 * Math.Sin((t - 0.7) * 2 * Math.PI * 2) * Bump(Seg(t, 0.7, 1.05)));
                double dip = Bump(Seg(t, 0, 0.25)) * 0.03, settle = Wob(Seg(t, 1.25, 1.9), 1.5);
                f.Sy = 1 - dip + 0.06 * open - settle * 0.04;
                f.Sx = 1 + dip * 0.6 - 0.03 * open + settle * 0.03;
                f.HeadTilt = -4 * open;
                f.Ears = 0.6 * open;
                f.Eyes = CatEyes.Shut;
                if (t >= 1.3) { f.Skin = EmoteFrame.SkinKind.Rest; f.Eyes = t < 1.62 ? CatEyes.Half : (t < 1.7 ? CatEyes.Shut : null); }
                break;
            }
            case CatModel.Action.Groom:
            {
                // the paw comes up to the mouth → three licks, head bobbing into each → a wipe over the brow to the ear → paw down
                var rest = new Point(29.5, 36.5); var mouthAt = new Point(27.5, 27.5); var ear = new Point(31, 15);
                double up = Back(Seg(t, 0.05, 0.35), 1.3), down = Smooth(Seg(t, 1.95, 2.2));
                var p = Mix(rest, mouthAt, up);
                bool licking = t > 0.35 && t < 1.5;
                double lk = Seg(t, 0.35, 1.5) * 3, fr = lk - Math.Floor(lk), lick = licking ? Bump(fr) : 0;
                p.Y -= 2.4 * lick; p.X += 0.6 * lick;
                double w = Seg(t, 1.5, 1.95), wb = Bump(w);
                if (w > 0) { p = Mix(mouthAt, ear, Smooth(wb)); p.X += 2 * wb; }
                p = Mix(p, rest, down);
                f.GroomPaw = t < 2.2 ? p : null;
                f.Tongue = licking && fr > 0.15 && fr < 0.75;
                f.Wrist = -35 * up * (1 - down) + 18 * lick - 25 * wb;          // paw curled in to the face, flexing on each lick
                f.HeadTilt = 5 * up * (1 - down) + 2.5 * lick + 6 * wb;
                f.Eyes = t > 0.3 && t < 1.95 ? CatEyes.Shut : (t >= 1.95 ? CatEyes.Open : null);
                f.EarTwitch = 0;
                f.Tail = Math.Sin(t * 2 * Math.PI * 0.7) * 1.8 * (1 - Smooth(Seg(t, 2.0, 2.45)));
                if (t >= 2.2)
                {
                    double u = Seg(t, 2.2, 2.5);
                    f.Skin = EmoteFrame.SkinKind.Rest; f.Eyes = null;
                    f.Sy = 1 - Wob(u, 1.5) * 0.035; f.Sx = 1 + Wob(u, 1.5) * 0.03; f.Rot = Wob(u, 1.5) * 2;
                }
                break;
            }
            case CatModel.Action.Spin:
            {
                // spots the tail (head + eyes turn, tail flicks), crouches → two turns that speed up and slow down, a hop
                // in the middle → lands with a squash, dizzy wobble
                double c = Smooth(Seg(t, 0, 0.4)) * (1 - Smooth(Seg(t, 0.4, 0.55)));
                double notice = Smooth(Seg(t, 0, 0.3)) * (1 - Smooth(Seg(t, 0.4, 0.6)));
                f.Skin = EmoteFrame.SkinKind.Rest;
                f.HeadTurn = 3 * notice; f.Pupil = 1.5 * notice;
                f.Tail = Math.Sin(t * 2 * Math.PI * 2.2) * 3.2 * notice;
                double u = Seg(t, 0.4, 1.6);
                f.Spin = 720 * EaseInOut(u);
                f.Lift = 4 * Bump(u);
                f.Rot = 4 * Bump(u);
                f.Sy = 1 - 0.08 * c + 0.03 * Bump(u); f.Sx = 1 + 0.05 * c;
                if (t >= 1.6)
                {
                    double v = Seg(t, 1.6, 2.2), r = Ring(v, 1.2);
                    f.Rot = Wob(v, 2, 2.2) * 6; f.HeadTilt = -Wob(v, 2, 2.2) * 5;
                    f.Sy = 1 - 0.06 * r; f.Sx = 1 + 0.05 * r;
                    if (v < 0.7) f.Eyes = CatEyes.Happy;
                }
                break;
            }
            case CatModel.Action.Look:
            {
                // glance left (a touch past) → hold, ear flick → swing right → hold, slow blink → back to centre, a nod
                static double Turn(double tt) =>
                    -3 * Back(Seg(tt, 0.15, 0.5), 1.2) + 6 * Back(Seg(tt, 1.0, 1.45), 1.2) - 3 * Smooth(Seg(tt, 1.95, 2.35));
                double h = Turn(t);
                f.HeadTurn = h;
                f.HeadTilt = h * 2 + Wob(Seg(t, 2.3, 2.6), 1.5) * 2.5;
                f.Pupil = h * 0.5;
                f.Rot = Turn(t - 0.12) * 0.5;                           // the body follows a beat behind the head
                f.EarTwitch = Bump(Seg(t, 0.65, 0.85)) + Bump(Seg(t, 1.6, 1.8));
                if (t > 1.72 && t < 1.84) f.Eyes = CatEyes.Shut;
                f.Tail = Math.Sin(t * 2 * Math.PI * 0.6) * 2 * Bump(Seg(t, 0, 2.6));
                break;
            }
            case CatModel.Action.Jump:
            {
                // crouch low with a wiggle → the leap stretches long, nose up then down → lands front-on with a squash
                double j0 = JumpTakeoff, j1 = JumpTakeoff + JumpFlight;
                if (t < j0)
                {
                    double c = Smooth(Seg(t, 0, 0.18));
                    f.Skin = EmoteFrame.SkinKind.Crouch; f.Sy = 1 - 0.1 * c; f.Sx = 1 + 0.07 * c;
                    f.Rot = Wob(Seg(t, 0.08, j0), 1, 1) * 2.5;
                }
                else if (t < j1)
                {
                    double u = Seg(t, j0, j1);
                    f.Skin = EmoteFrame.SkinKind.Leap;
                    f.Sx = 1 + 0.08 * Bump(u); f.Sy = 1 - 0.05 * Bump(u);
                    f.Rot = -7 + 14 * Smooth(u);
                    f.Lift = 4 * Bump(u);
                }
                else
                {
                    double r = Ring(Seg(t, j1, Duration(CatModel.Action.Jump)), 1.3);
                    f.Skin = EmoteFrame.SkinKind.Rest; f.Sy = 1 - 0.15 * Math.Max(r, 0); f.Sx = 1 + 0.11 * r;   // never taller: the jump's panel ends at the pin
                    if (t < j1 + 0.18) f.Eyes = CatEyes.Shut;
                }
                break;
            }
            case CatModel.Action.Nap:
            {
                // a real cat nap (owner 13 Oct 2026): drowsy, head nods → turns once round its spot and sinks → curled into
                // a loaf, tail wrapped, head on its paws, two slow breaths, z's drift up → one eye peeks open → sits up with
                // a stretch-yawn → blinks awake. On the pin there is no floor to curl on: it dozes sitting, head nodding.
                double breath = 0.5 - 0.5 * Math.Cos(Seg(t, 1.0, 3.3) * 2 * 2 * Math.PI);          // two slow breaths, from and back to 0
                double zu = Seg(t, 1.1, 3.2);
                var z = (Rise: zu * 12, Opacity: zu <= 0 || zu >= 1 ? 0 : (zu < 0.15 ? zu / 0.15 : 1 - Math.Max(0, zu - 0.7) / 0.3));
                if (t < 0.5)
                {
                    double d = Smooth(Seg(t, 0, 0.5));
                    f.Skin = EmoteFrame.SkinKind.Rest; f.Eyes = t < 0.25 ? CatEyes.Half : CatEyes.Shut;
                    f.HeadTilt = 5 * d; f.Sy = 1 - 0.03 * d; f.Sx = 1 + 0.02 * d; f.Ears = 0.4 * d;
                }
                else if (t < 0.95 && !hanging)
                {
                    double u = Seg(t, 0.5, 0.95);
                    f.Skin = EmoteFrame.SkinKind.Rest; f.Eyes = CatEyes.Shut;
                    f.Spin = 360 * EaseInOut(u);                                         // turns once round its spot…
                    f.Sy = 1 - 0.18 * Smooth(u); f.Sx = 1 + 0.08 * Smooth(u);            // …sinking as it goes
                }
                else if (t < 3.55)
                {
                    f.Skin = EmoteFrame.SkinKind.Own;
                    f.Breath = breath;
                    f.Z = z;
                    if (hanging) { f.HeadTilt = 6 + 2 * breath; f.Sy = 1 + 0.03 * breath; f.Ears = 0.4; }
                    else
                    {
                        double r = Ring(Seg(t, 0.95, 1.35), 1, 3);                       // flops into the curl with a squash
                        f.Sy = 1 - 0.12 * r; f.Sx = 1 + 0.07 * r;
                    }
                    f.Peek = t > 3.2;
                    if (hanging && t > 3.2) f.Eyes = CatEyes.Half;
                }
                else if (t < 4.15)
                {
                    double u = Seg(t, 3.55, 4.15), o = Bump(u);
                    f.Skin = EmoteFrame.SkinKind.Yawn; f.Mouth = o; f.Eyes = CatEyes.Shut;               // up to sitting with a stretch-yawn
                    f.Sy = 1 + 0.07 * o - 0.08 * Ring(Seg(t, 3.55, 3.8), 1, 3); f.Sx = 1 - 0.03 * o;
                    f.HeadTilt = -4 * o; f.Ears = 0.5 * o;
                }
                else
                {
                    double v = Seg(t, 4.15, 4.6);
                    f.Skin = EmoteFrame.SkinKind.Rest; f.Eyes = v < 0.4 ? CatEyes.Half : (v < 0.55 ? CatEyes.Shut : null);
                    f.Sy = 1 - 0.03 * Wob(v, 1.5); f.EarTwitch = Bump(Seg(t, 4.3, 4.5));
                }
                break;
            }
        }
        if (held) f.Z = (f.Z.Rise, 0);
        return f;
    }
}
