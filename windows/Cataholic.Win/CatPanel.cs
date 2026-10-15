using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace Cataholic;

// CatPanel — the borderless, never-activating window the cat lives in (CrewPanel.swift): always on top, no taskbar
// button, no Alt-Tab, clear pixels pass clicks through, and it is only as big as the pose that is drawn, so it never
// blocks clicks elsewhere. Also everything that moves her: settle home, drop/throw with gravity, the perch watch,
// zoomies, glide / hop / climb / slip / bump, lifeJump / lifeCling. Layout and drawing are 1× points (CatLayout); this
// file works in SCREEN points (= DIPs on the primary monitor, y down), so every cat-sized number here is × CatLayout.Zoom.
static class CatPanel
{
    static CatWindow? panel;
    static CatModel? model;

    public static CatModel? CurrentModel => model;
    public static CatWindow? Window => panel;

    public static void Show()
    {
        if (!CatPrefs.ShowCrew) return;
        Display.Refresh();
        if (perchTimer == null) WatchPerch();
        if (model == null) { model = new CatModel(); model.Changed += OnChanged; }
        if (panel != null) { panel.Show(); panel.Refit(); return; }
        panel = new CatWindow(model);
        panel.Show();
        panel.Refit();
        CatHerd.Shared.Restart();                  // the multiplier's herd comes back with the app
    }

    public static void Hide() { roam = null; panel?.Hide(); }
    public static bool Visible => panel?.IsVisible == true;
    /// The pointer is on her — by geometry: MouseLeave can miss the exit when she walks out from under a still pointer.
    public static bool PointerOnHer => CharScreenRect() is Rect r && Rect.Inflate(r, -6 * CatLayout.Zoom, -6 * CatLayout.Zoom).Contains(Display.Pointer());

