using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Cataholic;

/// The eight coats — the same values as CatCoat in Sources/Cataholic/CrewCharacter.swift.
/// `Stripe` transparent hides the tabby markings; `Patches` paints calico spots (clipped to the head and body);
/// `Glasses` puts the round reading glasses on. `NapPatches` = the same coat laid flat for the melted nap.
public sealed class CatCoat
{
    public string Id { get; }
    public string Name { get; }
    public Color Fur { get; }
    public Color Stripe { get; }
    public Color Shade { get; }
    public Color Cream { get; }
    public Color Line { get; }
    public Color Eye { get; }
    public List<(Color Color, Rect Rect)> Patches { get; init; } = new();
    public bool Glasses { get; init; }
    public Color? EarL { get; init; }
    public Color? EarR { get; init; }
    public List<(Color Color, Rect Rect)> NapPatches { get; init; } = new();

    public CatCoat(string id, string name, Color fur, Color stripe, Color shade, Color cream, Color line, Color eye)
    {
        Id = id; Name = name; Fur = fur; Stripe = stripe; Shade = shade; Cream = cream; Line = line; Eye = eye;
    }

    /// Swift's Color(red:green:blue:) — components 0…1.
    public static Color C(double r, double g, double b) =>
        Color.FromRgb((byte)System.Math.Round(r * 255), (byte)System.Math.Round(g * 255), (byte)System.Math.Round(b * 255));

    static readonly Color Orange = C(0.95, 0.62, 0.30), Dark = C(0.24, 0.20, 0.19);

    public static readonly CatCoat Ginger = new("ginger", "Ginger", C(0.97, 0.70, 0.40), C(0.91, 0.53, 0.23),
        C(0.90, 0.58, 0.30), C(1.0, 0.96, 0.90), C(0.54, 0.29, 0.13), C(0.35, 0.20, 0.13)) { Glasses = true };
    public static readonly CatCoat Smokey = new("smokey", "Smokey", C(0.70, 0.71, 0.75), C(0.49, 0.50, 0.56),
        C(0.60, 0.61, 0.66), C(0.96, 0.95, 0.94), C(0.27, 0.27, 0.32), C(0.20, 0.20, 0.24));
    public static readonly CatCoat PatchesCoat = new("patches", "Patches", C(1.0, 0.97, 0.93), Colors.Transparent,
        C(0.90, 0.87, 0.83), C(1.0, 0.99, 0.97), C(0.36, 0.27, 0.22), C(0.25, 0.18, 0.15))
    {
        Patches = new()
        {
            (Orange, new Rect(6, 8, 16, 14)),     // orange over the left ear
            (Dark, new Rect(27, 9, 12, 10)),      // dark over the right ear
            (Orange, new Rect(24, 29, 11, 8)),    // orange on the shoulder
            (Dark, new Rect(12, 30, 7, 7)),       // dark on the chest side
        },
        EarL = Orange, EarR = Dark,
        NapPatches = new()
        {
            (Orange, new Rect(1, 25.5, 10, 9)),   // orange over the left ear
            (Dark, new Rect(13, 25.5, 8, 7)),     // dark over the right ear
            (Orange, new Rect(20, 36, 13, 8)),    // orange saddle on the back
            (Dark, new Rect(32, 38, 8, 6)),       // dark on the rump
        },
    };
    public static readonly CatCoat Tux = new("tux", "Tux", C(0.20, 0.20, 0.23), Colors.Transparent,
        C(0.14, 0.14, 0.16), C(0.98, 0.98, 0.97), C(0.08, 0.08, 0.10), C(0.98, 0.84, 0.35));
    public static readonly CatCoat Butter = new("butter", "Butter", C(0.99, 0.86, 0.62), C(0.93, 0.72, 0.43),
        C(0.93, 0.78, 0.54), C(1.0, 0.98, 0.93), C(0.55, 0.38, 0.20), C(0.36, 0.24, 0.14));
    public static readonly CatCoat Cocoa = new("cocoa", "Cocoa", C(0.55, 0.37, 0.26), C(0.38, 0.24, 0.16),
        C(0.46, 0.30, 0.21), C(0.96, 0.90, 0.82), C(0.22, 0.13, 0.08), C(0.16, 0.10, 0.07));
    public static readonly CatCoat Snow = new("snow", "Snow", C(0.98, 0.98, 0.99), Colors.Transparent,
        C(0.88, 0.89, 0.92), C(1.0, 1.0, 1.0), C(0.40, 0.42, 0.50), C(0.22, 0.35, 0.62));
    public static readonly CatCoat Lilac = new("lilac", "Lilac", C(0.78, 0.74, 0.84), C(0.64, 0.59, 0.72),
        C(0.70, 0.66, 0.77), C(0.98, 0.96, 0.99), C(0.36, 0.31, 0.44), C(0.30, 0.24, 0.38));

    public static readonly IReadOnlyList<CatCoat> All = new[] { Ginger, Smokey, PatchesCoat, Tux, Butter, Cocoa, Snow, Lilac };

    /// The picked coat. The app sets it from saved prefs at start and when the menu picks one; drawing reads it.
    public static CatCoat Current { get; set; } = Ginger;
    public static CatCoat ById(string? id) => All.FirstOrDefault(c => c.Id == id) ?? Ginger;
}
