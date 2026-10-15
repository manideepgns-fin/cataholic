using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Cataholic;

// CatHerd — the cat multiplier (owner 27 Sep 2026: "my screen covered with cats … the right button i can select how many
// cats"). Every extra cat is its OWN small window that ignores the mouse, so a screen full of cats never blocks a click;
// only the main cat (CatPanel) is pettable. (One full-screen transparent overlay was tried first: a layered window is
// rendered in software, so every frame re-rendered the whole screen — a full CPU core for 20 cats.) Moving a cat moves its
// window and redraws nothing; a cat is redrawn only when its pose changes, so a sitting or napping cat costs nothing.
// Each herd cat is a tiny state machine — rain in from above the screen, land, sit, wander along its floor, nap melted
// flat, dash (zoomies) — standing on the same floors as the main cat (CatGravity: the taskbar line and the tops of
// windows). Random coats.
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
        public HerdCatWindow? Win;                  // made when the cat first comes into view
    }

    public static readonly CatHerd Shared = new();
    public List<Cat> Cats { get; private set; } = new();
    /// For the trace: windows made, window moves, pose redraws since the herd was last rebuilt.
    public int Windows, Moves, Draws;
    DispatcherTimer? timer;
    List<CatGravity.Win> wins = new();
    double winsAt, last;
    static readonly Stopwatch Clk = Stopwatch.StartNew();
    static double Clock => Clk.Elapsed.TotalSeconds;
    const double Tick = 1.0 / 30;                    // while anything moves
    const double FloorsEvery = 1;                    // the windows are re-read this often, moving or not

    /// Rebuild the herd for the chosen count (menu, launch, screen change). Sleep mode and Reduce Motion: no herd.
    public void Restart()
    {
        timer?.Stop(); timer = null;
        foreach (var c in Cats) c.Win?.Close();
        Windows = Moves = Draws = 0;
        int n = CatPrefs.Quiet || Fx.Reduced || !CatPrefs.ShowCrew ? 0 : Count;
        if (n <= 0) { Cats = new(); return; }
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
        last = Clock;
        wins = CatGravity.Windows(); winsAt = last;                         // floors known before the first cat lands
        timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(Tick) };
        timer.Tick += (_, _) => Step();
        timer.Start();
    }

    void Step()
    {
        double now = Clock, dt = Math.Min(now - last, 1.0 / 15); last = now;
        bool floors = now - winsAt > FloorsEvery;
        if (floors) { wins = CatGravity.Windows(); winsAt = now; }                    // floors, once a second
        double w = CatLayout.ScreenW, z = CatLayout.Zoom;
        double minX = Display.Frame.Left, maxX = Display.Frame.Right - w;
        CatGravity.Floor FloorOf(Cat c, double from) => CatGravity.FloorBelow(c.X + w / 2, from, wins);
        bool busy = false, made = false;
        double wake = winsAt + FloorsEvery;                                           // when nothing moves: floors or the next mode timer
        foreach (var c in Cats)
        {
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
                    // a window moved or closed under a sitting cat: it falls (checked whenever the floors are re-read)
                    if (floors && !OnFloor(c)) c.Mode = Mode.Fall;
                    else if (now > c.Until) Next(c, now);
                    break;
            }
            // the window follows the cat; the picture is redrawn only if the pose changed
            if (c.Win == null && c.Feet - CatGravity.Feet - HerdCatWindow.Pad + HerdCatWindow.H > 0)
            {
                c.Win = new HerdCatWindow(c.Coat); Windows++; made = true;
            }
            if (c.Win != null)
            {
                if (c.Win.Draw(c, now)) Draws++;                                  // (before the first Show: it appears drawn)
                if (c.Win.MoveTo(c.X - HerdCatWindow.Pad, c.Feet - CatGravity.Feet - HerdCatWindow.Pad)) Moves++;
                busy |= c.Win.Stale(c);
            }
            busy |= c.Mode is Mode.Fall or Mode.Walk or Mode.Zoom || c.Squash != 1;
            if (c.Mode is Mode.Sit or Mode.Nap) wake = Math.Min(wake, c.Until);
        }
        if (made) CatPanel.Raise();                                                   // the main cat stays on top of its herd
        // still herd: sleep until the next mode timer ends or the floors are due — not 30 times a second
        timer!.Interval = TimeSpan.FromSeconds(busy ? Tick : Math.Max(wake - now, 0.01));
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
        else { c.Mode = Mode.Sit; c.Until = now + Rnd.In(2, 6); if (Rnd.Bool()) c.Facing *= -1; }
    }
}

