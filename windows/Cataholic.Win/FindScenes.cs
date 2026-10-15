using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Cataholic;

// Find my cat's three places (owner 13 Oct 2026: a kingdom, a harbour, a snowy village; the kingdom again at night) —
// the port of Sources/Cataholic/FindScenes.swift. Each is drawn in the cat's own sticker style — soft fills, one warm
// ink outline — as a fixed backdrop plus props (bushes, crates, boats, snowmen…) that sort in depth with the cats, so
// a cat behind a crate is really behind it. This file is the layout side (places, props, spots, what is blocked, the
// fairness rule); the pictures are FindArt.cs.

enum FindPlace { Kingdom, Harbour, Snow }

/// One piece of scenery a cat can stand behind: drawn in `Rect`, solid over `Cover`, standing on cover's bottom edge.
sealed class FindProp
{
    public enum Kinds { Parapet, Bush, Crate, Barrels, Hay, Counter, Well, Wall, Tree, Cart,
                        Boat, Net, Pots, FishCrate, Snowman, Sled, Drift, Woodpile, Pine }
    public readonly Kinds Kind;
    public readonly FRect Rect;
    public readonly FRect Cover;
    public readonly int Tint;
    public FindProp(Kinds kind, FRect rect, FRect cover, int tint = 0) { Kind = kind; Rect = rect; Cover = cover; Tint = tint; }

    public bool Hides => Kind != Kinds.Tree && Kind != Kinds.Pine;
    public double Baseline => Cover.MaxY;
    /// The highest point the prop actually draws over a cat behind it — goods on a counter, a boat's gunwale, a bush's
    /// round top — so "the face shows" is judged on what you see, not on the solid box.
    public double FaceTop => Kind switch
    {
        Kinds.Boat => Cover.MinY - 7,
        Kinds.Well => Cover.MinY - 4,
        Kinds.Cart => Cover.MinY - 2,
        Kinds.Counter => Cover.MinY - 12,
        _ => Math.Min(Rect.MinY, Cover.MinY),
    };
    /// May YOUR cat (or a lookalike) hide here? Not behind a snowman: its head and body sit right over a cat's face.
    public bool FairHide => Hides && Kind != Kinds.Snowman;
}

/// Buildings drawn in the backdrop: wall rect, roof height, wall / roof colours. Cats sit on their ridges, never in their walls.
sealed record FindHouse(FRect Wall, double Roof, Color WallC, Color RoofC);

static class FindScene
{
    public const double Width = 1100, Height = 640;
    public static Color Ink => Gfx.Ink;

    public static IReadOnlyList<FindProp> Props(FindPlace place) => place switch
    {
        FindPlace.Kingdom => KingdomProps, FindPlace.Harbour => HarbourProps, _ => SnowProps,
    };
    static FindProp P(FindProp.Kinds k, double x, double y, double w, double h, double top = 0, int tint = 0) =>
        new(k, new FRect(x, y, w, h), new FRect(x, y + top, w, h - top), tint);
    static FindProp Q(FindProp.Kinds k, FRect rect, FRect cover, int tint = 0) => new(k, rect, cover, tint);
    const FindProp.Kinds Parapet = FindProp.Kinds.Parapet, Bush = FindProp.Kinds.Bush, Crate = FindProp.Kinds.Crate, Barrels = FindProp.Kinds.Barrels,
        Hay = FindProp.Kinds.Hay, Counter = FindProp.Kinds.Counter, Well = FindProp.Kinds.Well, Wall = FindProp.Kinds.Wall, Tree = FindProp.Kinds.Tree,
        Cart = FindProp.Kinds.Cart, Boat = FindProp.Kinds.Boat, Net = FindProp.Kinds.Net, Pots = FindProp.Kinds.Pots, FishCrate = FindProp.Kinds.FishCrate,
        Snowman = FindProp.Kinds.Snowman, Sled = FindProp.Kinds.Sled, Drift = FindProp.Kinds.Drift, Woodpile = FindProp.Kinds.Woodpile, Pine = FindProp.Kinds.Pine;

