using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Cataholic;

// CatView — the life layer and the drawing of the cat inside her window (CrewView.swift): breathing, tail sway, ear
// twitch, pupils toward the pointer, hover, click = pet + purr, drag, and the emote performances (`Emote.Frame`).
// She is drawn only when something changes. At rest the cat is ONE still frame — no breathing, tail sway, blinks or
// eyes on the pointer, and no timer running (owner 28 Sep 2026: "if its idle its idle, no theatrics"); it redraws when
// the model publishes. While an emote, the crouch or a landing squish runs, a 30 Hz clock redraws her; walks, zoomies
// and falls redraw from the mover's own tick. Sleep mode: one slow breath every ~4 s, drawn at 12 fps.
sealed partial class CatView : FrameworkElement
{
    readonly CatModel model;
    readonly DispatcherTimer clock = new();
    readonly CatPose pose = new();
    static readonly DateTime Ref = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);   // NSDate's reference date

    public CatView(CatModel m)
    {
        model = m;
        clock.Tick += (_, _) => { if (Need() is double iv) { InvalidateVisual(); Retime(iv); } else { clock.Stop(); InvalidateVisual(); } };
    }

    /// Sleep mode, nothing else going on: she lies curled up (the nap's held frame).
    bool Asleep => CatPrefs.Quiet && model.Act == CatModel.Action.None && model.RunFacing == 0 && !model.Airborne;
    bool LifeOffSchedule => Fx.Reduced || CatPrefs.Quiet;
    bool LifeOff => Fx.Reduced || Fx.Snapshot || CatPrefs.Quiet;

    /// How often the clock must redraw her right now, or null when she is still.
    double? Need() => Asleep && !Fx.Reduced ? 1.0 / 12 : (model.ClockDriven ? 1.0 / 30 : null);
    void Retime(double iv) { var t = TimeSpan.FromSeconds(iv); if (clock.Interval != t) clock.Interval = t; }

    /// Redraw now, and start or stop the clock as the state needs.
    public void Redraw()
    {
        if (Need() is double iv) { Retime(iv); if (!clock.IsEnabled) clock.Start(); }
        else if (clock.IsEnabled) clock.Stop();
        InvalidateVisual();
    }

    // ── mouse: click = pet · drag ≥ 4 pt = carry her · right-click = the menu · double-click: Find my cat ──
    bool pressing, dragging;
    Point pressAt;
    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); model.Hovered = true; CatPanel.Slip(); }   // on her own: knocked off a perch
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); model.Hovered = false; }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (WhisperHit(e.GetPosition(this))) { model.DismissWhisper(); e.Handled = true; return; }     // cataholic:// links: a tap on the bubble hushes it
        pressing = true; dragging = false; pressAt = Display.Pointer();
        clicks = e.ClickCount;
        CaptureMouse();
        e.Handled = true;
    }
    int clicks;
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!pressing) return;
        var p = Display.Pointer();
        if (!dragging && (p - pressAt).Length < 4) return;                // moving the mouse ≥ 4 pt while pressed carries her
        dragging = true;
        CatMove.Drag();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!pressing) return;
        pressing = false; ReleaseMouseCapture();
        if (dragging) { dragging = false; CatMove.End(); return; }
        if (clicks >= 2) { model.StopAction(); CatPanel.StopLife(); CatSound.Meow(); FindMyCat.Open(); return; }   // Find my cat: a double-click opens the game (CrewPanel.openGame)
        // click = pet it (owner 27 Sep 2026) — or catch it mid-run. Any autonomous action stops instantly — the click wins.
        if (CatPanel.Zooming) { CatSound.Meow(); CatPanel.CatchCat(); return; }
        model.StopAction(); CatPanel.StopLife();
        model.Pet();
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        CatMenuPanel.Show(Display.Pointer());
        e.Handled = true;
    }

    // ── the life layer ──
    /// The resting drawing for the pose — what an emote settles into before it ends. No inbox: her pose is `sleeping`,
    /// drawn awake (`look`) while you are around, and asleep after 5 minutes away (the idle nap).
    CatState RestSkin() => model.Airborne ? CatState.Alert : MailStore.Count > 0 ? CatState.Needs     // cataholic:// links: the sign beats the idle rule
        : (Win32.IdleSeconds < 300 ? CatState.Look : CatState.Sleeping);
    CatState SkinState()
    {
        // An in-progress behaviour overrides the pose (running zoomies win over all).
        if (model.Airborne) return CatState.Alert;          // falling / flung: eyes wide, ears up
        switch (model.Act)
        {
            case CatModel.Action.Hi: return CatState.Hi;
            case CatModel.Action.Big: return CatState.Big;
            case CatModel.Action.Stretch: return CatState.Stretch;
            case CatModel.Action.Yawn: return CatState.Yawn;
            case CatModel.Action.Groom: return CatState.Groom;
            case CatModel.Action.Spin: return MailStore.Count > 0 ? CatState.Needs : CatState.Working;   // spin on the base pose
            case CatModel.Action.Look: return CatState.Look;
            case CatModel.Action.Jump: return CatState.Jump;
            case CatModel.Action.Clingy: return CatState.Clingy;
            case CatModel.Action.Nap: return CatState.Nap;
            default: if (Asleep) return CatState.Nap; break;
        }
        return RestSkin();
    }
    /// The nap's curled-up held frame, the back rising and falling slowly (owner 14 Oct 2026: "add the slow breath").
    EmoteFrame SleepFrame(DateTime now)
    {
        var f = Emote.Frame(CatModel.Action.Nap, Emote.NapHoldAt, CatPanel.Hanging, 1);
        if (!Fx.Reduced) f.Breath = 0.5 - 0.5 * Math.Cos((now.ToUniversalTime() - Ref).TotalSeconds * 2 * Math.PI / 4.2);
        return f;
    }
    // Random-but-stable life constants (per launch): tail phase, breath period.
    const double LifeTailPhase = 1.7, LifeBreathPeriod = 3.4;
    double LifeBreatheScale(double t) => model.Act == CatModel.Action.None ? 1 + 0.0075 * (0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / LifeBreathPeriod)) : 1;
    double LifeTail(double t) => LifeOff || model.Act != CatModel.Action.None ? 0 : Math.Sin(t * 2 * Math.PI / 7 + LifeTailPhase) * 2.2;
    /// Ear twitch every 8–25 s (random): a 0.35 s flick on a slot hash.
    double LifeEar(double t)
    {
        if (LifeOff || model.Act != CatModel.Action.None) return 0;
        long slot = (long)(t / 8);
        double jitter = (double)(slot * 2654435761L % 170) / 10;          // 0–17 s extra → 8–25 s
        double u = t - (slot * 8 + jitter);
        return u >= 0 && u < 0.35 ? Math.Sin(u / 0.35 * Math.PI) : 0;
    }
    // (the Mac's random blinks are skipped while her pose is `sleeping` — which on Windows is always — so there are none)
    /// Eyes/head look toward the pointer within ~300 pt (pupil ≤ 1.5 pt).
    Vector LifePupil()
    {
        if (LifeOff || !(model.Act == CatModel.Action.None || model.Act == CatModel.Action.Look)) return default;
        if (CatPanel.CatCenter() is not Point c) return default;
        var p = Display.Pointer();
        double dx = p.X - c.X, dy = p.Y - c.Y;
        if (Math.Sqrt(dx * dx + dy * dy) >= CatModel.Tune.LookDist) return default;
        return new Vector(Math.Max(-1.5, Math.Min(1.5, dx / 200 * 1.5)), Math.Max(-1.5, Math.Min(1.5, dy / 200 * 1.5)));
    }
    /// Clingy: lean the whole body ~8° toward the pointer side.
    double ClingLean()
    {
        if (model.Act != CatModel.Action.Clingy) return 0;
        if (CatPanel.CatCenter() is not Point c) return 8;
        return Display.Pointer().X >= c.X ? 8 : -8;
    }
    /// Clingy rub: a gentle ±3° sway while it leans on the pointer.
    double ClingSway(double t) => model.Act != CatModel.Action.Clingy || LifeOff ? 0 : 3 * Math.Sin(t * 2 * Math.PI / 1.2);
    // The sleeping "z" drifting up, and the breath, when she is asleep on the pin (pose sleeping)
    static double Breathe(double t) => 0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / 6 - Math.PI / 2);
    static (double, double) ZLift(double t)
    {
        double u = t % 4 / 4;
        double op = u < 0.3 ? u / 0.3 * 0.8 : Math.Max(0, 0.8 * (1 - (u - 0.3) / 0.7));
        return (u * 10, op);
    }

    static void Scale(DrawingContext dc, double sx, double sy, double cx, double cy) => dc.PushTransform(new ScaleTransform(sx, sy, cx, cy));
    static void Rot(DrawingContext dc, double deg, double cx, double cy) => dc.PushTransform(new RotateTransform(deg, cx, cy));

    protected override void OnRender(DrawingContext dc)
    {
        if (CatPanel.Window is not CatWindow win) return;
        var now = DateTime.Now;
        double t = Fx.Reduced ? 0 : (now.ToUniversalTime() - Ref).TotalSeconds;
        bool running = model.RunFacing != 0, asleep = Asleep;
        // Zoomies face the way they run; a leap (and its crouch) faces the way it jumps; a peek over an edge faces out.
        double facing = running ? (model.RunFacing < 0 ? -1 : 1) : (model.Act == CatModel.Action.Jump || model.Act == CatModel.Action.Look ? model.JumpFacing : 1);
        // An emote is a performance on the clock (Emote.Frame). Sleep mode / Reduce Motion: the identity frame — the
        // action's still pose. Sleep mode at rest: the nap's curled-up held frame (owner 14 Oct 2026: "it just sleeps curly").
        bool animating = model.Act != CatModel.Action.None && !LifeOffSchedule && !Fx.Snapshot;
        var f = animating ? Emote.Frame(model.Act, (now - model.LastActionStarted).TotalSeconds, CatPanel.Hanging && !model.Airborne, model.NapHold)
              : asleep ? SleepFrame(now) : new EmoteFrame();
        double spinScale = Math.Cos(f.Spin * Math.PI / 180);
        CatState skin = f.Skin switch
        {
            EmoteFrame.SkinKind.Own => SkinState(),
            EmoteFrame.SkinKind.Rest => RestSkin(),
            EmoteFrame.SkinKind.Alert => CatState.Alert,
            EmoteFrame.SkinKind.Crouch or EmoteFrame.SkinKind.Leap => CatState.Jump,
            _ => CatState.Yawn,
        };
        bool crouch = f.Skin == EmoteFrame.SkinKind.Crouch ? true : (f.Skin == EmoteFrame.SkinKind.Leap ? false : model.JumpCrouch);
        double tilt = running ? Math.Sin(model.RunPhase) * 4 + model.RunLean : ClingSway(t);
        var z = model.Act == CatModel.Action.Nap ? f.Z : ZLift(t);

        var p = pose;
        p.State = running ? CatState.Running : skin;
        p.Badge = MailStore.Count > 0 ? MailStore.Count.ToString() : ""; p.BlinkShut = false;   // cataholic:// links: the sign's count
        p.BreatheY = Breathe(t);
        (p.ZRise, p.ZOpacity) = z;
        p.RunPhase = model.RunPhase;
        p.Breathe = LifeBreatheScale(t);
        p.TailSway = animating ? f.Tail : LifeTail(t);
        p.EarTwitch = animating ? f.EarTwitch : LifeEar(t);
        p.Pupil = animating ? new Vector(f.Pupil, 0) : LifePupil();
        p.HeadTurn = f.HeadTurn; p.HeadTilt = f.HeadTilt;
        p.JumpCrouch = crouch;
        p.BodyTilt = ClingLean();
        p.Pin = CatPanel.Hanging && !model.Airborne && f.Skin != EmoteFrame.SkinKind.Alert;
        p.Happy = now < model.PetUntil;
        p.Stride = model.RunStride;
        p.PawRaise = f.Paw; p.PawWave = f.Wave; p.GroomPaw = f.GroomPaw; p.Tongue = f.Tongue;
        p.MouthOpen = f.Mouth; p.EarsBack = f.Ears; p.Eyes = f.Eyes; p.PawWrist = f.Wrist;
        p.NapBreath = f.Breath; p.NapPeek = f.Peek;
        p.RunSpeed = model.RunSpeed; p.RunGait = model.RunGait;

        // Where the 46×62 character box sits inside the panel; everything below is in 1× box points, drawn at × Zoom.
        var box = win.BoxOffset;
        double sq = model.SquashNow();
        const double fx = 23, fy = 49, cx = 23, cy = 31, px = 23, py = 2;       // the feet · the box centre · the pin
        dc.PushTransform(new TranslateTransform(box.X, box.Y - f.Lift * CatLayout.Zoom));
        dc.PushTransform(new ScaleTransform(CatLayout.Zoom, CatLayout.Zoom));
        // SwiftUI lists these inner → outer; a DrawingContext applies the LAST pushed first — so they go in reverse.
        int n = 2;
        if (running) { Scale(dc, 0.93, 0.93, cx, cy); n++; }          // the side-on runner touches both edges of its box; 7 % smaller it can lean without being cut off
        if (running && model.ClimbRot != 0) { Rot(dc, model.ClimbRot, cx, cy); n++; }   // climbing: upright against the wall
        Rot(dc, tilt, running ? cx : px, running ? cy : py); n++;
        Rot(dc, f.Rot * facing, fx, fy); n++;
        Scale(dc, 1 + (1 - sq) * 0.6, sq, fx, fy); n++;               // chunky squish on landing / pet: flatter and wider from the feet
        Scale(dc, spinScale, 1, cx, cy); n++;
        Scale(dc, facing * f.Sx, f.Sy, cx, running ? cy : fy); n++;   // squash & stretch (and grow big) from the feet
        CatDrawing.Draw(dc, p);
        for (; n > 0; n--) dc.Pop();

        if (model.HiBubble is string h) DrawBubble(dc, box, h, f);
        DrawWhisper(dc, box);                                     // cataholic:// links
    }

    /// Say-hi bubble: sits just above-left of the head (≤ 6 pt from the ear) with a small tail pointing at the cat.
    /// Fixed 34×26 so its bounds (CatLayout.Drawn(Hi)) do not depend on font metrics.
    static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(251, 248, 241)), Ink = new SolidColorBrush(Color.FromRgb(33, 31, 38));
    void DrawBubble(DrawingContext dc, Vector box, string text, EmoteFrame f)
    {
        dc.PushTransform(new TranslateTransform(box.X, box.Y));
        dc.PushTransform(new ScaleTransform(CatLayout.Zoom, CatLayout.Zoom));
        dc.PushOpacity(Math.Max(0, Math.Min(1, f.BubbleAlpha)));
        dc.PushTransform(new TranslateTransform(-26.5, -20.5));
        dc.PushTransform(new ScaleTransform(f.Bubble, f.Bubble, 39, 34.5));       // pops in from its tail
        dc.DrawRoundedRectangle(Paper, null, new Rect(0, 0, 34, 26), 13, 13);
        var tail = new StreamGeometry();
        using (var g = tail.Open())
        {
            g.BeginFigure(new Point(24, 25), true, true);
            g.LineTo(new Point(32.5, 25), false, false); g.LineTo(new Point(39, 34.5), false, false);
        }
        dc.DrawGeometry(Paper, null, tail);
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, Ink,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point((34 - ft.Width) / 2, (26 - ft.Height) / 2));
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }
}
