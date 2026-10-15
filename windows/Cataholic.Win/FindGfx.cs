using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// Find my cat — the drawing kit. The Mac draws the game with SwiftUI's Canvas / GraphicsContext; this is the same
// vocabulary over a WPF DrawingContext, so FindArt.cs can follow FindScenes.swift line by line: Inked / Blobs / Line
// are the Swift helpers of the same names, FPath is a Path (move / line / quad / cubic / close), Linear / Radial are
// the gradients. Brushes and pens are frozen and cached: the scene is drawn once per layout, and again at every zoom
// repaint WPF does on its own, so nothing here may allocate per frame.

/// A rectangle with CGRect's rules (x, y, width, height; y down) — the layout code needs CGRect's `intersects` /
/// `contains` / `insetBy`, which are not WPF's (a negative width throws there, and edges count differently).
readonly record struct FRect(double X, double Y, double W, double H)
{
    public double MinX => X; public double MaxX => X + W; public double MinY => Y; public double MaxY => Y + H;
    public double MidX => X + W / 2; public double MidY => Y + H / 2;
    public FRect InsetBy(double dx, double dy) => new(X + dx, Y + dy, W - 2 * dx, H - 2 * dy);
    public FRect OffsetBy(double dx, double dy) => new(X + dx, Y + dy, W, H);
    /// CGRect.intersects: rectangles that only share an edge do not intersect.
    public bool Intersects(FRect o) => MinX < o.MaxX && o.MinX < MaxX && MinY < o.MaxY && o.MinY < MaxY;
    /// CGRect.contains(CGPoint): the min edges are inside, the max edges are not.
    public bool Contains(double px, double py) => px >= MinX && px < MaxX && py >= MinY && py < MaxY;
    public bool Contains(Point p) => Contains(p.X, p.Y);
    /// CGRect.contains(CGRect): inclusive.
    public bool Contains(FRect o) => o.MinX >= MinX && o.MaxX <= MaxX && o.MinY >= MinY && o.MaxY <= MaxY;
    public Rect ToRect() => new(X, Y, Math.Max(0, W), Math.Max(0, H));
}

/// A Swift `Path`: moveTo / addLine / addQuadCurve(to:control:) / addCurve / closeSubpath, built into a frozen geometry.
sealed class FPath
{
    readonly List<(char Op, double A, double B, double C, double D, double E, double F)> ops = new();
    public FPath M(double x, double y) { ops.Add(('M', x, y, 0, 0, 0, 0)); return this; }
    public FPath L(double x, double y) { ops.Add(('L', x, y, 0, 0, 0, 0)); return this; }
    /// addQuadCurve(to: (x, y), control: (cx, cy)) — the order of the Swift call.
    public FPath Q(double x, double y, double cx, double cy) { ops.Add(('Q', x, y, cx, cy, 0, 0)); return this; }
    /// addCurve(to: (x, y), control1: (c1x, c1y), control2: (c2x, c2y)).
    public FPath C(double x, double y, double c1x, double c1y, double c2x, double c2y) { ops.Add(('C', x, y, c1x, c1y, c2x, c2y)); return this; }
    public FPath Z() { ops.Add(('Z', 0, 0, 0, 0, 0, 0)); return this; }
    public FPath Lines(params Point[] pts)
    {
        for (int i = 0; i < pts.Length; i++) { if (i == 0) M(pts[i].X, pts[i].Y); else L(pts[i].X, pts[i].Y); }
        return this;
    }

    public Geometry Build()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            bool open = false;
            for (int i = 0; i < ops.Count; i++)
            {
                var (op, a, b, cc, d, e, f) = ops[i];
                switch (op)
                {
                    case 'M':
                    {
                        bool closed = false;                              // look ahead: does this figure end in Z?
                        for (int j = i + 1; j < ops.Count && ops[j].Op != 'M'; j++) if (ops[j].Op == 'Z') closed = true;
                        c.BeginFigure(new Point(a, b), true, closed); open = true; break;
                    }
                    case 'L': if (open) c.LineTo(new Point(a, b), true, true); break;
                    case 'Q': if (open) c.QuadraticBezierTo(new Point(cc, d), new Point(a, b), true, true); break;
                    case 'C': if (open) c.BezierTo(new Point(cc, d), new Point(e, f), new Point(a, b), true, true); break;
                    case 'Z': open = false; break;
                }
            }
        }
        g.Freeze();
        return g;
    }
    public static implicit operator Geometry(FPath p) => p.Build();
}

/// SwiftUI `GraphicsContext`, in the few verbs the game uses.
sealed class Gfx
{
    public readonly DrawingContext Dc;
    public Gfx(DrawingContext dc) { Dc = dc; }

