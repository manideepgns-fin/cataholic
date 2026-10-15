using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Cataholic;

// CatMenu — the cat's menu as one crafted panel, not a settings form (CatMenu.swift; see docs/menu.png). The same panel
// opens from the paw in the tray (a click on it) and from a right-click on the cat: a borderless window beside the
// pointer, closed by a click anywhere else, by Esc, or by picking something. Top to bottom, loudest first:
//   1. Header — your cat, big, with its name, the eight coats as a strip of tiny cats beside it, and Zoomies.
//   2. Do something — the 3×3 action grid, the hero of the panel; "On her own" switch.
//   3. More cats — the herd size, one segmented row.
//   4. Switches — Control-Center chips (accent = on): Show cats · Sounds · Sleep · At login.
//   5. Footer — the version and Quit. (No Grid chip and no "Check for updates" in this release.)
// Nothing in the panel changes its height while it is open. Every value lives where it always did (the prefs file).
static class CatMenuPanel
{
    static MenuWindow? win;
    static Later? watch;

    public static bool IsOpen => win != null;
    /// When it last closed (a click on the tray paw that closed it must not reopen it).
    public static DateTime ClosedAt = DateTime.MinValue;

    public static void Show(Point at)
    {
        Close();
        Display.Refresh();
        var w = new MenuWindow(Close);
        win = w;
        w.Build();
        // Beside the pointer, kept on the screen: right of it, else left of it; its top at the pointer, kept inside the work area.
        var vis = Display.Visible; var size = w.CardSize;
        double x = at.X + 8, y = at.Y;
        if (x + size.Width > vis.Right) x = at.X - size.Width - 8;
        x = Math.Max(vis.Left + 4, x);
        y = Math.Min(Math.Max(vis.Top + 4, y), vis.Bottom - size.Height - 4);
        w.PlaceCard(x, y);
        w.Show();
        // click-away / Esc: polled while open (the window never takes focus, so it cannot hear them itself)
        bool prev = Win32.AnyMouseButton;
        watch = Later.Every(0.05, _ =>
        {
            bool down = Win32.AnyMouseButton;
            if (Win32.KeyDown(Win32.VK_ESCAPE)) { Close(); return; }
            if (down && !prev)
            {
                var p = Display.Pointer();
                bool onCat = CatPanel.CharScreenRect() is Rect r && r.Contains(p);
                if (!w.CardScreenRect().Contains(p) && !onCat) Close();
            }
            prev = down;
        });
    }

    public static void Close()
    {
        watch?.Cancel(); watch = null;
        var w = win; win = null;
        if (w != null) ClosedAt = DateTime.Now;
        w?.Close();
    }
}

/// The panel takes its colour from the cat you picked (owner 13 Oct 2026). One accent per coat, drawn from its fur,
/// deep enough that a white glyph on it reads in light and dark.
static class CoatAccent
{
    public static Color Of(CatCoat c) => c.Id switch
    {
        "smokey" => CatCoat.C(0.42, 0.50, 0.66),     // slate
        "patches" => CatCoat.C(0.86, 0.44, 0.28),    // calico terracotta
        "tux" => CatCoat.C(0.28, 0.47, 0.82),        // tuxedo blue
        "butter" => CatCoat.C(0.84, 0.62, 0.14),     // butter gold
        "cocoa" => CatCoat.C(0.60, 0.39, 0.25),      // cocoa
        "snow" => CatCoat.C(0.36, 0.66, 0.86),       // ice blue
        "lilac" => CatCoat.C(0.60, 0.47, 0.84),      // lilac
        _ => CatCoat.C(0.95, 0.55, 0.20),            // ginger orange
    };
}

/// A cat drawn small: the character draws at zoom × its 46×62 box, so scale it into the frame it lands in.
sealed class CatPicture : FrameworkElement
{
    readonly CatCoat coat; readonly double scale;
    readonly CatPose pose;
    public CatPicture(CatCoat c, double scale, double w, double h)
    {
        coat = c; this.scale = scale; Width = w; Height = h; ClipToBounds = true;
        pose = new CatPose { State = CatState.Look, Pin = false, CoatOverride = c };
        IsHitTestVisible = false;
    }
    protected override void OnRender(DrawingContext dc)
    {
        double k = CatLayout.Zoom * scale;
        dc.PushTransform(new TranslateTransform((Width - CatLayout.CharW * k) / 2, (Height - CatLayout.CharH * k) / 2));
        dc.PushTransform(new ScaleTransform(k, k));
        CatDrawing.Draw(dc, pose);
        dc.Pop(); dc.Pop();
    }
}

