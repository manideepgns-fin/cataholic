using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// PLACEHOLDER until the drawing lane lands: a fur-coloured loaf in the character box.
// The real port of CrewCharacter.swift replaces this whole file, keeping this signature.
public static class CatDrawing
{
    /// Draws the cat for `p` in character-box points (46 × 62, origin top-left, y down; feet at y ≈ 49).
    /// The caller sets any scale (zoom 2), mirroring and offset on `dc` before calling.
    public static void Draw(DrawingContext dc, CatPose p)
    {
        var coat = p.CoatOverride ?? CatCoat.Current;
        dc.DrawEllipse(new SolidColorBrush(coat.Fur), new Pen(new SolidColorBrush(coat.Line), 0.6), new Point(23, 38), 13, 11);
        dc.DrawEllipse(new SolidColorBrush(coat.Fur), new Pen(new SolidColorBrush(coat.Line), 0.6), new Point(23, 21), 16, 11);
    }
}
