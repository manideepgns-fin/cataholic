using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Cataholic;

// CatHerd — the cat multiplier (owner 27 Sep 2026: "my screen covered with cats … the right button i can select how many
// cats"). The extra cats live in ONE full-screen overlay window that ignores the mouse, so a screen full of cats never
// blocks a click; only the main cat (CatPanel) is pettable. Each herd cat is a tiny state machine — rain in from above
// the screen, land, sit, wander along its floor, nap melted flat, dash (zoomies) — standing on the same floors as the
// main cat (CatGravity: the taskbar line and the tops of windows). Random coats.
sealed class CatHerd
{
    public static readonly int[] Sizes = { 0, 3, 10, 25, 50, 100 };
    public static int Count => Prefs.Int("crewHerd");
    public static void Set(int n) { Prefs.Set("crewHerd", (double)n); Shared.Restart(); }

    public enum Mode { Fall, Sit, Walk, Zoom, Nap }
    public sealed class Cat
    {
        public CatCoat Coat = CatCoat.Ginger;
        public double X, Feet;                      // box left, feet line (y down from the screen top)
        public double Vx, Vy;
        public Mode Mode = Mode.Fall;
        public double Until;                        // when the current mode ends (clock seconds)
        public double Target;
        public double Facing = 1;
        public double Phase;
        public double Squash = 1;
        public bool Dirty = true;                   // redraw me (a sitting cat needs no redraw)
    }

    public static readonly CatHerd Shared = new();
    public List<Cat> Cats { get; private set; } = new();
    HerdWindow? window;
    Later? timer;
    List<CatGravity.Win> wins = new();
    double winsAt, last;
    static readonly Stopwatch Clk = Stopwatch.StartNew();
    static double Clock => Clk.Elapsed.TotalSeconds;

    /// Rebuild the herd for the chosen count (menu, launch, screen change). Sleep mode and Reduce Motion: no herd.
    public void Restart()
    {
        timer?.Cancel(); timer = null;
        int n = CatPrefs.Quiet || Fx.Reduced || !CatPrefs.ShowCrew ? 0 : Count;
        if (n <= 0) { window?.Hide(); Cats = new(); return; }
        double w = CatLayout.ScreenW;
        Cats = new();
        for (int i = 0; i < n; i++)
            Cats.Add(new Cat
            {
                Coat = CatCoat.All[Rnd.R.Next(CatCoat.All.Count)],
                X = Rnd.In(Display.Frame.Left, Display.Frame.Right - w),
                Feet = -Rnd.In(40, 900),                                    // above the screen: they rain in, staggered
                Vx = Rnd.In(-120, 120),
            });
        window ??= new HerdWindow();
        window.Cover(Display.Frame);
        window.Show();
        window.Rebuild(Cats);
        CatPanel.Raise();                                                   // the main cat stays on top of its herd
        last = Clock;
        wins = CatGravity.Windows(); winsAt = last;                         // floors known before the first cat lands
        timer = Later.Every(1.0 / 30, _ => Step());
    }