sealed class MenuWindow : Window
{
    readonly System.Action close;
    readonly Border card = new();
    readonly Grid shell = new();
    const double Pad = 14, Width_ = 300, Margin_ = 12;    // the card, and the room around it for its shadow
    IntPtr hwnd;
    Rect placed;
    Theme th = Theme.Current();

    /// Light or dark, from the system's app theme.
    sealed record Theme(bool Dark, Color Bg, Color Fg, Color Dim, Color Faint, Color Ink)
    {
        public static Theme Current()
        {
            bool light = true;
            try { light = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) as int? ?? 1) != 0; } catch { }
            return light
                ? new Theme(false, Color.FromRgb(247, 245, 243), Color.FromRgb(29, 29, 31), Color.FromRgb(110, 110, 115), Color.FromRgb(160, 160, 166), Colors.Black)
                : new Theme(true, Color.FromRgb(44, 43, 46), Color.FromRgb(245, 245, 247), Color.FromRgb(166, 166, 172), Color.FromRgb(110, 110, 115), Colors.White);
        }
    }
    Color Accent => CoatAccent.Of(CatCoat.Current);
    Brush Ink(byte a) => Br(Color.FromArgb(a, th.Ink.R, th.Ink.G, th.Ink.B));
    static Brush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static readonly FontFamily Ui = new("Segoe UI"), Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public MenuWindow(System.Action close)
    {
        this.close = close;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        FontFamily = Ui; UseLayoutRounding = true; SnapsToDevicePixels = true;
        shell.Margin = new Thickness(Margin_);
        card.CornerRadius = new CornerRadius(12);
        card.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.32, Color = Colors.Black };
        shell.Children.Add(card);
        Content = shell;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
            Display.Place(hwnd, new Rect(placed.X - Margin_, placed.Y - Margin_, placed.Width + 2 * Margin_, placed.Height + 2 * Margin_));
        };
        // drag it by any empty spot (owner 14 Oct 2026)
        card.MouseLeftButtonDown += (_, e) => { try { DragMove(); } catch { } };
    }

    public Size CardSize { get; private set; }
    public void PlaceCard(double x, double y)
    {
        placed = new Rect(x, y, CardSize.Width, CardSize.Height);
        Left = x - Margin_; Top = y - Margin_; Width = CardSize.Width + 2 * Margin_; Height = CardSize.Height + 2 * Margin_;
    }
    /// The card's rect on screen right now (it can be dragged).
    public Rect CardScreenRect()
    {
        if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var r)) return placed;
        var w = Display.ToDip(r);
        return new Rect(w.X + Margin_, w.Y + Margin_, Math.Max(0, w.Width - 2 * Margin_), Math.Max(0, w.Height - 2 * Margin_));
    }

    public void Build()
    {
        th = Theme.Current();
        card.Background = Br(Color.FromArgb(0xF7, th.Bg.R, th.Bg.G, th.Bg.B));
        card.BorderBrush = Ink(26); card.BorderThickness = new Thickness(1);
        card.Child = Content_();
        card.Measure(new Size(Width_, double.PositiveInfinity));
        CardSize = new Size(Width_, Math.Ceiling(card.DesiredSize.Height));
        card.Width = CardSize.Width; card.Height = CardSize.Height;
    }
    /// Rebuild after a pref changed (the panel redraws its own state; the size never changes).
    void Refresh() => Dispatcher.BeginInvoke(new System.Action(() => { card.Child = Content_(); }));

    // ── pieces ──
    TextBlock Txt(string s, double size, Color c, FontWeight? w = null) =>
        new() { Text = s, FontSize = size, Foreground = Br(c), FontWeight = w ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None };
    TextBlock Glyph(string glyph, double size, Brush fg) =>
        new() { Text = glyph, FontFamily = Glyphs, FontSize = size, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    UIElement Content_()
    {
        var col = new StackPanel { Margin = new Thickness(Pad) };
        col.Children.Add(Header());
        col.Children.Add(DoSomething());
        col.Children.Add(MoreCats());
        col.Children.Add(Chips());
        col.Children.Add(Footer());
        return col;
    }

    UIElement Header()
    {
        var coat = CatCoat.Current; var acc = Accent;
        var g = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var bubble = new Grid { Width = 54, Height = 54, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        bubble.Children.Add(new Ellipse { Fill = Br(Color.FromArgb(36, acc.R, acc.G, acc.B)) });
        bubble.Children.Add(new CatPicture(coat, 0.46, 46, 52) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(bubble, 0); g.Children.Add(bubble);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 5), LastChildFill = false };
        var name = Txt(coat.Name, 15, th.Fg, FontWeights.SemiBold);
        DockPanel.SetDock(name, Dock.Left); top.Children.Add(name);
        var zoom = Pill(new StackPanel { Orientation = Orientation.Horizontal, Children = { Glyph("\uE945", 11, Br(acc)), Gap(5), Txt("Zoomies!", 11, acc, FontWeights.SemiBold) } },
            Color.FromArgb(38, acc.R, acc.G, acc.B), Color.FromArgb(66, acc.R, acc.G, acc.B), () =>
            {
                close();
                var m = CatPanel.CurrentModel; m?.StopAction(); CatPanel.StopLife(); CatSound.Meow(); CatPanel.Zoomies();
            });
        DockPanel.SetDock(zoom, Dock.Right); top.Children.Add(zoom);
        right.Children.Add(top);
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in CatCoat.All) strip.Children.Add(CoatDot(c, c == coat));
        right.Children.Add(strip);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return g;
    }
    static UIElement Gap(double w) => new Border { Width = w };

    /// A capsule button: a fill that deepens on hover.
    Border Pill(UIElement content, Color fill, Color hover, System.Action click)
    {
        var b = new Border { CornerRadius = new CornerRadius(99), Background = Br(fill), Padding = new Thickness(9, 4, 9, 4), Child = content, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = Br(hover);
        b.MouseLeave += (_, _) => b.Background = Br(fill);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return b;
    }

    /// One coat in the header strip: a tiny cat, ringed in the accent when it is the one on your screen.
    UIElement CoatDot(CatCoat c, bool selected)
    {
        var b = new Border
        {
            Width = 23, Height = 25, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 2, 0),
            BorderBrush = Br(Accent), BorderThickness = new Thickness(selected ? 1.5 : 0), Cursor = Cursors.Hand,
            Child = new CatPicture(c, 0.19, 21, 23) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            ToolTip = c.Name,
        };
        b.MouseEnter += (_, _) => b.Background = Ink(31);
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CatCoat.Current = c; Prefs.Set("crewCoat", c.Id);
            CatPanel.ApplyPrefs(); CatPanel.CurrentModel?.Pet(); Refresh();
        };
        return b;
    }

    UIElement Heading(string s) => Txt(s, 11, th.Dim, FontWeights.SemiBold);

    UIElement DoSomething()
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        var h = Heading("Do something"); DockPanel.SetDock(h, Dock.Left); head.Children.Add(h);
        // Owner 13 Oct 2026: "let her do all the stuff that in do something" — on her own, like the extra cats.
        bool on = CatPrefs.OnHerOwn; var acc = Accent;
        var knob = new Ellipse { Width = 11, Height = 11, Fill = Brushes.White, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(2) };
        var track = new Border { Width = 26, Height = 15, CornerRadius = new CornerRadius(8), Background = on ? Br(acc) : Ink(64), Child = knob };
        var sw = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        sw.Children.Add(Txt("On her own", 11, th.Dim)); sw.Children.Add(Gap(6)); sw.Children.Add(track);
        sw.MouseLeftButtonDown += (_, e) => e.Handled = true;
        sw.MouseLeftButtonUp += (_, e) => { e.Handled = true; CatPrefs.SetOnHerOwn(!on); Refresh(); };
        sw.ToolTip = "Your cat does these by herself every few seconds while you work: walks, climbs, zoomies";
        DockPanel.SetDock(sw, Dock.Right); head.Children.Add(sw);
        box.Children.Add(head);
        var grid = new UniformGrid { Columns = 3 };
        foreach (var (action, name) in CatModel.RingActions) grid.Children.Add(Tile(action, name));
        box.Children.Add(grid);
        return box;
    }

    /// An action: an icon over a short label, lighter on hover.
    UIElement Tile(CatModel.Action a, string title)
    {
        var acc = Accent;
        var icon = ActionIcon(a, th.Fg);
        var label = Txt(title, 11, th.Fg);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var st = new StackPanel();
        icon.Margin = new Thickness(0, 0, 0, 4); icon.HorizontalAlignment = HorizontalAlignment.Center;
        st.Children.Add(icon); st.Children.Add(label);
        var b = new Border { CornerRadius = new CornerRadius(9), Background = Ink(14), Padding = new Thickness(0, 8, 0, 8), Margin = new Thickness(2.5), Child = st, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => { b.Background = Ink(31); SetStroke(icon, Br(acc)); };
        b.MouseLeave += (_, _) => { b.Background = Ink(14); SetStroke(icon, Br(th.Fg)); };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (CatPanel.CurrentModel is not CatModel m) return;
            close();
            CatSound.ForAction(a); m.Tap(a);
        };
        return b;
    }
    static void SetStroke(FrameworkElement icon, Brush b) { if (icon is Path p) p.Stroke = b; }

    // The nine action icons, line drawings on a 16-unit grid (SF Symbols have no Windows twin worth a font glyph).
    static readonly Dictionary<CatModel.Action, string> IconPaths = new()
    {
        [CatModel.Action.Hi] = "M4.5 8.5 V4 M7 7.5 V2.5 M9.5 7.5 V3 M12 8.5 V5 M4.5 9 L2.5 7.5 M4.5 8.5 C4.5 13 6 14.5 8.5 14.5 C11 14.5 12 12.5 12 8.5",
        [CatModel.Action.Big] = "M9.5 2.5 H13.5 V6.5 M13.5 2.5 L9 7 M6.5 13.5 H2.5 V9.5 M2.5 13.5 L7 9",
        [CatModel.Action.Stretch] = "M3 3.5 A1.3 1.3 0 1 0 3.01 3.5 M4 6 L8 8.5 L13.5 6.5 M8 8.5 L7 13.5 M8 8.5 L10.5 13.5 M4 6 L2 9.5",
        [CatModel.Action.Yawn] = "M2 8 C5 3.5 11 3.5 14 8 C11 12.5 5 12.5 2 8 Z M2 8 H14",
        [CatModel.Action.Groom] = "M2 4 H14 V7 H2 Z M4 7 V12.5 M6.5 7 V12.5 M9 7 V12.5 M11.5 7 V12.5",
        [CatModel.Action.Spin] = "M13 8 A5 5 0 1 1 10.5 3.7 M10.8 1.5 V4.2 H13.5",
        [CatModel.Action.Look] = "M1.5 8 C4 3.5 12 3.5 14.5 8 C12 12.5 4 12.5 1.5 8 Z M8 6 A2 2 0 1 0 8.01 6",
        [CatModel.Action.Jump] = "M2 12 C4 3 12 3 14 12 M11.5 10.5 L14 12 L14.6 9 M1.5 14 H14.5",
        [CatModel.Action.Nap] = "M2 12 V4 M2 9.5 H14 V12 M14 9.5 V7.5 H7 V9.5 M2 12 H14",
    };
    static FrameworkElement ActionIcon(CatModel.Action a, Color fg) => new Path
    {
        Data = Geometry.Parse(IconPaths[a]), Stroke = Br(fg), StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Fill = Brushes.Transparent,
        Width = 18, Height = 18, Stretch = Stretch.Uniform,
    };

    UIElement MoreCats()
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var h = Heading("More cats"); ((FrameworkElement)h).Margin = new Thickness(0, 0, 0, 6);
        box.Children.Add(h);
        var seg = new UniformGrid { Rows = 1 };
        int cur = CatHerd.Count;
        foreach (int n in CatHerd.Sizes)
        {
            int nn = n; bool sel = n == cur;
            var t = Txt(n == 0 ? "None" : n.ToString(), 11, th.Fg, sel ? FontWeights.Medium : FontWeights.Normal);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(0, 3, 0, 3), Background = sel ? Ink(40) : Brushes.Transparent, Child = t, Cursor = Cursors.Hand };
            if (!sel) { b.MouseEnter += (_, _) => b.Background = Ink(18); b.MouseLeave += (_, _) => b.Background = Brushes.Transparent; }
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; CatHerd.Set(nn); Refresh(); };
            seg.Children.Add(b);
        }
        box.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = Ink(14), Padding = new Thickness(1), Child = seg });
        return box;
    }

    UIElement Chips()
    {
        var row = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 0) };
        row.Children.Add(Chip("Show cats", true, null, CatPrefs.ShowCrew, CatPrefs.SetShowCrew));
        row.Children.Add(Chip("Sounds", false, "\uE767", CatSound.IsOn, CatSound.SetEnabled));
        row.Children.Add(Chip("Sleep", false, "\uE708", CatPrefs.Quiet, CatPrefs.SetQuiet));
        row.Children.Add(Chip("At login", false, "\uE7E8", Startup.IsOn, Startup.Set));
        return row;
    }

    /// A switch as a Control-Center chip: an accent circle when on, a quiet one when off, a short caption beneath.
    UIElement Chip(string title, bool paw, string? glyph, bool on, Action<bool> set)
    {
        var acc = Accent;
        var fg = on ? Brushes.White : Ink(190);
        FrameworkElement icon = paw ? PawShape(fg, 14) : Glyph(glyph!, 13, fg);
        var circle = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = on ? Br(acc) : Ink(20), Child = icon, HorizontalAlignment = HorizontalAlignment.Center };
        var label = Txt(title, 10, on ? th.Fg : th.Dim); label.HorizontalAlignment = HorizontalAlignment.Center;
        var st = new StackPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        circle.Margin = new Thickness(0, 0, 0, 4);
        st.Children.Add(circle); st.Children.Add(label);
        if (!on) { st.MouseEnter += (_, _) => circle.Background = Ink(41); st.MouseLeave += (_, _) => circle.Background = Ink(20); }
        st.MouseLeftButtonDown += (_, e) => e.Handled = true;
        st.MouseLeftButtonUp += (_, e) => { e.Handled = true; set(!on); Refresh(); };
        st.ToolTip = title + (on ? " — on" : " — off");
        return st;
    }

    /// The paw (tray icon, "Show cats"): four toes and a pad.
    public static FrameworkElement PawShape(Brush fill, double size)
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var (x, y, rx, ry) in new[] { (3.2, 6.2, 1.6, 2.1), (6.6, 3.2, 1.7, 2.3), (11.4, 3.2, 1.7, 2.3), (14.8, 6.2, 1.6, 2.1) })
            g.Children.Add(new EllipseGeometry(new Point(x, y), rx, ry));
        g.Children.Add(Geometry.Parse("M9 7.6 C6.2 7.6 3.6 11 3.9 13.1 C4.1 14.7 5.6 15.1 7 14.7 C8.1 14.4 8.5 14.3 9 14.3 C9.5 14.3 9.9 14.4 11 14.7 C12.4 15.1 13.9 14.7 14.1 13.1 C14.4 11 11.8 7.6 9 7.6 Z"));
        return new Path { Data = g, Fill = fill, Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    UIElement Footer()
    {
        var d = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var ver = Txt("Cataholic " + (v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}"), 10, th.Faint);
        DockPanel.SetDock(ver, Dock.Left); d.Children.Add(ver);
        var quit = Pill(new StackPanel { Orientation = Orientation.Horizontal, Children = { Glyph("\uE7E8", 11, Br(th.Dim)), Gap(5), Txt("Quit", 11, th.Dim) } },
            Colors.Transparent, Color.FromArgb(26, th.Ink.R, th.Ink.G, th.Ink.B), () => Application.Current.Shutdown());
        DockPanel.SetDock(quit, Dock.Right); d.Children.Add(quit);
        return new Border { BorderBrush = Ink(26), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 0, 0, 0), Child = d, Padding = new Thickness(0, 0, 0, 0) };
    }
}