    public static readonly FindProp[] KingdomProps =
    {
        P(Parapet, 428, 166, 244, 36, top: 18),
        Q(Tree, new FRect(318, 318, 92, 122), new FRect(352, 420, 22, 20)),
        P(Bush, 690, 368, 86, 42, top: 4),
        P(Counter, 766, 426, 94, 44, top: 12, tint: 0), P(Counter, 878, 426, 94, 44, top: 12, tint: 1), P(Counter, 990, 426, 94, 44, top: 12, tint: 2),
        P(Hay, 380, 452, 72, 40),
        P(Wall, 26, 560, 168, 30, top: 4),
        P(Crate, 214, 568, 54, 50),
        P(Bush, 300, 586, 112, 46, top: 4),
        Q(Well, new FRect(650, 486, 74, 92), new FRect(652, 538, 70, 40)),
        P(Barrels, 790, 540, 74, 46),
        P(Crate, 878, 556, 58, 52),
        P(Bush, 960, 572, 116, 48, top: 4),
        Q(Cart, new FRect(470, 548, 96, 64), new FRect(470, 560, 96, 40)),
        P(Bush, 20, 452, 74, 38, top: 4),
    };
    public static readonly FindProp[] HarbourProps =
    {
        Q(Boat, new FRect(430, 296, 132, 118), new FRect(430, 368, 132, 44), 0),
        Q(Boat, new FRect(606, 392, 146, 54), new FRect(606, 398, 146, 46), 1),
        Q(Boat, new FRect(830, 300, 108, 98), new FRect(830, 360, 108, 38), 2),
        P(Counter, 562, 494, 100, 46, top: 12, tint: 3), P(Counter, 694, 494, 100, 46, top: 12, tint: 4),
        P(Barrels, 40, 470, 74, 46),
        P(FishCrate, 170, 556, 70, 46),
        P(Net, 268, 590, 128, 42),
        P(Pots, 836, 570, 96, 46),
        P(Crate, 980, 520, 58, 52),
        P(Crate, 448, 572, 54, 48),
        P(FishCrate, 940, 600, 76, 36),
    };
    public static readonly FindProp[] SnowProps =
    {
        Q(Pine, new FRect(312, 300, 76, 128), new FRect(342, 410, 16, 18)),
        Q(Pine, new FRect(1006, 290, 80, 134), new FRect(1038, 406, 16, 18)),
        Q(Pine, new FRect(690, 296, 64, 110), new FRect(714, 390, 14, 16)),
        Q(Snowman, new FRect(150, 510, 58, 92), new FRect(150, 552, 58, 50)),
        Q(Snowman, new FRect(760, 408, 52, 82), new FRect(760, 446, 52, 44)),
        Q(Snowman, new FRect(970, 540, 60, 94), new FRect(970, 584, 60, 50)),
        P(Sled, 380, 592, 100, 36), P(Sled, 862, 476, 94, 34, tint: 1),
        P(Drift, 28, 596, 126, 38), P(Drift, 600, 594, 136, 40), P(Drift, 1000, 438, 92, 34), P(Drift, 470, 410, 110, 34),
        P(Woodpile, 232, 470, 94, 44),
    };

    static Color K(double r, double g, double b) => Gfx.C(r, g, b);
    public static readonly FindHouse[] Houses =
    {
        new(new FRect(30, 392, 120, 60), 44, K(0.99, 0.93, 0.80), K(0.84, 0.45, 0.36)),
        new(new FRect(176, 402, 110, 56), 40, K(0.98, 0.86, 0.84), K(0.46, 0.58, 0.78)),
        new(new FRect(50, 484, 112, 62), 42, K(0.86, 0.92, 0.96), K(0.50, 0.68, 0.46)),
        new(new FRect(196, 496, 120, 60), 44, K(0.99, 0.93, 0.80), K(0.86, 0.62, 0.32)),
    };
    public static readonly FindHouse[] Shacks =
    {
        new(new FRect(26, 372, 176, 74), 40, K(0.62, 0.76, 0.86), K(0.84, 0.45, 0.36)),
    };
    public static readonly FindHouse[] Cabins =
    {
        new(new FRect(40, 384, 132, 64), 42, K(0.66, 0.46, 0.33), Colors.White),
        new(new FRect(196, 364, 112, 58), 38, K(0.74, 0.52, 0.36), Colors.White),
        new(new FRect(812, 360, 122, 62), 40, K(0.70, 0.48, 0.34), Colors.White),
        new(new FRect(52, 470, 118, 62), 40, K(0.74, 0.52, 0.36), Colors.White),
    };
    public static FindHouse[] Buildings(FindPlace p) => p switch { FindPlace.Kingdom => Houses, FindPlace.Harbour => Shacks, _ => Cabins };