/// One herd cat: a small borderless, transparent, topmost window that ignores the mouse, the character box + a margin
/// (CatLayout). Moving the cat is SetWindowPos — no redraw; the picture is redrawn only when the pose changes.
sealed class HerdCatWindow : Window
{
    public const double Pad = CatLayout.Margin * CatLayout.Zoom;          // the margin around the 46×62 box, on screen
    public const double W = CatLayout.ScreenW + 2 * Pad, H = CatLayout.ScreenH + 2 * Pad;
    const double Gap = 0.04, ZoomGap = 0.028;       // fewest seconds between two redraws: legs at ~15 fps walking, every tick dashing

    readonly CatSprite sprite = new();
    readonly CatCoat coat;
    IntPtr hwnd;
    double left, top;
    int px = int.MinValue, py = int.MinValue;
    // what is on the window now
    bool drawn;
    CatHerd.Mode dMode; double dPhase, dSquash, dFacing, dAt;

    public HerdCatWindow(CatCoat coat)
    {
        this.coat = coat;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = W; Height = H;
        Content = sprite;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            // never blocks a click: transparent to the mouse, never activates, no Alt-Tab entry
            Win32.AddExStyle(hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED);
            Display.Place(hwnd, new Rect(left, top, W, H));
        };
        Left = left; Top = top;
    }

    /// Put the window at (x, y) DIPs; true if it actually moved a pixel.
    public bool MoveTo(double x, double y)
    {
        left = x; top = y;
        int nx = Display.Px(x), ny = Display.Px(y);
        if (nx == px && ny == py) return false;
        px = nx; py = ny;
        if (hwnd == IntPtr.Zero) { Left = x; Top = y; Show(); return true; }                 // first time: shown where it belongs
        Win32.SetWindowPos(hwnd, IntPtr.Zero, nx, ny, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        return true;
    }

    public bool Stale(CatHerd.Cat c) => !drawn || dMode != c.Mode || dPhase != c.Phase || dSquash != c.Squash || dFacing != c.Facing;

    /// Redraw if the pose changed (a turn or a new mode at once; leg phase and squash at most every `Gap`). true if redrawn.
    public bool Draw(CatHerd.Cat c, double now)
    {
        if (!Stale(c)) return false;
        bool now_ = !drawn || dMode != c.Mode || dFacing != c.Facing;
        if (!now_ && now - dAt < (c.Mode == CatHerd.Mode.Zoom ? ZoomGap : Gap)) return false;
        drawn = true; dMode = c.Mode; dPhase = c.Phase; dSquash = c.Squash; dFacing = c.Facing; dAt = now;
        sprite.Draw(c, coat);
        return true;
    }
}

sealed class CatSprite : FrameworkElement
{
    readonly DrawingVisual visual = new();
    static readonly CatPose Pose = new() { Pin = false };
    public CatSprite() => AddVisualChild(visual);
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => visual;

    static CatState Skin(CatHerd.Cat c) => c.Mode switch
    {
        CatHerd.Mode.Fall => CatState.Alert,
        CatHerd.Mode.Walk or CatHerd.Mode.Zoom => CatState.Running,
        CatHerd.Mode.Nap => CatState.Sleeping,
        _ => CatState.Look,
    };

    public void Draw(CatHerd.Cat c, CatCoat coat)
    {
        using var dc = visual.RenderOpen();
        Pose.State = Skin(c); Pose.RunPhase = c.Phase; Pose.Stride = c.Mode == CatHerd.Mode.Zoom ? 38 : 22; Pose.CoatOverride = coat;
        dc.PushTransform(new TranslateTransform(HerdCatWindow.Pad, HerdCatWindow.Pad));
        dc.PushTransform(new ScaleTransform(CatLayout.Zoom, CatLayout.Zoom));
        dc.PushTransform(new ScaleTransform(c.Facing * (1 + (1 - c.Squash) * 0.6), c.Squash, 23, 49));
        CatDrawing.Draw(dc, Pose);
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}