    // One redraw + refit per turn of the dispatcher, however many properties a mover's tick sets (zoomies set five a frame).
    static bool queued;
    static void OnChanged()
    {
        if (queued || panel == null) return;
        queued = true;
        panel.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new System.Action(() =>
        {
            queued = false;
            panel?.Refit();
            panel?.View.Redraw();
        }));
    }

    // ── gravity (owner 27 Sep 2026, step 2 of "make it lively") ──

    /// Hanging on its pin near the top of the screen (the old home, and the default) — or standing on a floor (the
    /// taskbar line or a window's top edge, CatGravity). Put down near the top → it hangs; anywhere lower → it falls and stands.
    public static bool Hanging => Prefs.Bool("crewHang", true);
    public static void SetHanging(bool on) => Prefs.Set("crewHang", on);
    /// The character box's top-left — where the cat is DRAWN right now.
    public static Point? CharTopLeft() => CharScreenRect() is Rect r ? new Point(r.Left, r.Top) : null;

    /// Remember where the cat now lives (the keys a drag writes) and let the panel sit there.
    public static void SettleHome(double left, double top)
    {
        // The window she now sits on and where along it (on her own she rides it when it slides sideways — CheckPerch)
        var wins = CatGravity.Windows();
        var fl = CatGravity.FloorBelow(left + CatLayout.ScreenW / 2, top + CatGravity.Feet - 3, wins);
        int? id = Math.Abs(fl.Y - top - CatGravity.Feet) > 3 ? null : fl.Window;
        perch = null;
        if (id is int pid) foreach (var w in wins) if (w.Id == pid) { perch = (pid, left - w.Rect.Left, w.Rect.Top); break; }
        Prefs.Set("crewLeft", left - Display.Frame.Left);
        Prefs.Set("crewTop", top);
        roam = null;
        panel?.Refit();
        calmChecks = 0;                                  // a new perch: watch it closely for a moment
    }
    static Later? fallTimer;
    /// The window under her feet: its id, her left minus its left, and its top (SettleHome).
    static (int Id, double Dx, double Top)? perch;

    /// Let go of the cat, or knock it off its perch: it flies with (vx, vy) pt/s (y down), bounces off the screen sides
    /// and the top, and lands on the first floor under it — squash, and one small bounce when it came down hard. Wide
    /// eyes while airborne (CatView).
    public static void Drop(double vx = 0, double vy = 0, int? through = null, System.Action? done = null)
    {
        if (CharTopLeft() is not Point start) { done?.Invoke(); return; }
        fallTimer?.Cancel();
        climbed = false;
        var wins = CatGravity.Windows().Where(w => w.Id != through).ToList();   // `through`: the perch she was knocked off
        double w = CatLayout.ScreenW, feet = CatGravity.Feet;
        double minX = Display.Frame.Left, maxX = Display.Frame.Right - w;
        double ceiling = Display.Visible.Top + 2;
        if (Fx.Reduced)                                    // no flight: straight onto the floor below
        {
            var fl = CatGravity.FloorBelow(start.X + w / 2, start.Y + feet - 3, wins);
            SettleHome(start.X, fl.Y - feet); done?.Invoke(); return;
        }
        double x = start.X, y = start.Y; DateTime last = DateTime.Now; int bounces = 0;
        if (model != null) model.Airborne = true;
        fallTimer = Later.Every(1.0 / 30, t =>               // 30 Hz: she moves a lot on her own (CPU)
        {
            var now = DateTime.Now; double dt = Math.Min((now - last).TotalSeconds, 1.0 / 15); last = now;
            double prevFeet = y + feet;
            vy += CatGravity.G * dt;
            x += vx * dt; y += vy * dt;
            if (x < minX) { x = minX; vx = -vx * 0.55; } else if (x > maxX) { x = maxX; vx = -vx * 0.55; }
            if (y < ceiling) { y = ceiling; vy = Math.Abs(vy) * 0.3; }
            if (vy > 0)
            {
                var fl = CatGravity.FloorBelow(x + w / 2, prevFeet, wins);
                if (y + feet >= fl.Y)
                {
                    y = fl.Y - feet;
                    if (vy > 1100 && bounces == 0)         // came down hard: one little bounce
                    {
                        bounces = 1; vy = -vy * 0.28; vx *= 0.5; model?.Land(0.7);
                    }
                    else
                    {
                        t.Cancel(); fallTimer = null;
                        if (model != null) model.Airborne = false;
                        model?.Land(0.7);
                        SettleHome(x, y);
                        done?.Invoke();
                        model?.Chain();
                        return;
                    }
                }
            }
            roam = (x, y);
            panel?.Refit();
        });
    }

    /// The perch check: the window under it closed, moved or minimised → it falls; the window rose a little under it → it
    /// rides up with it. Every 0.4 s while a mouse button is down (you may be dragging its window) or just after something
    /// changed; once it has sat still for ~2 s it looks only every 1.2 s (battery).
    static Later? perchTimer;
    static int calmChecks, perchTick;
    public static void WatchPerch()
    {
        perchTimer?.Cancel();
        perchTimer = Later.Every(0.4, _ => CheckPerch());
    }
    public static void CheckPerch()
    {
        perchTick++;
        if (Win32.AnyMouseButton) calmChecks = 0;
        if (calmChecks >= 5 && perchTick % 3 != 0) return;
        if (Hanging || model == null || model.Airborne || Zooming || lifeTimer != null || CatMove.Holding || CatPrefs.Quiet
            || panel?.IsVisible != true || CharTopLeft() is not Point at) return;
        var wins = CatGravity.Windows();
        double cx = at.X + CatLayout.ScreenW / 2, feetY = at.Y + CatGravity.Feet;
        // On her own she rides her window when you slide it sideways (same top, new left): she keeps her spot on it.
        if (CatPrefs.OnHerOwn && perch is { } p && wins.FindIndex(w => w.Id == p.Id) is int wi and >= 0 && Math.Abs(wins[wi].Rect.Top - p.Top) < 1)
        {
            double nl = Math.Min(Math.Max(wins[wi].Rect.Left + p.Dx, Display.Frame.Left), Display.Frame.Right - CatLayout.ScreenW);
            if (Math.Abs(nl - at.X) >= 1 && roam == null)
            {
                SettleHome(nl, at.Y);
                calmChecks = 0;
                return;
            }
        }
        if (CatGravity.Supported(cx, feetY, wins)) { calmChecks++; return; }
        calmChecks = 0;
        int ri = wins.FindIndex(w => CatGravity.Ledge(w.Rect) is double l && l < feetY && l > feetY - 90 && cx > w.Rect.Left + 6 && cx < w.Rect.Right - 6);
        if (ri >= 0 && CatGravity.Ledge(wins[ri].Rect) is double top
            && CatGravity.FloorBelow(cx, top - 0.5, wins).Window == wins[ri].Id)
        {
            SettleHome(at.X, top - CatGravity.Feet);
            model.Land(0.88);
            return;
        }
        Drop();
    }

    // ── zoomies (menu → Zoomies) ──

    /// Zoomies, grounded (owner 27 Sep 2026: "can you do better zoomies?"): a hanging cat drops to the floor first. Then
    /// 3–4 dashes along THAT floor — a leg-scramble start, a galloping squash-and-stretch run, sometimes a hop mid-dash, a
    /// skid that leans back, a look around — and it stays where it ends. A click mid-run catches it (`CatchCat`).
    public static bool Zooming { get; private set; }
    static bool caught;
    public static void Zoomies()
    {
        if (Zooming || Fx.Reduced || panel == null || model == null) return;
        Zooming = true; caught = false;
        if (Hanging) { SetHanging(false); Drop(done: Run); } else Run();
    }
    static void Run()
    {
        if (model is not CatModel m || CharTopLeft() is not Point at) { Zooming = false; return; }
        var wins = CatGravity.Windows();
        double w = CatLayout.ScreenW, feet = CatGravity.Feet;
        var fl = CatGravity.FloorBelow(at.X + w / 2, at.Y + feet - 3, wins);
        double lo = Math.Max(fl.X0, Display.Frame.Left) + 4, hi = Math.Min(fl.X1, Display.Frame.Right) - w - 4;
        if (hi - lo <= 80) { Zooming = false; m.Land(); return; }          // no room to run: a squish
        var xs = new List<double> { Math.Min(Math.Max(at.X, lo), hi) };
        for (int n = Rnd.In(3, 4); n > 0; n--)
        {
            double q = xs[^1];
            for (int k = 0; k < 16; k++) { q = Rnd.In(lo, hi); if (Math.Abs(q - xs[^1]) > (hi - lo) * 0.3) break; }
            xs.Add(q);
        }
        var legs = new List<(double A, double B, bool Hop)>();
        for (int k = 0; k + 1 < xs.Count; k++) legs.Add((xs[k], xs[k + 1], Math.Abs(xs[k + 1] - xs[k]) > 300 && Rnd.Bool()));
        double floorTop = fl.Y - feet, z = CatLayout.Zoom;
        const double topSpeed = 560, accel = 2400, brake = 2200;
        int i = 0, stage = 0;                                              // 0 scramble · 1 run · 2 skid · 3 look
        double x = xs[0], v = 0, phase = 0, pause = 0.45;
        DateTime stageT = DateTime.Now, last = DateTime.Now;
        m.RunStride = 38;
        Later.Every(1.0 / 30, t =>
        {
            var now = DateTime.Now; double dt = Math.Min((now - last).TotalSeconds, 1.0 / 15); last = now;
            double se = (now - stageT).TotalSeconds;
            if (caught || i >= legs.Count)
            {
                t.Cancel();
                m.RunFacing = 0; m.RunPhase = 0; m.RunLean = 0; m.RunSpeed = 0; m.RunGait = 0;
                Zooming = false;
                SettleHome(x, floorTop);
                // a proud squash and a happy squint when it stops (owner 13 Oct 2026)
                m.Land(0.75);
                m.PetUntil = DateTime.Now.AddSeconds(1.3);
                Later.After(1.35, m.Publish);
                m.Chain(1.4, 2.4);                                         // after the squint
                return;
            }
            var leg = legs[i]; double dir = leg.B >= leg.A ? 1 : -1;
            double lift = 0;
            m.RunFacing = dir > 0 ? 1 : -1;
            switch (stage)
            {
                case 0:                                                    // scramble: legs blur, body leans in, no ground covered
                    phase += dt * 30; m.RunLean = 7 * dir;
                    if (se > 0.3) { stage = 1; stageT = now; }
                    break;
                case 1:                                                    // gallop
                {
                    v = Math.Min(topSpeed, v + accel * dt);
                    x += dir * v * dt;
                    phase += v * dt / z / 6;
                    m.RunLean = 0;
                    double span = Math.Abs(leg.B - leg.A), p = span > 0 ? (x - leg.A) / (leg.B - leg.A) : 1;
                    if (leg.Hop && p > 0.35 && p < 0.65) lift = Math.Sin((p - 0.35) / 0.3 * Math.PI) * 46 * z / 2;
                    if ((leg.B - x) * dir <= v * v / (2 * brake) + 2) { stage = 2; stageT = now; }
                    break;
                }
                case 2:                                                    // skid: lean back, legs planted, slide to a stop
                    v = Math.Max(0, v - brake * dt);
                    x += dir * v * dt;
                    m.RunLean = -13 * dir;
                    if (v == 0) { stage = 3; stageT = now; pause = Rnd.In(0.35, 0.7); m.RunLean = 0; m.Land(0.88); }
                    break;
                default:                                                   // stand, glance back over the shoulder, go again
                    if (se > pause * 0.3 && se < pause * 0.7) m.RunFacing = dir > 0 ? -1 : 1;
                    if (se > pause) { i++; stage = Rnd.Bool() ? 0 : 1; stageT = now; v = 0; }
                    break;
            }
            x = Math.Min(Math.Max(x, lo), hi);
            m.RunPhase = stage == 3 ? 0 : phase;      // a skid (RunGait 2) draws its legs braced; the phase drives its dust
            if (stage == 2) phase += dt * 20;
            m.RunSpeed = v / topSpeed;
            m.RunGait = stage == 0 ? 1 : (stage == 2 ? 2 : 0);
            roam = (x, floorTop - lift);
            panel?.Refit();
        });
    }

    /// A click on the running cat catches it: it stops right there with a squish.
    public static void CatchCat() { if (Zooming) caught = true; }

    // ── cat-life panel movers (owner, 26 Sep 2026) ──

    /// Where the character box (46×62 × zoom on screen) sits while it roams away from home (a jump in flight, settled
    /// next to the pointer): top-left, screen points, y from the screen top. null = home. The panel around it changes size
    /// with the pose (CatLayout) — moving the CHARACTER keeps its feet anchored.
    public static (double Left, double Top)? roam;
    /// One mover at a time: a 30 Hz timer gliding the character box. The zoomies timer owns its own path; life actions
    /// never run mid-zoomies (the scheduler guards) and a click stops them via StopLife().
    static Later? lifeTimer, jumpWork;
    public static void StopLife()
    {
        jumpWork?.Cancel(); jumpWork = null;
        lifeTimer?.Cancel(); lifeTimer = null;
        roam = null;
        if (!Zooming && model != null) { model.RunFacing = 0; model.RunPhase = 0; model.RunStride = 38; }   // a walk cut short stands up
        fallTimer?.Cancel(); fallTimer = null; if (model != null) model.Airborne = false;                  // caught mid-air
        climbDir = 0; climbed = false; if (model != null) model.ClimbRot = 0;                              // caught mid-climb
        if (Zooming) caught = true;
        model?.StopAction();
    }
    public static void Refit() => panel?.Refit();
    /// Keep the main (pettable) cat above the herd's overlay window.
    public static void Raise() { if (panel != null) panel.RaiseToTop(); }
    /// Where the cat is drawn on screen right now — the drag grabs from here, not from the stored home, so a cat that
    /// jumped or walked over moves from where you see it.
    public static Rect? CharScreenRect() => panel?.CharRect();
    public static Point? CatCenter() => CharScreenRect() is Rect r ? new Point(r.Left + r.Width / 2, r.Top + r.Height / 2) : null;

    public static void Glide(double toLeft, double toTop, double dur, double hop = 0, System.Action? done = null)
    {
        if (panel == null || CharScreenRect() is not Rect r) return;
        lifeTimer?.Cancel();
        double fromLeft = r.Left, fromTop = r.Top;
        var t0 = DateTime.Now;
        // A move on the ground is a WALK (owner 27 Sep 2026: "it's not live like" — the cat slid like a sticker): side-on,
        // legs stepping with the distance covered, ~100 pt/s (about a body length a second). A hop (the leap) keeps its own pose.
        double dist = Math.Sqrt((toLeft - fromLeft) * (toLeft - fromLeft) + (toTop - fromTop) * (toTop - fromTop));
        bool walks = hop == 0 && dist > 12 && !Fx.Reduced;
        if (walks) dur = Math.Min(Math.Max(dist / 100, 0.6), 5);
        int tick = 0;
        lifeTimer = Later.Every(1.0 / 30, timer =>
        {
            tick++;
            double e = (DateTime.Now - t0).TotalSeconds;
            double u = Math.Min(e / dur, 1);
            // a walk keeps an even pace (gentle start/stop); a leap eases in and out
            // (the Swift ramps down with `1 − …/0.24` — a 12 % jump at u = 0.88 and a 13 % overshoot at the end;
            //  the continuous trapezoid is `0.88 − …`, so the walk ends exactly on its target)
            double k = walks ? (u < 0.12 ? u * u / 0.24 : u > 0.88 ? 0.88 - (1 - u) * (1 - u) / 0.24 : u - 0.06) / 0.88
                             : (u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2);
            double lift = hop * Math.Sin(u * Math.PI);
            roam = (fromLeft + (toLeft - fromLeft) * k, fromTop + (toTop - fromTop) * k - lift);
            if (walks && model is CatModel m)
            {
                // set only on change: every Set redraws her (CPU, 13 Oct 2026)
                int face = Math.Abs(toLeft - fromLeft) > 4 && toLeft < fromLeft ? -1 : 1;
                m.RunFacing = face;
                m.RunStride = 22;
                // legs drawn "on twos" (15 drawings a second, as cartoons do) while the window glides at 30 Hz
                if (tick % 2 == 1) m.RunPhase = dist * k / CatLayout.Zoom / 3;      // one stride cycle ≈ 19 pt of drawing
            }
            panel?.Refit();
            if (u >= 1)
            {
                timer.Cancel();
                lifeTimer = null;
                if (walks && model is CatModel mm) { mm.RunFacing = 0; mm.RunPhase = 0; mm.RunStride = 38; }
                if (hop > 0) model?.Land();
                done?.Invoke();
            }
        });
    }

    /// Jump: crouch 0.35 s, then a parabolic arc to the opposite side of its screen; where it lands is its new home
    /// (persisted like a drag). On her own: the next jump goes HERE (a window top) instead of across the screen — CatOwn
    /// sets it, LifeJump uses it once.
    public static (double Left, double Top)? leapTarget;
    public static void LifeJump()
    {
        var target = leapTarget; leapTarget = null;
        if (Zooming || panel == null || CharScreenRect() is not Rect r) return;
        var vf = Display.Visible;
        double fromLeft = r.Left, fromTop = r.Top;
        double leftX = Display.Frame.Left + 8;
        double rightX = Math.Max(leftX, vf.Right - CatLayout.ScreenW - 8);
        double destL = target?.Left ?? (Math.Abs(fromLeft - leftX) < Math.Abs(fromLeft - rightX) ? rightX : leftX);
        double top = target?.Top ?? fromTop;
        // window to window: the arc clears the higher ledge by 40 pt, so a leap up never cuts through its edge
        double hop = target == null ? 60 * CatLayout.Zoom : Math.Max(40, fromTop - top + 40);
        if (model != null) model.JumpFacing = destL < fromLeft ? -1 : 1;     // the crouch and the leap face the way it jumps
        jumpWork = Later.After(Emote.JumpTakeoff, () =>      // the crouch, then the leap (Emote's timing)
        {
            if (model != null) model.JumpCrouch = false;     // take-off: crouch → leap
            Glide(destL, top, Emote.JumpFlight, hop, () =>
            {
                if (target != null)
                {
                    SettleHome(destL, top);
                    climbed = true;                          // up high by herself — the pointer can knock her off
                    return;
                }
                if (destL + CatLayout.ScreenW > Display.Frame.Right - 28) Prefs.Remove("crewLeft");
                else Prefs.Set("crewLeft", destL - Display.Frame.Left);
                Prefs.Set("crewTop", top);
                roam = null;
                panel?.Refit();
                CheckPerch();                                // leapt past the end of its window → it falls
            });
        });
    }

    /// Clingy: walk to the still pointer and settle next to it (no jump — the rub is in the drawing: swaying body,
    /// half-closed eyes).
    public static void LifeCling(Point pointer)
    {
        if (Zooming) return;
        var vf = Display.Visible;
        double z = CatLayout.Zoom;
        double left = Math.Min(Math.Max(pointer.X - CatLayout.ScreenW - 6 * z, vf.Left), vf.Right - CatLayout.ScreenW);
        double top = Math.Min(Math.Max(pointer.Y - CatLayout.ScreenH / 2, vf.Top), vf.Bottom - CatLayout.ScreenH);
        // Standing on a floor it WALKS along it toward the pointer (no walking on air); it falls if it walks off.
        if (!Hanging && CharTopLeft() is Point at) top = at.Y;
        Glide(left, top, 0.9);
    }
    /// Something moving her right now (a walk, a leap, a climb, a fall) — the on-her-own scheduler waits for it.
    public static bool Busy => lifeTimer != null || fallTimer != null || climbDir != 0;

    /// Climbing (owner 13 Oct 2026: "climbing on screen, when i point cursor on it it falls from there"): she walks to the
    /// side of a nearby window and climbs its edge — the side-on runner turned upright against the wall, paws stepping in
    /// small pulls — then hops onto its top, her new spot. Standing on the taskbar line with no window to climb, she climbs
    /// the nearer screen edge back up to her pin (zoomies take her off it). null: nothing to climb.
    public const double HangTop = 64;                  // CatMove.Top's default: the pin near the top of the screen
    static double climbDir;                            // mid-climb: 1 = the wall is on her right · -1 on her left
    static bool climbed;                               // up high by herself — the pointer can knock her off (Slip)
    /// `edge`: only the screen edge — up to the window top that meets it, else to the pin (from the taskbar line) — else only
    /// a window side. Returns where she ends up (box top-left) or null: nothing to climb. `go: false` only asks (CatOwn's picker).
    public static Point? Climb(Point at, CatGravity.Floor fl, List<CatGravity.Win> wins, bool edge = false, bool go = true)
    {
        double w = CatLayout.ScreenW, z = CatLayout.Zoom, feet = CatGravity.Feet, feetY = at.Y + feet;
        double gap = 12 * z;                           // box centre → wall: the upright body's back against the edge
        (double Base, double Land, double Top, double Bottom, double Dir)? best = null;
        var F = Display.Frame;
        foreach (var win in wins)
        {
            var r = win.Rect;
            // taller than her, and its side reaches down to her floor — or stops at most 150 pt above it (she hops onto it)
            if (CatGravity.Ledge(r) is not double top || !(top < feetY - 60 && r.Bottom >= feetY - 150 && r.Bottom - top > 80)) continue;
            foreach (var (edgeX, dir) in new[] { (r.Left, 1.0), (r.Right, -1.0) })
            {
                double bas = edgeX - dir * gap - w / 2, land = dir > 0 ? r.Left + 6 : r.Right - w - 6;
                // any side on her floor — a walk of up to 5 s (Glide) — not only one within 420 pt: a big window's sides
                // are far apart and she never reached them (owner 13 Oct 2026)
                if (!(bas + w / 2 > fl.X0 && bas + w / 2 < fl.X1 && bas >= F.Left && bas + w <= F.Right
                      && CatGravity.FloorBelow(land + w / 2, top - 0.5, wins).Window == win.Id)) continue;
                if (best == null || Math.Abs(bas - at.X) < Math.Abs(best.Value.Base - at.X)) best = (bas, land, top, r.Bottom, dir);
            }
        }
        if (!edge && best is { } b)
        {
            if (!go) return new Point(b.Land, b.Top - feet);
            System.Action up = () =>
                ClimbWall(b.Base, b.Top - feet + 2 * z, b.Dir, () =>           // head over the top edge…
                    Glide(b.Land, b.Top - feet, 0.4, 10 * z, () =>            // …and a hop onto it
                    {
                        SettleHome(b.Land, b.Top - feet);
                        climbed = true;
                        model?.Chain();
                    }));
            Glide(b.Base, at.Y, 1, 0, () =>
            {
                // a side that stops above her floor: a hop up onto its foot first
                if (b.Bottom < feetY - 4) Glide(b.Base, b.Bottom - feet, 0.5, feetY - b.Bottom + 30, up); else up();
            });
            return new Point(b.Land, b.Top - feet);
        }
        if (!edge || fl.Window != null) return null;   // up the screen edge — only from the taskbar line
        bool right = at.X + w / 2 > F.Left + F.Width / 2;
        double dirE = right ? 1 : -1;
        double baseE = right ? F.Right - w / 2 - gap : F.Left - w / 2 + gap;
        // A window flush with that edge (maximised, or snapped to the side): no side of it is on screen to climb, so she
        // climbs the screen edge up to its top and steps onto it. None: up to her pin near the top.
        double landE = right ? F.Right - w - 6 : F.Left + 6;
        var onto = CatGravity.FloorBelow(landE + w / 2, 0, wins);
        if (onto.Window != null && onto.Y < feetY - 120)
        {
            if (!go) return new Point(landE, onto.Y - feet);
            Glide(baseE, at.Y, 1, 0, () =>
                ClimbWall(baseE, onto.Y - feet + 2 * z, dirE, () =>
                    Glide(landE, onto.Y - feet, 0.4, 10 * z, () =>
                    {
                        // the window closed or moved while she climbed: the perch check drops her
                        SettleHome(landE, onto.Y - feet);
                        climbed = true;
                        model?.Chain();
                    })));
            return new Point(landE, onto.Y - feet);
        }
        if (!go) return new Point(baseE, HangTop);
        Glide(baseE, at.Y, 1, 0, () =>
            ClimbWall(baseE, HangTop, dirE, () =>
            {
                SettleHome(baseE, HangTop);
                if (right) Prefs.Remove("crewLeft");   // docked, as at first launch
                SetHanging(true);
                climbed = true;
                panel?.Refit();
                model?.Land(0.85);
                model?.Chain();
            }));
        return new Point(baseE, HangTop);
    }

    /// The climb itself: up (or down — on her own, head first) the wall at `left` to box top `top1`, one 30 Hz timer only
    /// while she climbs. The pointer on her makes her let go (Slip).
    public static void ClimbWall(double left, double top1, double dir, System.Action done)
    {
        if (panel == null || model is not CatModel m || CharTopLeft() is not Point at) return;
        lifeTimer?.Cancel();
        climbDir = dir;
        double down = top1 > at.Y ? 1 : -1;
        m.RunFacing = dir * -down > 0 ? 1 : -1; m.RunStride = 22; m.RunSpeed = 0; m.RunGait = 0; m.RunLean = 0;
        m.ClimbRot = -90 * dir;                         // head up (or, facing back, head down) — belly to the wall either way
        double phase = m.RunPhase;
        double z = CatLayout.Zoom;
        double y = at.Y, t = 0; var last = DateTime.Now; int tick = 0;
        // Going up (desktop-pet prior art): a 0.4 s scratch at the foot of the wall first, so the climb reads as meant —
        // and one climb in six fails halfway: she slides back a little, lets go, lands, and shakes it off with zoomies.
        double claw = down < 0 ? 0.4 : 0.0;
        double? failAt = down < 0 && Rnd.R.Next(6) == 0 ? (at.Y + top1) / 2 : null;
        double? slideT = null;
        lifeTimer = Later.Every(1.0 / 30, timer =>
        {
            var now = DateTime.Now; double dt = Math.Min((now - last).TotalSeconds, 1.0 / 15); last = now; t += dt;
            if (slideT is double s0)                                                 // losing her grip
            {
                y += 110 * dt; roam = (left, y); panel?.Refit();
                if (t - s0 > 0.35)
                {
                    timer.Cancel(); lifeTimer = null; climbDir = 0;
                    m.RunFacing = 0; m.RunPhase = 0; m.RunStride = 38; m.ClimbRot = 0;
                    Drop(-110 * dir, 30, null, () => { CatOwn.LastZoomies = DateTime.Now; Zoomies(); });
                }
                return;
            }
            if (t < claw) { phase += dt * 16; m.RunPhase = phase; return; }                       // paws up on the wall, a quick scratch
            double v = 160 * (0.2 + 0.8 * Math.Max(0, Math.Sin((t - claw) * 2 * Math.PI * 1.5)));  // small pulls, ~70 pt/s
            y = down > 0 ? Math.Min(top1, y + v * dt) : Math.Max(top1, y - v * dt);
            phase += v * dt / z / 3;
            tick++;
            if (tick % 2 == 1) m.RunPhase = phase;                                  // paws on twos, like the walk (CPU)
            if (failAt is double f && y <= f) slideT = t;
            roam = (left, y);
            panel?.Refit();
            if (panel!.CharRect() is Rect cr && Rect.Inflate(cr, -6 * z, -6 * z).Contains(Display.Pointer())) { Slip(); return; }
            if (y == top1)
            {
                timer.Cancel(); lifeTimer = null; climbDir = 0;
                m.RunFacing = 0; m.RunPhase = 0; m.RunStride = 38; m.ClimbRot = 0;
                done();
            }
        });
    }

    /// The pointer on a cat that climbed up by herself (on her own only): she loses her grip and falls — off the wall, off
    /// the pin, or straight off the front of the window she sits on. A cat you put somewhere stays put.
    public static void Slip()
    {
        if (!CatPrefs.OnHerOwn || !(climbDir != 0 || climbed) || Zooming || CatMove.Holding || model is not CatModel m || m.Airborne
            || CatPrefs.Quiet || Fx.Reduced || CharTopLeft() is not Point at) return;
        if (climbDir != 0)                                                   // mid-wall: pushed off it
        {
            double dir = climbDir;
            lifeTimer?.Cancel(); lifeTimer = null; climbDir = 0;
            m.RunFacing = 0; m.RunPhase = 0; m.RunStride = 38; m.ClimbRot = 0;
            Drop(-150 * dir, -80);
            return;
        }
        if (lifeTimer != null || m.Act != CatModel.Action.None) return;
        if (Hanging) { SetHanging(false); Drop(0, -60); return; }
        var fl = CatGravity.FloorBelow(at.X + CatLayout.ScreenW / 2, at.Y + CatGravity.Feet - 3, CatGravity.Windows());
        Drop(Rnd.In(-70, 70), -220, fl.Window);                              // a startled hop, then down
    }

    /// Two quick hops on the spot, straight back to where it sat — no trip across the screen. A cat with no headroom
    /// (hanging right under the top of the screen) squishes twice instead. Never over a full-screen app, never in Sleep
    /// mode / Reduce Motion. (On the Mac the new-notification whisper calls this; kept for completeness.)
    public static void Bump()
    {
        if (Zooming || lifeTimer != null || CatMove.Holding || model is not CatModel m || m.Airborne || CatPrefs.Quiet || Fx.Reduced
            || FullScreenInFront() || CharTopLeft() is not Point at) return;
        var back = roam;                                                 // null at home; a walked-over cat stays where it is
        double hop = Math.Min(12 * CatLayout.Zoom, at.Y - Display.Visible.Top);   // air up to the top of the screen
        if (hop < 6)
        {
            m.Land(0.7);
            Later.After(0.3, () => m.Land(0.7));
            return;
        }
        Glide(at.X, at.Y, 0.3, hop, () =>
            Glide(at.X, at.Y, 0.24, hop * 0.55, () =>
            {
                roam = back;
                panel?.Refit();
            }));
    }

    /// A window of another app covering the whole screen (a full-screen app, a slideshow).
    static (DateTime At, bool Yes) fullScreenSeen = (DateTime.MinValue, false);
    public static bool FullScreenInFront()
    {
        if ((DateTime.Now - fullScreenSeen.At).TotalSeconds < 2) return fullScreenSeen.Yes;   // the window query costs; ask at most every 2 s
        bool yes = FullScreenNow();
        fullScreenSeen = (DateTime.Now, yes);
        return yes;
    }
    static bool FullScreenNow()
    {
        var h = Win32.GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(h, out uint pid);
        if (pid == Environment.ProcessId || Win32.ClassName(h) is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        // the extended frame, not GetWindowRect: a maximised window's rect spills ~8 px past the screen and would count
        if (!Win32.FrameBounds(h, out var rc)) return false;
        var r = Display.ToDip(rc);
        return r.Width >= Display.Frame.Width - 0.5 && r.Height >= Display.Frame.Height - 0.5;
    }

    public static void LifeGlideHome()
    {
        if (Zooming || panel == null) return;
        var h = panel.HomeAnchor();
        Glide(h.Left, h.Top, 0.9, 0, () => { roam = null; panel?.Refit(); });
    }
    // Redraw, not just resize: the views read prefs while drawing, so tell them something changed.
    public static void ApplyPrefs() { model?.Publish(); panel?.Refit(); }
}

