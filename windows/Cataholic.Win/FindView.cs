using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Cataholic;

// Find my cat — the window and what is in it (FindMyCat.swift: FindMyCat, FindWindow, FindCamera, FindMyCatView,
// FindViewport, FindStage, FindMarks, FindLanternLayer, FindCatView, PawMark, Spotlight, FindCelebration).
// The Mac stacks SwiftUI views; here the scene is a stack of small FrameworkElements inside one container that the
// camera scales and moves (WPF is retained-mode: a zoom or a pan re-composes the same vector drawing, nothing is
// re-recorded). Like the Mac's three observables, each layer repaints only for its own reason:
//   scene     (backdrop, props, cats by depth)  — a new layout, or the round ending
//   darkness  (the night lantern)               — the pointer moving, the zoom
//   marks     (paw prints and hints)            — a guess, the zoom
//   pop       (the found-it / spotlight cat)    — the round ending, and a 30 Hz clock for 2.2 s while the wave plays
// Nothing runs while you look: no timer exists except during that wave.

/// The view onto the scene: zoom (1×…3.5×) and pan. The scene lives in its own 1100×640 points; `Off` is where the
/// scene's top-left lands in the viewport. It never leaves the viewport (clamped), and every round starts at 1×.
sealed class FindCamera
{
    public const double MaxZ = 3.5;
    public double Z { get; private set; } = 1;
    public Point Off { get; private set; }
    public Point? Pointer;                       // last pointer spot over the viewport (no redraws hang on it)
    public Point? DragFrom;                      // a click-drag in progress: the offset it started at
    public bool Dragging;
    /// The view moved; the flag says whether to ease there (0.18 s) or jump.
    public event Action<bool>? Changed;

    public Point ToScene(Point p) => new((p.X - Off.X) / Z, (p.Y - Off.Y) / Z);
    public Point ToViewport(Point s) => new(Off.X + s.X * Z, Off.Y + s.Y * Z);
    static Point Clamped(Point o, double z)
    {
        double W = FindScene.Width, H = FindScene.Height;
        return new Point(Math.Min(0, Math.Max(W - W * z, o.X)), Math.Min(0, Math.Max(H - H * z, o.Y)));
    }
    void Set(double nz, Point o, bool animated)
    {
        Z = nz; Off = Clamped(o, nz);
        Changed?.Invoke(animated && !Fx.Reduced);
    }
    /// Zoom so the scene point under `p` (viewport points) stays under it.
    public void Zoom(double nz, Point p, bool animated = false)
    {
        nz = Math.Min(MaxZ, Math.Max(1, nz));
        var s = ToScene(p);
        Set(nz, new Point(p.X - s.X * nz, p.Y - s.Y * nz), animated);
    }
    /// The +/− buttons and Ctrl +/−: a step of ×1.5 about the pointer (or the middle).
    public void Step(int dir)
    {
        var p = Pointer ?? new Point(FindScene.Width / 2, FindScene.Height / 2);
        Zoom(dir > 0 ? Z * 1.5 : Z / 1.5, p, true);
    }
    public void Pan(Vector d) { if (Z > 1) Set(Z, new Point(Off.X + d.X, Off.Y + d.Y), false); }
    public void PanTo(Point o) => Set(Z, o, false);
    public void Reset(bool animated = false) => Set(1, new Point(0, 0), animated);
}

/// Open and close the Find my cat window — one at a time, a normal window of its own (title bar, taskbar button,
/// centred on the cat's screen, Esc closes).
static class FindMyCat
{
    static FindWindow? window;
    /// Test hooks (--find-level, --find-seed): the first game starts here.
    public static int? TestLevel;
    public static ulong? TestSeed;

    public static FindWindow? Window => window;
    public static bool IsOpen => window?.IsVisible == true;

    public static void Open()
    {
        if (window != null)
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            return;
        }
        var w = new FindWindow(new FindGame(TestLevel, TestSeed));
        window = w;
        w.Closed += (_, _) => { if (window == w) window = null; };    // the window drops its whole tree and the game itself
        w.Show();
        w.Activate();
    }
}

sealed class FindWindow : Window
{
    public const double HeaderH = 64;
    public readonly FindGame Game;
    public readonly FindCamera Camera = new();
    Point lantern = new(550, 430);