    // ── colours ──
    /// Color(red:green:blue:) — components 0…1.
    public static Color C(double r, double g, double b) => CatCoat.C(r, g, b);
    /// `.opacity(a)` — multiplies whatever alpha the colour has.
    public static Color Op(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Max(0, Math.Min(1, c.A / 255.0 * a)) * 255), c.R, c.G, c.B);
    public static readonly Color White = Colors.White;
    /// Same colour, fully transparent — SwiftUI's `.clear` as a gradient end (it blends premultiplied; WPF does not).
    public static Color Clear(Color c) => Color.FromArgb(0, c.R, c.G, c.B);
    /// The warm ink of the cat's sticker outline (FindScene.ink).
    public static readonly Color Ink = C(0.33, 0.25, 0.23);

    // ── caches ──
    static readonly Dictionary<Color, Brush> brushes = new();
    public static Brush B(Color c)
    {
        if (brushes.TryGetValue(c, out var b)) return b;
        var s = new SolidColorBrush(c); s.Freeze();
        return brushes[c] = s;
    }
    static readonly Dictionary<(Color, double, PenLineCap, PenLineJoin), Pen> pens = new();
    public static Pen P(Color c, double w, PenLineCap cap = PenLineCap.Flat, PenLineJoin join = PenLineJoin.Miter)
    {
        var k = (c, w, cap, join);
        if (pens.TryGetValue(k, out var p)) return p;
        p = new Pen(B(c), w) { StartLineCap = cap, EndLineCap = cap, LineJoin = join };
        p.Freeze();
        return pens[k] = p;
    }

    // ── shapes (frozen, in scene points) ──
    public static Point Pt(double x, double y) => new(x, y);
    public static Geometry Ell(FRect r) => Ell(r.X, r.Y, r.W, r.H);
    public static Geometry Ell(double x, double y, double w, double h)
    {
        var g = new EllipseGeometry(new Rect(x, y, Math.Max(0, w), Math.Max(0, h))); g.Freeze(); return g;
    }
    public static Geometry Box(FRect r) => Box(r.X, r.Y, r.W, r.H);
    public static Geometry Box(double x, double y, double w, double h)
    {
        var g = new RectangleGeometry(new Rect(x, y, Math.Max(0, w), Math.Max(0, h))); g.Freeze(); return g;
    }
    /// Path(roundedRect:cornerRadius:) — circular corners.
    public static Geometry RBox(FRect r, double radius) => RBox(r.X, r.Y, r.W, r.H, radius);
    public static Geometry RBox(double x, double y, double w, double h, double radius)
    {
        var g = new RectangleGeometry(new Rect(x, y, Math.Max(0, w), Math.Max(0, h)), radius, radius); g.Freeze(); return g;
    }

    // ── verbs ──
    public void Fill(Geometry g, Color c) { if (c.A > 0) Dc.DrawGeometry(B(c), null, g); }
    public void Fill(Geometry g, Brush b) => Dc.DrawGeometry(b, null, g);
    /// ctx.stroke(path, with: .color(c), lineWidth: w) — butt caps, mitre joins unless `cap` / `join` say otherwise
    /// (StrokeStyle(lineCap:lineJoin:)).
    public void Stroke(Geometry g, Color c, double w, PenLineCap cap = PenLineCap.Flat, PenLineJoin join = PenLineJoin.Miter) =>
        Dc.DrawGeometry(null, P(c, w, cap, join), g);

    /// Fill a shape and give it the cat's sticker outline.
    public void Inked(Geometry g, Color c, double w = 2.2, double alpha = 1)
    {
        Fill(g, c);
        Stroke(g, Op(Ink, alpha), w, PenLineCap.Round, PenLineJoin.Round);
    }
    /// Several blobs that read as one (outline all, then fill all, so the inner lines vanish).
    public void Blobs(FRect[] rs, Color c, double w = 2.2, double alpha = 1)
    {
        foreach (var r in rs) Stroke(Ell(r), Op(Ink, alpha), w * 2);
        foreach (var r in rs) Fill(Ell(r), c);
    }
    /// A polyline with round caps and joins.
    public void Line(Point[] pts, Color c, double w) => Stroke(new FPath().Lines(pts), c, w, PenLineCap.Round, PenLineJoin.Round);

    /// ctx.drawLayer { l in l.clip(to: shape); … }: everything `body` draws is clipped to `clip`.
    public void Clip(Geometry clip, Action body) { Dc.PushClip(clip); body(); Dc.Pop(); }
    /// A copy of the context moved / scaled / rotated (`var c = ctx; c.translateBy…`): `body` draws inside it.
    public void With(Transform t, Action body) { Dc.PushTransform(t); body(); Dc.Pop(); }

    /// ctx.draw(Text(s).font(.system(size:, weight:, design: .rounded)), at: (cx, cy), anchor: .center)
    public void TextCentered(string s, double size, FontWeight weight, Color c, double cx, double cy)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, B(c), 1.0);
        Dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }

    // ── gradients (absolute coordinates, like SwiftUI's startPoint / center in the canvas) ──
    /// .linearGradient(Gradient(colors:), startPoint:, endPoint:) — colours evenly spaced.
    public static Brush Linear(Point from, Point to, params Color[] colors)
    {
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = from, EndPoint = to };
        for (int i = 0; i < colors.Length; i++) b.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
        b.Freeze();
        return b;
    }
    /// .radialGradient(Gradient(stops:), center:, startRadius: 0, endRadius: r) — padded beyond the end.
    public static Brush Radial(Point center, double r, params (double At, Color Color)[] stops)
    {
        var b = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, Center = center, GradientOrigin = center, RadiusX = r, RadiusY = r,
            SpreadMethod = GradientSpreadMethod.Pad,
        };
        foreach (var (at, col) in stops) b.GradientStops.Add(new GradientStop(col, at));
        b.Freeze();
        return b;
    }
}