/// The window itself. Style bits: no activation, no Alt-Tab entry, no taskbar button. Mouse: the left button pets
/// (click) or drags; the right button opens the menu (CatView).
sealed class CatWindow : Window
{
    public readonly CatView View;
    readonly CatModel model;
    IntPtr hwnd;
    Rect frame = new(0, 0, CatLayout.ScreenW, CatLayout.ScreenH);
    CatLayout.Spec spec = CatLayout.Closed;

    public CatWindow(CatModel m)
    {
        model = m;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Left = frame.Left; Top = frame.Top; Width = frame.Width; Height = frame.Height;
        Content = View = new CatView(m);
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
            Refit();
        };
    }

    /// Screen rect (DIPs, y down) of the character box inside the current panel (`spec` is 1×, the panel is × zoom).
    public Rect CharRect() =>
        new(frame.Left + spec.BoxX * CatLayout.Zoom, frame.Top + spec.BoxY * CatLayout.Zoom, CatLayout.ScreenW, CatLayout.ScreenH);

    /// Home of the character box (top-left): docked at the right edge or where the owner parked it, kept above the taskbar.
    public (double Left, double Top) HomeAnchor()
    {
        // Docked = fully visible, 8 pt in from the edge (owner 27 Sep 2026: the 16 pt tuck hid the cat in the corner).
        const double hang = -8;
        double left = CatMove.Docked ? Display.Frame.Right - CatLayout.ScreenW + hang : CatMove.Left();
        double top = CatMove.Top();
        double ground = Display.Visible.Bottom;                                   // the taskbar line
        if (top + CatGravity.Feet > ground) top = Math.Max(CatMove.MinTop, ground - CatGravity.Feet);   // feet on it
        return (left, top);
    }

    /// A pose grows the panel to the drawn bounds + 4 pt (CatLayout), anchored so the character's FEET stay put, clamped
    /// into the visible frame; it shrinks back when the pose ends.
    public void Refit()
    {
        var anchor = CatPanel.roam ?? HomeAnchor();
        var vf = Display.Visible;
        // cataholic:// links: the whisper's side (the bubble needs ~280 pt (1×) at the cat's left), decided before the spec reads it
        double roomR = vf.Right - (anchor.Left + CatLayout.ScreenW), roomL = anchor.Left - vf.Left;
        CatLayout.WhisperOnRight = roomL < 280 * CatLayout.Zoom && roomR > roomL;
        var s = CatLayout.SpecFor(model.Act, model.Whisper != null);
        bool tucked = CatPanel.roam == null && CatMove.Docked && !model.IsOut;   // hangs off the right edge on purpose
        // The empty box below the feet may hang off the bottom of the screen (a cat standing on the floor with the taskbar
        // hidden): clamping it back lifted the cat for as long as a pose was showing.
        double belowFeet = CatLayout.ScreenH - CatGravity.Feet;
        // A resting cat draws nothing above its box, so it may sit at the very top; any pose that draws above keeps the clamp.
        double ceiling = model.Act == CatModel.Action.None ? 0 : vf.Top;
        var within = new Rect(vf.Left, ceiling, vf.Width, Display.Frame.Height - ceiling + belowFeet);
        var r = CatLayout.PanelRect(anchor.Left, anchor.Top, s, within, tucked);
        spec = s;
        if (r == frame) return;
        bool resized = r.Size != frame.Size;
        frame = r;
        if (hwnd != IntPtr.Zero) Display.Place(hwnd, r);
        if (resized) View.Redraw();
    }

    /// Where the character box sits inside the panel (DIPs).
    public Vector BoxOffset => new(spec.BoxX * CatLayout.Zoom, spec.BoxY * CatLayout.Zoom);

    public void RaiseToTop() { if (hwnd != IntPtr.Zero) Display.Raise(hwnd); }
}