    readonly Grid root = new() { Width = FindScene.Width, Height = FindScene.Height + HeaderH };
    readonly Grid viewport = new() { Width = FindScene.Width, Height = FindScene.Height, ClipToBounds = true, Background = Brushes.Transparent };
    readonly Canvas stage = new() { Width = FindScene.Width, Height = FindScene.Height };
    readonly ScaleTransform zoomT = new(1, 1);
    readonly TranslateTransform offT = new();
    readonly FindLayer scene, darkness, nightLights, marks, spot, sparks, pop;
    readonly Border banner;
    readonly TextBlock bannerTitle, subtitle, levelLabel, zoomLabel;
    readonly Border zoomOut, zoomIn;
    readonly StackPanel paws = new() { Orientation = Orientation.Horizontal };
    readonly FrameworkElement portrait;
    readonly Button bannerButton;
    DispatcherTimer? clock;
    // what the scene layer was last painted for: it repaints only for a new layout or the round ending
    object? paintedCats; bool paintedPlaying;

    static readonly Brush Fg = Gfx.B(Gfx.C(0.12, 0.12, 0.13)), Dim = Gfx.B(Gfx.C(0.40, 0.40, 0.43)), Chrome = Gfx.B(Color.FromRgb(243, 243, 243));
    static readonly Color BannerInk = Gfx.C(0.24, 0.18, 0.16), Paper = Gfx.C(1, 0.98, 0.94);

    public FindWindow(FindGame game)
    {
        Game = game;
        Title = "Find my cat";
        ResizeMode = ResizeMode.CanMinimize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = true;
        Background = Chrome;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // ── the layers ──
        scene = new FindLayer(PaintScene); darkness = new FindLayer(PaintDarkness); nightLights = new FindLayer(g => FindArt.NightLights(g));
        marks = new FindLayer(PaintMarks); spot = new FindLayer(PaintSpotlight); sparks = new FindLayer(PaintSparks); pop = new FindLayer(PaintPop);
        pop.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 6, ShadowDepth = 2, Direction = 270 };
        foreach (var l in new[] { scene, darkness, nightLights, marks, spot, sparks, pop }) stage.Children.Add(l);
        stage.RenderTransform = new TransformGroup { Children = { zoomT, offT } };
        stage.IsHitTestVisible = false;
        viewport.Children.Add(stage);

