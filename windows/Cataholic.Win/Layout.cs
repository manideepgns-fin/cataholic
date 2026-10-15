using System;
using System.Windows;

namespace Cataholic;

// CatLayout — how big the cat's window is, and where the 46×62 character box sits inside it, for the resting cat and
// every mid-action pose. Mirrors CrewLayout.swift (minus the hover ring and the text overlays, which are not in this
// port). Points, origin top-left, y down. The character box is the closed panel (46×62); its feet stand at y ≈ 49.
// ZOOM (owner, 27 Sep 2026: "double the cat size"): everything in this file, and everything a view draws, is in 1×
// points. The view scales the finished drawing by `Zoom`; every place that turns layout into a window frame or a
// screen position (PanelRect, the panel, the movers) multiplies by it. On Windows 1 point = 1 DIP.
static class CatLayout
{
    public const double Zoom = 2;
    public const double CharW = 46, CharH = 62;
    /// The character box on screen (CharW × CharH at `Zoom`).
    public const double ScreenW = CharW * Zoom, ScreenH = CharH * Zoom;
    public const double Margin = 4;          // a pose's panel = its drawn bounds + 4 pt each side

    public readonly record struct Bounds(double X0, double Y0, double X1, double Y1)
    {
        public Bounds Union(Bounds o) => new(Math.Min(X0, o.X0), Math.Min(Y0, o.Y0), Math.Max(X1, o.X1), Math.Max(Y1, o.Y1));
        /// Flipped about the body's centre line (x = 23).
        public Bounds Mirrored => new(46 - X1, Y0, 46 - X0, Y1);
        /// The bounds rocked ±`deg` about the pin (23, 2), unioned with the original.
        public Bounds Rocked(double deg)
        {
            var r = this;
            foreach (var sign in new[] { -1.0, 1.0 })
            {
                double t = sign * deg * Math.PI / 180, c = Math.Cos(t), sn = Math.Sin(t);
                foreach (var (x, y) in new[] { (X0, Y0), (X1, Y0), (X0, Y1), (X1, Y1) })
                {
                    double px = 23 + (x - 23) * c - (y - 2) * sn, py = 2 + (x - 23) * sn + (y - 2) * c;
                    r = r.Union(new Bounds(px, py, px, py));
                }
            }
            return r;
        }
    }
    public readonly record struct Spec(double W, double H, double BoxX, double BoxY);
    public static readonly Spec Closed = new(CharW, CharH, 0, 0);

    /// What each pose draws, static and unrotated, in character-box points (measured on transparent pixels on the Mac).
    static Bounds Measured(CatModel.Action a) => a switch
    {
        CatModel.Action.Hi => new(-27.0, -21.0, 41.5, 50.5),      // bubble + tail, ears, wave paw
        CatModel.Action.Big => new(-11.5, -35.0, 58.0, 52.0),     // 1.95× (spring peak) about the feet
        CatModel.Action.Stretch => new(2.0, 8.5, 41.5, 50.0),     // side-on bow
        CatModel.Action.Jump => new(-0.5, 21.5, 42.0, 50.5),      // side-on leap
        CatModel.Action.Clingy => new(10.5, 0.0, 43.0, 50.0),     // leaning +8°
        CatModel.Action.Look => new(5.0, 0.0, 41.5, 50.5),        // head turned +3
        CatModel.Action.Nap => new(0.0, 0.0, 46.5, 50.5),         // sitting on the pin, or melted flat when standing
        _ => new(0.0, 0.0, 41.5, 50.5),                           // yawn, groom, spin, none
    };
    /// The 0.2 s take-off crouch before the leap (front-on, off the pin).
    static readonly Bounds Crouch = new(5.0, 9.0, 41.0, 50.0);

    /// The bounds the panel must hold: the static drawing plus its motion — the look turns ±3 pt, the lean is ±8°, the
    /// spin flips about the body, the jump lifts 6 pt, the whole cat rocks ±2.5° about the pin — ±5.5° while clingy rubs.
    public static Bounds Drawn(CatModel.Action a)
    {
        var b = Measured(a);
        switch (a)
        {
            case CatModel.Action.Look: b = b with { X0 = b.X0 - 6 }; break;
            case CatModel.Action.Clingy: case CatModel.Action.Spin: b = b.Union(b.Mirrored); break;
            case CatModel.Action.Jump:
                b = b with { Y0 = b.Y0 - 6 };                    // the leap lifts; the crouch is on the ground
                b = b.Union(Crouch);
                b = b.Union(b.Mirrored);                         // …and it leaps (crouches) left as well as right
                break;
        }
        return b.Rocked(a == CatModel.Action.Clingy ? 5.5 : 2.5);
    }

    /// The panel for whatever is drawn: the pose (+ 4 pt) — always at least the 46×62 character box, in whole points.
    public static Spec SpecFor(CatModel.Action a)
    {
        if (a == CatModel.Action.None) return Closed;
        var b = Drawn(a);
        double x0 = Math.Floor(Math.Min(b.X0 - Margin, 0)), y0 = Math.Floor(Math.Min(b.Y0 - Margin, 0));
        double x1 = Math.Ceiling(Math.Max(b.X1 + Margin, CharW)), y1 = Math.Ceiling(Math.Max(b.Y1 + Margin, CharH));
        return new Spec(x1 - x0, y1 - y0, -x0, -y0);
    }

    /// The panel rect, in SCREEN points, for a character box whose top-left is (charLeft, charTop): `s` is the 1×
    /// layout, scaled by Zoom here. A grown panel is shifted back inside `screen`; the resting panel is never clamped
    /// (the docked cat hangs off the edge on purpose). A `tucked` cat keeps that overhang while a pose grows its panel.
    public static Rect PanelRect(double charLeft, double charTop, Spec s, Rect? within, bool tucked = false)
    {
        var r = new Rect(charLeft - s.BoxX * Zoom, charTop - s.BoxY * Zoom, s.W * Zoom, s.H * Zoom);
        if (within is not Rect v || s == Closed) return r;
        double x = Math.Min(Math.Max(r.Left, v.Left), tucked ? r.Left : Math.Max(v.Left, v.Right - r.Width));
        double y = Math.Min(Math.Max(r.Top, v.Top), Math.Max(v.Top, v.Bottom - r.Height));
        return new Rect(x, y, r.Width, r.Height);
    }
}