/// Move the cat anywhere (owner 25 Sep 2026: "can drag anywhere to screen?"). Dropped within 28 pt of the right edge it
/// DOCKS (sits at the edge, as before); anywhere else it floats. Remembered as crewLeft / crewTop (points from the
/// screen's left / top); screen-space deltas because the window moves under the pointer. Screen points throughout.
static class CatMove
{
    public const double MinTop = 30;
    /// How far above the bottom of the visible frame the cat's TOP may sit: feet on the taskbar line (gravity, 27 Sep 2026).
    static readonly double BottomRoom = 49 * CatLayout.Zoom;
    static Point? grab;
    public static bool Holding => grab != null;
    /// The last pointer samples of a drag (time, screen point) — the fling velocity on release.
    static readonly List<(DateTime T, Point P)> trail = new();
    public static bool Docked => !Prefs.Has("crewLeft");
    public static double Top()
    {
        double v = Prefs.Double("crewTop");
        double maxTop = Display.Visible.Bottom - BottomRoom;
        return v > 0 ? Math.Min(Math.Max(v, MinTop), maxTop) : 64;
    }
    /// The character's left edge in screen coordinates (docked = flush with the right edge).
    public static double Left()
    {
        if (Docked) return Display.Frame.Right - CatLayout.ScreenW;
        double v = Prefs.Double("crewLeft");
        return Math.Min(Math.Max(Display.Frame.Left + v, Display.Frame.Left), Display.Frame.Right - CatLayout.ScreenW);
    }
    public static void Drag()
    {
        var m = Display.Pointer();
        if (grab == null)
        {
            // Grabbing the cat (owner 27 Sep 2026: "unable to move the cat clearly"): stop whatever it is doing and drop a
            // roam position (after a jump or a clingy walk the panel stayed pinned there while the drag moved only the
            // stored home), then take the pointer's offset from where the cat is DRAWN, not from the stored home.
            CatPanel.StopLife();
            CatSound.PurrStart();          // it purrs while you hold it
            var r = CatPanel.CharScreenRect();
            double drawnLeft = r?.Left ?? Left(), drawnTop = r?.Top ?? Top();
            grab = new Point(m.X - drawnLeft, m.Y - drawnTop);
        }
        trail.Add((DateTime.Now, m)); if (trail.Count > 6) trail.RemoveAt(0);
        var g = grab ?? default;
        double newLeft = m.X - g.X, newTop = m.Y - g.Y;
        if (newLeft + CatLayout.ScreenW > Display.Frame.Right - 28) Prefs.Remove("crewLeft");   // dock
        else Prefs.Set("crewLeft", newLeft - Display.Frame.Left);
        // stored clamped, as Top() reads it, so dragging back from past an edge has no dead zone
        double maxTop = Display.Visible.Bottom - BottomRoom;
        Prefs.Set("crewTop", Math.Min(Math.Max(newTop, MinTop), maxTop));
        CatPanel.ApplyPrefs();
    }
    /// Let go: slow and near the top → it hangs on its pin again; anywhere else it falls (flung fast → it flies with the
    /// pointer's speed) and lands on the floor below (CatPanel.Drop).
    public static void End()
    {
        grab = null; CatSound.PurrStop();
        double vx = 0, vy = 0;
        if (trail.Count >= 2)
        {
            var a = trail[0]; var b = trail[^1];
            if ((b.T - a.T).TotalSeconds > 0.01 && (DateTime.Now - b.T).TotalSeconds < 0.1)
            {
                double dt = (b.T - a.T).TotalSeconds;
                vx = (b.P.X - a.P.X) / dt; vy = (b.P.Y - a.P.Y) / dt;        // y down
                double sp = Math.Sqrt(vx * vx + vy * vy); if (sp > 3200) { vx *= 3200 / sp; vy *= 3200 / sp; }
            }
        }
        trail.Clear();
        CatPanel.CurrentModel?.Touched();                                  // on her own: she stays put a few seconds after you let go
        // A window top right under its feet wins over the pin (owner 13 Oct 2026: put on a window near the top, it "gets
        // attached to the top bar rather than sitting on" the window) — it hangs only with no window to stand on.
        if (CatPanel.CharScreenRect() is Rect r && r.Top < CatGravity.HangZone && Math.Sqrt(vx * vx + vy * vy) < 400 && !WindowUnderFeet(r))
        {
            CatPanel.SetHanging(true); CatPanel.ApplyPrefs(); CatPanel.CurrentModel?.Land(0.85);
            return;
        }
        CatPanel.SetHanging(false);
        CatPanel.Drop(vx, vy);
    }
    /// Is there a window top within reach (≤ 60 pt) below the cat's feet? `r` is the character's screen rect.
    static bool WindowUnderFeet(Rect r)
    {
        double feetY = r.Top + CatGravity.Feet;
        var fl = CatGravity.FloorBelow(r.Left + r.Width / 2, feetY - 8, CatGravity.Windows());
        return fl.Window != null && fl.Y - feetY <= 60;
    }
}