        // ── the banner (found / lost) ──
        bannerTitle = new TextBlock { FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Gfx.B(BannerInk), VerticalAlignment = VerticalAlignment.Center };
        bannerButton = new Button
        {
            IsDefault = true, Margin = new Thickness(14, 0, 0, 0), Padding = new Thickness(18, 6, 18, 6), FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, Background = Gfx.B(CoatAccent.Of(CatCoat.Current)), BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Template = RoundedButton(),
        };
        bannerButton.Click += (_, _) => { Camera.Reset(); Game.Next(); };
        var bannerRow = new StackPanel { Orientation = Orientation.Horizontal };
        bannerRow.Children.Add(bannerTitle); bannerRow.Children.Add(bannerButton);
        banner = new Border
        {
            Child = bannerRow, Background = Gfx.B(Paper), BorderBrush = Gfx.B(FindScene.Ink), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 12, 20, 12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 22), Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 10, ShadowDepth = 3, Direction = 270 },
        };
        viewport.Children.Add(banner);

        // ── the header ──
        var me = CatCoat.Current;
        portrait = new FindPortrait(me) { Width = 40, Height = 52, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = "Find " + me.Name, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Fg };
        subtitle = new TextBlock { FontSize = 12, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title); texts.Children.Add(subtitle);
        zoomLabel = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Fg, MinWidth = 30, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        zoomOut = Tiny(GlyphText("", 13), () => Camera.Step(-1), "Zoom out (Ctrl −)");
        zoomIn = Tiny(GlyphText("", 13), () => Camera.Step(1), "Zoom in (Ctrl +) — or pinch");
        var mid = Tiny(zoomLabel, () => Camera.Reset(true), "Actual size (Ctrl 0)");
        var zoomBar = new Border
        {
            Background = Gfx.B(Color.FromArgb(15, 0, 0, 0)), CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { zoomOut, mid, zoomIn } },
        };
        levelLabel = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center };
        var levelBtn = new Border
        {
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { levelLabel, GlyphText("", 9, Dim, new Thickness(6, 1, 0, 0)) } },
            Background = Brushes.Transparent, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 10, 0), CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand, ToolTip = "Pick a level you've reached", VerticalAlignment = VerticalAlignment.Center,
        };
        levelBtn.MouseEnter += (_, _) => levelBtn.Background = Gfx.B(Color.FromArgb(15, 0, 0, 0));
        levelBtn.MouseLeave += (_, _) => levelBtn.Background = Brushes.Transparent;
        levelBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowLevels(levelBtn); };
        paws.ToolTip = "Guesses left"; paws.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel { Height = HeaderH, Margin = new Thickness(18, 0, 18, 0), LastChildFill = true };
        DockPanel.SetDock(portrait, Dock.Left); head.Children.Add(portrait);
        DockPanel.SetDock(paws, Dock.Right); head.Children.Add(paws);
        DockPanel.SetDock(levelBtn, Dock.Right); head.Children.Add(levelBtn);
        DockPanel.SetDock(zoomBar, Dock.Right); head.Children.Add(zoomBar);
        head.Children.Add(texts);

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderH) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FindScene.Height) });
        Grid.SetRow(head, 0); Grid.SetRow(viewport, 1);
        root.Children.Add(head); root.Children.Add(viewport);

        // On a screen too small for 1100×704 (+ the title bar) the whole window is scaled down to fit — never clipped.
        var work = Display.Visible;
        double k = Math.Min(1, Math.Min((work.Width - 16) / root.Width, (work.Height - 39) / root.Height));
        Content = k < 1 ? new Grid { LayoutTransform = new ScaleTransform(k, k), Children = { root } } : (UIElement)root;

        // ── wiring ──
        Game.Changed += OnGame;
        Camera.Changed += OnCamera;
        viewport.MouseLeftButtonDown += OnDown; viewport.MouseMove += OnMove; viewport.MouseLeftButtonUp += OnUp;
        viewport.MouseLeave += (_, _) => Camera.Pointer = null;
        viewport.MouseWheel += OnWheel;
        viewport.IsManipulationEnabled = true;                         // touch pinch
        viewport.ManipulationStarting += (_, e) => { e.ManipulationContainer = viewport; e.Mode = ManipulationModes.Scale; };
        viewport.ManipulationDelta += (_, e) =>
        {
            double s = e.DeltaManipulation.Scale.X;
            if (Math.Abs(s - 1) > 1e-4) Camera.Zoom(Camera.Z * s, e.ManipulationOrigin);
            e.Handled = true;
        };
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => Centre();
        Closed += OnClosed;
        OnGame();
        OnCamera(false);
    }

    /// Centred on the cat's screen (the primary monitor's work area), like NSWindow.center().
    void Centre()
    {
        var work = Display.Visible;
        Left = work.Left + Math.Max(0, (work.Width - ActualWidth) / 2);
        Top = work.Top + Math.Max(0, (work.Height - ActualHeight) / 2);
    }

    /// Closing drops the whole view tree and the game — nothing left running.
    void OnClosed(object? s, EventArgs e)
    {
        clock?.Stop(); clock = null;
        Game.Changed -= OnGame; Camera.Changed -= OnCamera;
        Content = null;
        stage.Children.Clear(); viewport.Children.Clear(); root.Children.Clear();
    }

    // ── the keyboard: Esc closes · Ctrl + / − / 0 zoom ──
    void OnKey(object? s, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (k == Key.Escape) { e.Handled = true; Close(); return; }
        if (!ctrl) return;
        if (k == Key.OemPlus || k == Key.Add) { Camera.Step(1); e.Handled = true; }
        else if (k == Key.OemMinus || k == Key.Subtract) { Camera.Step(-1); e.Handled = true; }
        else if (k == Key.D0 || k == Key.NumPad0) { Camera.Reset(true); e.Handled = true; }
        else if (k == Key.W) { e.Handled = true; Close(); }
    }

    // ── the mouse: a click (moving ≤ 4 pt) is a guess, in scene points; a click-drag pans when zoomed ──
    Point downAt;
    void OnDown(object? s, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInside(d, banner)) return;       // the banner's button
        downAt = e.GetPosition(viewport);
        Camera.DragFrom = Camera.Off; Camera.Dragging = false;
        viewport.CaptureMouse();
        e.Handled = true;
    }
    static bool IsInside(DependencyObject d, DependencyObject ancestor)
    {
        for (var c = d; c != null; c = c is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(c) : LogicalTreeHelper.GetParent(c)) if (c == ancestor) return true;
        return false;
    }
    void OnMove(object? s, MouseEventArgs e)
    {
        var p = e.GetPosition(viewport);
        Camera.Pointer = p;
        if (Camera.DragFrom is Point from && viewport.IsMouseCaptured)
        {
            var d = p - downAt;
            if (d.Length > 4) Camera.Dragging = true;
            if (Camera.Dragging && Camera.Z > 1) Camera.PanTo(new Point(from.X + d.X, from.Y + d.Y));
            return;
        }
        if (Game.Round.Night && Game.Phase == FindGame.Phases.Playing) { lantern = Camera.ToScene(p); darkness.Redraw(); }   // the night lantern follows the pointer (no timers)
    }
    void OnUp(object? s, MouseButtonEventArgs e)
    {
        if (Camera.DragFrom == null) return;
        viewport.ReleaseMouseCapture();
        if (!Camera.Dragging) Game.Guess(Camera.ToScene(e.GetPosition(viewport)));       // a drag is never a guess
        Camera.DragFrom = null; Camera.Dragging = false;
        e.Handled = true;
        if (Game.Round.Night && Game.Phase == FindGame.Phases.Playing) { lantern = Camera.ToScene(e.GetPosition(viewport)); darkness.Redraw(); }
    }
    /// A mouse wheel notch or Ctrl+wheel (a touchpad pinch arrives as Ctrl+wheel) zooms at the pointer; a touchpad's
    /// own scroll (deltas that are not whole notches) pans, Shift for sideways.
    void OnWheel(object? s, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(viewport);
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, notch = e.Delta % 120 == 0;
        if (!ctrl && !notch)
        {
            bool sideways = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            Camera.Pan(sideways ? new Vector(e.Delta, 0) : new Vector(0, e.Delta));
        }
        else
        {
            double d = Math.Max(-0.3, Math.Min(0.3, e.Delta / 1200.0));
            Camera.Zoom(Camera.Z * (1 + d), p);
        }
        e.Handled = true;
    }

    // ── model → view ──
    void OnGame()
    {
        var R = Game.Round; var me = CatCoat.Current; bool playing = Game.Phase == FindGame.Phases.Playing;
        subtitle.Text = R.Lookalikes == 0 ? $"{me.Name} is hiding somewhere in the kingdom. Three guesses — click where you think."
            : R.Night ? $"Night has fallen — your pointer is a lantern. Lookalikes about: only one is exactly {me.Name}."
            : "Lookalikes about — only one has exactly this collar, bell, face and tail.";
        levelLabel.Text = R.Level > 5 ? "Endless" : $"Level {R.Level} of 5";
        paws.Children.Clear();
        var acc = CoatAccent.Of(me);
        for (int i = 0; i < 3; i++)
        {
            var paw = MenuWindow.PawShape(Gfx.B(i < Game.GuessesLeft ? acc : Color.FromArgb(64, 110, 110, 118)), 17);
            paw.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
            paws.Children.Add(paw);
        }
        if (!ReferenceEquals(paintedCats, Game.Cats) || paintedPlaying != playing)
        {
            paintedCats = Game.Cats; paintedPlaying = playing;
            scene.Redraw();
        }
        nightLights.Visibility = R.Night ? Visibility.Visible : Visibility.Collapsed;
        nightLights.Redraw(); darkness.Redraw(); marks.Redraw(); spot.Redraw(); pop.Redraw(); sparks.Redraw();
        bool over = !playing, found = Game.Phase == FindGame.Phases.Found;
        int level = R.Level;
        banner.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
        bannerTitle.Text = !found ? "There she was!" : level >= 5 ? (level == 5 ? "Found her! Endless mode →" : "Found her!") : $"Found her! Level {level + 1} →";
        bannerButton.Content = !found ? "Try again" : level >= 5 ? "Next round" : $"Level {level + 1}";
        // the wave: a clock for two seconds, then one still frame
        if (Game.Celebrating && clock == null)
        {
            clock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
            clock.Tick += (_, _) =>
            {
                sparks.Redraw(); pop.Redraw();
                if (!Game.Celebrating || Game.Phase != FindGame.Phases.Found) { clock?.Stop(); clock = null; sparks.Redraw(); pop.Redraw(); }
            };
            clock.Start();
        }
        else if (!Game.Celebrating && clock != null) { clock.Stop(); clock = null; sparks.Redraw(); pop.Redraw(); }
    }

    bool shown;
    void OnCamera(bool animated)
    {
        double z = Camera.Z; var o = Camera.Off;
        if (animated && shown)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromSeconds(0.18);
            double z0 = zoomT.ScaleX, x0 = offT.X, y0 = offT.Y;
            zoomT.ScaleX = zoomT.ScaleY = z; offT.X = o.X; offT.Y = o.Y;          // the base values are the end state…
            zoomT.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(z0, z, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            zoomT.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(z0, z, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            offT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x0, o.X, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            offT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y0, o.Y, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
        else
        {
            zoomT.BeginAnimation(ScaleTransform.ScaleXProperty, null); zoomT.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            offT.BeginAnimation(TranslateTransform.XProperty, null); offT.BeginAnimation(TranslateTransform.YProperty, null);
            zoomT.ScaleX = zoomT.ScaleY = z; offT.X = o.X; offT.Y = o.Y;
        }
        shown = true;
        zoomLabel.Text = z < 1.05 ? "1×" : string.Format(CultureInfo.InvariantCulture, "{0:0.0}×", z);
        SetEnabled(zoomOut, z > 1.001); SetEnabled(zoomIn, z < FindCamera.MaxZ - 0.01);
        darkness.Redraw(); marks.Redraw();                         // both are drawn at a size that follows the zoom
    }

    // ── the level picker: 1 · 2 · 3 · 4 · 5 · Endless, each with its place; levels not reached yet are locked ──
    void ShowLevels(FrameworkElement at)
    {
        int cur = Game.Round.Level, max = FindGame.MaxLevel;
        var menu = new ContextMenu { PlacementTarget = at, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        for (int l = 1; l <= 6; l++)
        {
            int level = l;
            var item = new MenuItem { Header = FindGame.LevelName(l), IsEnabled = l <= max };
            if (l == cur) item.Icon = GlyphText("", 11, Fg);
            else if (l > max) item.Icon = GlyphText("", 11, Dim);
            item.Click += (_, _) => { Camera.Reset(); Game.Play(level); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ── small pieces ──
    static TextBlock GlyphText(string glyph, double size, Brush? fg = null, Thickness? margin = null) => new()
    {
        Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, Foreground = fg ?? Fg, VerticalAlignment = VerticalAlignment.Center,
        Margin = margin ?? default,
    };
    static Border Tiny(UIElement content, Action click, string tip)
    {
        var b = new Border { Child = content, Padding = new Thickness(7, 3, 7, 3), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip };
        b.MouseEnter += (_, _) => b.Background = Gfx.B(Color.FromArgb(18, 0, 0, 0));
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }
    static void SetEnabled(Border b, bool on) { b.IsHitTestVisible = on; b.Opacity = on ? 1 : 0.35; }
    static ControlTemplate RoundedButton()
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }

    // ── for the tests ──
    /// The screen pixel of a scene point (what a real click would hit).
    public Point ScreenPixelOfScene(Point scene) => viewport.PointToScreen(Camera.ToViewport(scene));
    /// The window's contents (header + scene, 1100×704, unscaled) as a PNG — independent of the screen size.
    public void SavePng(string path)
    {
        root.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)root.Width, (int)root.Height, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(Chrome, null, new Rect(0, 0, root.Width, root.Height));
        bmp.Render(bg); bmp.Render(root);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = System.IO.File.Create(path); enc.Save(f);
    }

    // ═════════ the layers ═════════

    /// The scene: backdrop, then props and cats sorted into it by depth, so a cat behind a bush is behind it.
    void PaintScene(Gfx g)
    {
        var R = Game.Round; var props = FindScene.Props(R.Place);
        bool crop = CatDrawing.CropToBox; CatDrawing.CropToBox = false;      // the game does not crop a cat to its box (the melted nap's tail pokes out)
        try
        {
            FindArt.Backdrop(g, R.Place);
            foreach (var item in FindScene.DepthOrder(R.Place, Game.Cats))
            {
                if (item.IsProp) FindArt.Prop(g, props[item.Index]);
                else if (!(item.Index == Game.Yours && Game.Phase != FindGame.Phases.Playing)) FindDraw.Cat(g, Game.Cats[item.Index]);
            }
        }
        finally { CatDrawing.CropToBox = crop; }
    }

    /// Night: near-dark everywhere except a soft lantern circle (~85 pt) around the pointer. While you search, the lantern
    /// shows true colours (the details matter); once the round is over the whole kingdom takes the moonlit tint instead.
    void PaintDarkness(Gfx g)
    {
        if (!Game.Round.Night) return;
        var full = new Rect(0, 0, FindScene.Width, FindScene.Height);
        if (Game.Phase == FindGame.Phases.Playing)
        {
            double R = 85 / Camera.Z;                               // ~85 pt on screen at any zoom
            var warm = Gfx.C(1, 0.85, 0.5);
            var brush = Gfx.Radial(lantern, R, (0, Gfx.Clear(warm)), (0.45, Gfx.Op(warm, 0.06)), (1, Gfx.Op(Gfx.C(0.03, 0.04, 0.11), 0.965)));
            g.Dc.DrawRectangle(brush, null, full);
            g.Stroke(Gfx.Ell(lantern.X - R * 0.98, lantern.Y - R * 0.98, R * 1.96, R * 1.96), Gfx.Op(Gfx.C(1, 0.86, 0.5), 0.18), 3 / Camera.Z);
        }
        else
        {
            // The Mac multiplies the scene by (0.40, 0.44, 0.68). WPF has no blend modes: a translucent navy gives the same
            // result on white (0.40, 0.44, 0.68) and close on mid tones.
            g.Dc.DrawRectangle(Gfx.B(Gfx.Op(Gfx.C(0.143, 0.2, 0.543), 0.7)), null, full);
        }
    }

    /// The paw prints and hints: glued to the art (scene points) but kept at their screen size whatever the zoom.
    void PaintMarks(Gfx g)
    {
        var tint = CoatAccent.Of(CatCoat.Current);
        for (int i = 0; i < Game.Guesses.Count; i++)
        {
            var gu = Game.Guesses[i]; bool latest = gu.Id == Game.Guesses.Count - 1;
            var dc = g.Dc;
            dc.PushTransform(new TranslateTransform(gu.At.X, gu.At.Y));
            dc.PushTransform(new ScaleTransform(1 / Camera.Z, 1 / Camera.Z));
            // the paw print: 28×24, turned a little more with every guess, white halo under the tint
            dc.PushTransform(new TranslateTransform(-14, -12));
            dc.PushTransform(new RotateTransform(gu.Id * 17 - 12, 14, 12));
            var parts = new[] { new FRect(5, 10, 14, 12), new FRect(2.5, 4, 5, 6.2), new FRect(8, 0.5, 5, 6.5), new FRect(14, 0.5, 5, 6.5), new FRect(19.5, 4, 5, 6.2) };
            foreach (var p in parts) g.Stroke(Gfx.Ell(p), Colors.White, 3);
            foreach (var p in parts) g.Fill(Gfx.Ell(p), tint);
            dc.Pop(); dc.Pop();
            // the hint in a capsule beside it
            var ft = new FormattedText(gu.Hint, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), latest ? 14 : 12, Gfx.B(BannerInk), Ppd);
            double w = ft.Width + 18, h = ft.Height + 8, x0 = gu.At.X > FindScene.Width - 140 ? -110 : 18, y0 = -14;
            if (!latest) dc.PushOpacity(0.75);
            g.Fill(Gfx.RBox(x0, y0, w, h, h / 2), Paper);
            g.Stroke(Gfx.RBox(x0 + 0.75, y0 + 0.75, w - 1.5, h - 1.5, (h - 1.5) / 2), Gfx.Op(FindScene.Ink, 0.8), 1.5);
            dc.DrawText(ft, new Point(x0 + 9, y0 + 4));
            if (!latest) dc.Pop();
            dc.Pop(); dc.Pop();
        }
    }
    public static double Ppd = 1;

    /// Three misses: the kingdom dims, with a soft spotlight on where your cat was.
    void PaintSpotlight(Gfx g)
    {
        if (Game.Phase != FindGame.Phases.Lost) return;
        var c = FindCelebrationInfo.Popped(Game.Mine);
        double ax = c.MidX, ay = c.MidY;
        var hole = new EllipseGeometry(new Rect(ax - 58, ay - 58, 116, 116));
        var eo = new GeometryGroup { FillRule = FillRule.EvenOdd };
        eo.Children.Add(new RectangleGeometry(new Rect(0, 0, FindScene.Width, FindScene.Height))); eo.Children.Add(hole);
        eo.Freeze();
        g.Fill(eo, Gfx.Op(Colors.Black, 0.55));
        g.Stroke(Gfx.Ell(ax - 58, ay - 58, 116, 116), Gfx.Op(Gfx.C(1, 0.95, 0.75), 0.9), 3);
    }

    /// Seconds into the found-it celebration (99 = settled).
    double CelebrationT => Game.Phase == FindGame.Phases.Lost ? 99 : Game.Celebrating ? (DateTime.Now - Game.FoundAt).TotalSeconds : 99;

    /// Found it: sparkles fly out and fade.
    void PaintSparks(Gfx g)
    {
        if (Game.Phase != FindGame.Phases.Found) return;
        double t = CelebrationT;
        if (t >= 1.5) return;
        var cat = Game.Mine; var tint = CoatAccent.Of(CatCoat.Current);
        var center = FindDraw.PopCenter(cat);
        double fade = Math.Max(0, 1 - t / 1.5);
        for (int i = 0; i < 16; i++)
        {
            double a = i / 16.0 * 2 * Math.PI + 0.3;
            double d = 34 + 110 * (1 - Math.Pow(1 - Math.Min(1, t / 1.0), 2)) * (i % 2 == 0 ? 1 : 0.75);
            double px = center.X + Math.Cos(a) * d, py = center.Y + Math.Sin(a) * d;
            double r = i % 2 == 0 ? 9 : 6;
            var star = new FPath().M(px, py - r).Q(px + r, py, px, py).Q(px, py + r, px, py).Q(px - r, py, px, py).Q(px, py - r, px, py);
            Geometry sg = star;
            g.Stroke(sg, Gfx.Op(Colors.White, fade), 2);
            g.Fill(sg, Gfx.Op(i % 3 == 0 ? tint : Gfx.C(1, 0.80, 0.30), fade));
        }
    }

    /// Found it: your cat pops up out of its hiding place, twice its size, and waves (the Hi emote's own timing).
    /// Three misses: the same pop, sitting still, under the spotlight.
    void PaintPop(Gfx g)
    {
        if (Game.Phase == FindGame.Phases.Playing) return;
        FindDraw.Pop(g, Game.Mine, CelebrationT, Game.Phase == FindGame.Phases.Found);
    }
}

/// A layer of the stage: a transparent 1100×640 element that paints one thing and repaints only when told.
sealed class FindLayer : FrameworkElement
{
    readonly Action<Gfx> paint;
    public FindLayer(Action<Gfx> paint)
    {
        this.paint = paint;
        Width = FindScene.Width; Height = FindScene.Height; IsHitTestVisible = false; ClipToBounds = true;
    }
    public void Redraw() => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        FindWindow.Ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        paint(new Gfx(dc));
    }
}

/// The header's little portrait: your cat, sitting, in the game's bolder collar.
sealed class FindPortrait : FrameworkElement
{
    readonly CatPose pose;
    public FindPortrait(CatCoat coat)
    {
        pose = new CatPose { State = CatState.Look, Pin = false, CoatOverride = coat, CollarBold = FindDraw.Collar };
        IsHitTestVisible = false;
    }
    protected override void OnRender(DrawingContext dc)
    {
        // 92 × 124 pt drawn at 0.42 inside a 40 × 52 frame: 0.84 per box point, centred
        double k = CatLayout.Zoom * 0.42;
        dc.PushTransform(new TranslateTransform((Width - CatLayout.CharW * k) / 2, (Height - CatLayout.CharH * k) / 2));
        dc.PushTransform(new ScaleTransform(k, k));
        CatDrawing.Draw(dc, pose);
        dc.Pop(); dc.Pop();
    }
}

/// How the game draws a cat (FindCatView, findCharacter) and the popped one (FindCelebration).
static class FindDraw
{
    /// Every cat in the game wears a slightly bolder collar and bell, so a lookalike's wrong collar reads at this size.
    public const double Collar = 1.6;

    /// A cat as the game draws it — a lookalike's wrong collar, missing bell or tail tip included.
    public static CatPose Character(FindCat cat, CatState? state = null, double pawRaise = 1, double pawWave = 0, double pawWrist = 0, CatEyes? eyes = null)
    {
        bool dark = CatCoat.Current.Id == "tux" || CatCoat.Current.Id == "cocoa";
        return new CatPose
        {
            State = state ?? cat.State, Badge = "", Pin = false,
            RunPhase = cat.State == CatState.Running ? 0.9 + cat.Id % 3 : 0,
            CoatOverride = cat.Coat,
            PawRaise = pawRaise, PawWave = pawWave, Eyes = eyes, PawWrist = pawWrist,
            CollarTint = cat.Odd == FindOdd.Collar ? Gfx.C(0.86, 0.30, 0.36) : null,
            Bell = cat.Odd != FindOdd.Bell,
            TailTip = cat.Odd == FindOdd.Tail ? (dark ? Gfx.C(0.98, 0.96, 0.92) : Gfx.C(0.30, 0.24, 0.23)) : null,
            CollarBold = Collar,
        };
    }

    /// One cat in the scene: the 46×62 box scaled by the cat's size, mirrored about its middle when it faces the other way.
    public static void Cat(Gfx g, FindCat cat)
    {
        var b = cat.Box;
        var dc = g.Dc;
        dc.PushTransform(new TranslateTransform(b.MinX + (cat.Flip ? 46 * cat.Scale : 0), b.MinY));
        dc.PushTransform(new ScaleTransform((cat.Flip ? -1 : 1) * cat.Scale, cat.Scale));
        CatDrawing.Draw(dc, Character(cat));
        dc.Pop(); dc.Pop();
    }

    /// Where the sparkles burst from.
    public static Point PopCenter(FindCat cat)
    {
        var b = cat.Box;
        return new Point(b.MidX, cat.Feet.Y - FindCelebrationInfo.Rise(cat) - cat.Rect.H * FindCelebrationInfo.Grow / 2);
    }

    /// FindCelebration's cat at `t` seconds (99 = settled).
    public static void Pop(Gfx g, FindCat cat, double t, bool wave)
    {
        double u = Math.Min(1, Math.Max(0, t / 0.4));
        double pop = 1 - Math.Pow(1 - u, 3);                      // ease out…
        double over = u < 1 ? Math.Sin(u * Math.PI) * 0.12 : 0;   // …with a little overshoot
        double k = 1 + (FindCelebrationInfo.Grow - 1) * (pop + over);
        double rise = FindCelebrationInfo.Rise(cat) * pop;
        var f = Emote.Frame(CatModel.Action.Hi, Math.Min(t, 1.62));
        var b = cat.Box;
        var pose = Character(cat, wave ? CatState.Hi : CatState.Look, Math.Max(f.Paw, t >= 1.62 ? 1 : 0), f.Wave, f.Wrist,
                             wave && t > 0.5 && t < 1.3 ? CatEyes.Happy : null);
        // The Mac scales a 46z × 62z view about (0.5, 49/62) inside a frame of the cat's own size, so its feet end up
        // 18 · (z − scale) lower than the scale alone would put them; the same arithmetic, so the same place.
        double s = cat.Scale * k, sx = (cat.Flip && !wave ? -1 : 1) * s;
        var dc = g.Dc;
        bool crop = CatDrawing.CropToBox; CatDrawing.CropToBox = false;
        try
        {
            dc.PushTransform(new TranslateTransform(b.MinX, b.MinY - rise));
            dc.PushTransform(new TranslateTransform(23 * cat.Scale, 31 * cat.Scale + 18 * CatLayout.Zoom));
            dc.PushTransform(new ScaleTransform(sx, s));
            dc.PushTransform(new TranslateTransform(-23, -49));
            CatDrawing.Draw(dc, pose);
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }
        finally { CatDrawing.CropToBox = crop; }
    }
}

/// `--check-find [N]`: the fairness self-check without a window. WinExe has no console of its own: the table goes to
/// stdout when it is redirected (CI), else to the console that started it.
static class FindCli
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    public static int Check(string[] args, int flagAt)
    {
        int n = flagAt + 1 < args.Length && int.TryParse(args[flagAt + 1], out var v) ? v : 200;
        if (!Console.IsOutputRedirected) AttachConsole(-1);
        return FindCheck.Run(n, Console.WriteLine);
    }
}