    void Step()
    {
        double now = Clock, dt = Math.Min(now - last, 1.0 / 15); last = now;
        if (now - winsAt > 1) { wins = CatGravity.Windows(); winsAt = now; }          // floors, once a second
        double w = CatLayout.ScreenW, z = CatLayout.Zoom;
        double minX = Display.Frame.Left, maxX = Display.Frame.Right - w;
        CatGravity.Floor FloorOf(Cat c, double from) => CatGravity.FloorBelow(c.X + w / 2, from, wins);
        foreach (var c in Cats)
        {
            var before = c.Mode;
            c.Squash += (1 - c.Squash) * Math.Min(1, dt * 9);
            if (Math.Abs(1 - c.Squash) < 0.002) c.Squash = 1;
            switch (c.Mode)
            {
                case Mode.Fall:
                {
                    double prev = c.Feet;
                    c.Vy += CatGravity.G * dt;
                    c.X += c.Vx * dt; c.Feet += c.Vy * dt;
                    if (c.X < minX) { c.X = minX; c.Vx = -c.Vx * 0.5; } else if (c.X > maxX) { c.X = maxX; c.Vx = -c.Vx * 0.5; }
                    if (c.Vy > 0 && c.Feet >= 0)
                    {
                        var f = FloorOf(c, Math.Max(prev, 0));
                        if (c.Feet >= f.Y) { c.Feet = f.Y; c.Vy = 0; c.Vx = 0; c.Squash = 0.72; Settle(c, now); }
                    }
                    break;
                }
                case Mode.Walk: case Mode.Zoom:
                {
                    double speed = c.Mode == Mode.Zoom ? 430 : 70;
                    double d = c.Target - c.X;
                    double stepX = Math.Min(Math.Abs(d), speed * dt) * (d < 0 ? -1 : 1);
                    c.X += stepX;
                    c.Facing = d < 0 ? -1 : 1;
                    c.Phase += Math.Abs(stepX) / z / (c.Mode == Mode.Zoom ? 6 : 3);
                    if (Math.Abs(d) < 1) { c.Phase = 0; if (c.Mode == Mode.Zoom) c.Squash = 0.86; Settle(c, now); }
                    if (!OnFloor(c)) { c.Mode = Mode.Fall; c.Phase = 0; }            // walked off the end of a window
                    break;
                }
                default:
                    if (now > c.Until) Next(c, now);
                    break;
            }
            if (c.Mode != Mode.Sit && c.Mode != Mode.Nap || c.Mode != before || c.Squash != 1) c.Dirty = true;
        }
        // a window moved or closed under a sitting cat: it falls (checked in a rolling slice — cheap at 100 cats)
        if (Cats.Count > 0)
        {
            int k = (int)(now * 30) % Cats.Count;
            if ((Cats[k].Mode == Mode.Sit || Cats[k].Mode == Mode.Nap) && !OnFloor(Cats[k])) { Cats[k].Mode = Mode.Fall; Cats[k].Dirty = true; }
        }
        window?.Sync(Cats);
    }
    bool OnFloor(Cat c) => CatGravity.Supported(c.X + CatLayout.ScreenW / 2, c.Feet, wins);
    static void Settle(Cat c, double now) { c.Mode = Mode.Sit; c.Until = now + Rnd.In(1.5, 5); }
    /// What a cat does next: mostly wander, often nap, sometimes zoom.
    void Next(Cat c, double now)
    {
        var f = CatGravity.FloorBelow(c.X + CatLayout.ScreenW / 2, c.Feet - 3, wins);
        double lo = Math.Max(f.X0, Display.Frame.Left), hi = Math.Max(lo, Math.Min(f.X1, Display.Frame.Right) - CatLayout.ScreenW);
        int r = Rnd.R.Next(100);
        if (r < 45) { c.Mode = Mode.Walk; c.Target = Rnd.In(lo, hi); }
        else if (r < 72) { c.Mode = Mode.Nap; c.Until = now + Rnd.In(6, 20); }
        else if (r < 84) { c.Mode = Mode.Zoom; c.Target = Math.Abs(c.X - lo) > Math.Abs(c.X - hi) ? lo : hi; }
        else { c.Mode = Mode.Sit; c.Until = now + Rnd.In(2, 6); if (Rnd.Bool()) { c.Facing *= -1; c.Dirty = true; } }
    }
}

/// The overlay: one transparent, click-through, full-screen window; each herd cat is its own DrawingVisual, redrawn only
/// when it changed (a sitting cat costs nothing).
sealed class HerdWindow : Window
{
    readonly HerdCanvas canvas = new();
    IntPtr hwnd;
    Rect cover = Display.Frame;

    public HerdWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Left = 0; Top = 0; Width = cover.Width; Height = cover.Height;
        Content = canvas;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            // never blocks a click: transparent to the mouse, never activates, no Alt-Tab entry
            Win32.AddExStyle(hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED);
            Display.Place(hwnd, cover);
        };
    }
    public void Cover(Rect r) { cover = r; if (hwnd != IntPtr.Zero) Display.Place(hwnd, r); }
    public void Rebuild(List<CatHerd.Cat> cats) => canvas.Rebuild(cats);
    public void Sync(List<CatHerd.Cat> cats) => canvas.Sync(cats);
}

sealed class HerdCanvas : FrameworkElement
{
    readonly List<DrawingVisual> visuals = new();
    readonly CatPose pose = new() { Pin = false };

    public void Rebuild(List<CatHerd.Cat> cats)
    {
        foreach (var v in visuals) RemoveVisualChild(v);
        visuals.Clear();
        foreach (var _ in cats) { var v = new DrawingVisual(); visuals.Add(v); AddVisualChild(v); }
        foreach (var c in cats) c.Dirty = true;
        Sync(cats);
    }
    protected override int VisualChildrenCount => visuals.Count;
    protected override Visual GetVisualChild(int index) => visuals[index];

    static CatState Skin(CatHerd.Cat c) => c.Mode switch
    {
        CatHerd.Mode.Fall => CatState.Alert,
        CatHerd.Mode.Walk or CatHerd.Mode.Zoom => CatState.Running,
        CatHerd.Mode.Nap => CatState.Sleeping,
        _ => CatState.Look,
    };

    public void Sync(List<CatHerd.Cat> cats)
    {
        for (int i = 0; i < cats.Count && i < visuals.Count; i++)
        {
            var c = cats[i];
            if (!c.Dirty) continue;
            c.Dirty = false;
            using var dc = visuals[i].RenderOpen();
            pose.State = Skin(c); pose.RunPhase = c.Phase; pose.Stride = c.Mode == CatHerd.Mode.Zoom ? 38 : 22; pose.CoatOverride = c.Coat;
            dc.PushTransform(new TranslateTransform(c.X - Display.Frame.Left, c.Feet - CatGravity.Feet));
            dc.PushTransform(new ScaleTransform(CatLayout.Zoom, CatLayout.Zoom));
            dc.PushTransform(new ScaleTransform(c.Facing * (1 + (1 - c.Squash) * 0.6), c.Squash, 23, 49));
            CatDrawing.Draw(dc, pose);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}