    /// Places in the open: rooftops, and the open ground of each place (the bridge, the pier, the frozen pond…).
    public static (double X, double Y, double Scale, bool Perch) OpenSpot(FindPlace place, FindRandom rng)
    {
        double r = rng.Double(0, 1);
        if (r < 0.14)                                                      // a rooftop ridge
        {
            var h = rng.Pick(Buildings(place));
            double ridge = h.Wall.W * 0.36;
            return (h.Wall.MidX + rng.DoubleClosed(-ridge / 2, ridge / 2), h.Wall.MinY - h.Roof + 3, 0.92, true);
        }
        // (`CGPoint(x: .random(in: 24...1076), …)` picks CGPoint's Int initialiser on the Mac: whole-number spots, an Int draw)
        switch (place)
        {
            case FindPlace.Kingdom:
                if (r < 0.2) return (rng.IntClosed(528, 572), rng.IntClosed(322, 344), 0.95, false);   // the bridge
                return (rng.IntClosed(24, 1076), rng.IntClosed(372, 632), 1, false);
            case FindPlace.Harbour:
                if (r < 0.26) return (rng.IntClosed(318, 384), rng.IntClosed(330, 452), 0.92, false);  // the pier
                if (r < 0.34) return (rng.IntClosed(220, 250), rng.IntClosed(420, 452), 0.95, false);  // the quay
                return (rng.IntClosed(24, 1076), rng.IntClosed(490, 632), 1, false);                   // the dock
            default:
                return (rng.IntClosed(24, 1076), rng.IntClosed(384, 632), 1, false);
        }
    }

    /// Would this cat land somewhere silly — in the water, inside a wall, or half-buried in a prop it isn't behind?
    public static bool Blocked(FindPlace place, FRect r, FRect? behind)
    {
        foreach (var p in Props(place))
        {
            if (behind is FRect b && p.Cover == b) continue;                     // `where p.cover != behind`
            if (!p.Hides && r.MaxY > p.Baseline + 2) continue;                  // sitting in front of a tree is fine
            if (p.Rect.InsetBy(-3, -3).Intersects(r)) return true;
        }
        foreach (var h in Buildings(place))
            if (h.Wall.InsetBy(4, 4).Intersects(r) && r.MaxY < h.Wall.MaxY + 3) return true;   // in front of a wall is fine
        switch (place)
        {
            case FindPlace.Kingdom:
            {
                var moat = new FRect(0, 306, 1100, 46);
                if (moat.Intersects(r) && !(r.MinX > 516 && r.MaxX < 584)) return true;
                if (new FRect(760, 376, 330, 66).Intersects(r)) return true;       // the stalls' awnings and cloths
                if (r.MinY < 352 && !(r.MinX > 516 && r.MaxX < 584) && r.MaxY > 300) return true;
                break;
            }
            case FindPlace.Harbour:
            {
                if (new FRect(554, 446, 248, 52).Intersects(r)) return true;       // the fish stalls' awnings
                var water = new FRect(0, 240, 1100, 222);
                var pier = new FRect(306, 300, 88, 162); var quay = new FRect(0, 330, 262, 132);
                if (water.Intersects(r) && !pier.Contains(r) && !quay.Contains(r) && behind == null) return true;
                break;
            }
            default:
                if (r.MinY < 340) return true;
                break;
        }
        return false;
    }
    public static bool Inside(FRect r) => new FRect(0, 0, Width, Height).InsetBy(6, 6).Contains(r);

    public readonly record struct Item(bool IsProp, int Index);
    /// Props and cats sorted by the line they stand on (a cat behind a prop stands a little above its bottom edge).
    /// A stable sort, so props that share a baseline keep their order.
    public static List<Item> DepthOrder(FindPlace place, IReadOnlyList<FindCat> cats)
    {
        var all = new List<(double, Item)>();
        var props = Props(place);
        for (int i = 0; i < props.Count; i++) all.Add((props[i].Baseline, new Item(true, i)));
        foreach (var c in cats) all.Add((c.Feet.Y, new Item(false, c.Id)));
        return all.OrderBy(a => a.Item1).Select(a => a.Item2).ToList();
    }
}

// MARK: — fair hiding (owner 13 Oct 2026, playing as Snow: the whole cat sat behind a snowman — "i cant even see its
// ears or toes". Blending into the scene is fine; being covered is not.)

/// Every placement of YOUR cat (and of its lookalikes) must be findable: the face and ears show above whatever it hides
/// behind, enough of the body shows, and it never hides behind a snowman. A white cat in white snow is fair game.
static class FindFair
{
    /// Box y (1×, the 46×62 character box) below which the eyes, nose and mouth all sit.
    public const double FaceLine = 28;

    /// Does the face clear whatever the cat hides behind?
    public static bool FaceShows(FindCat cat)
    {
        if (cat.Top is not double top) return true;
        return top >= cat.Feet.Y + (FaceLine - 49) * cat.Scale;
    }
    /// How much of the cat's height shows.
    public static double Shown(FindCat cat) => cat.Visible.H / Math.Max(1, cat.Rect.H);

    /// The whole rule.
    public static bool Fair(FindCat cat, FindPlace place, double minShow) =>
        FaceShows(cat) && Shown(cat) >= minShow - 0.001
        && !FindScene.Props(place).Any(p => p.Kind == FindProp.Kinds.Snowman && cat.Cover is FRect c && p.Cover == c);
}
