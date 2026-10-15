using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// Find my cat (owner 13 Oct 2026: "double click takes you to find my cat page, where we have a kingdom, and user will
// have three guesses to find where its cat is hidden — find my Waldo?", then "only one feature, simple", then — after
// playing it — "nice actually but seems easy"). Double-click the cat → five levels across three places, harder each time:
//   1. The kingdom by day — ~30 cats in the other seven coats; yours tucked behind something.
//   2. The kingdom by day — plus 4 lookalikes in YOUR coat, each with one detail wrong (collar, bell, glasses, ears,
//      tail tip, a spot). The header shows the real one.
//   3. The harbour — boats, nets, a fish market, a lighthouse; 5–6 lookalikes; yours shows only its ears.
//   4. The snowy village — pale coats blend into the snow; ~40 cats, 6 lookalikes.
//   5. The kingdom at night — dark but for lit windows and lanterns; your pointer is a lantern.
// Then endless: a random place at level-5 difficulty. Three guesses a round: a miss leaves a paw print and says warmer /
// colder (or, on a lookalike, what gave it away); a hit pops your cat up waving and meows → next level; three misses
// spotlight where it was → the same level again, new layout. The level is remembered (`findLevel`).
// Nothing animates while you look: the found-it wave runs a clock for two seconds, and the night lantern redraws only
// when the pointer moves over the scene. Closing the window frees everything.
//
// This file is the round itself (FindMyCat.swift: FindOdd … FindGame, FindRandom, FindCheck) — no WPF window code, so
// the layout can be dealt and checked without a screen. The window, camera and pictures are FindView.cs / FindArt.cs.

/// What a lookalike got wrong — one detail, chosen so it reads at the game's size.
enum FindOdd
{
    Collar, Bell, Glasses, Ears, Tail, Spot,
}

static class FindOddInfo
{
    public static readonly FindOdd[] All = { FindOdd.Collar, FindOdd.Bell, FindOdd.Glasses, FindOdd.Ears, FindOdd.Tail, FindOdd.Spot };
    public static string Hint(this FindOdd o) => o switch
    {
        FindOdd.Collar => "Not her — look at the collar!",
        FindOdd.Bell => "Not her — where's the bell?",
        FindOdd.Glasses => "Not her — check the glasses!",
        FindOdd.Ears => "Not her — look at the ears!",
        FindOdd.Tail => "Not her — look at the tail!",
        _ => "Not her — that spot isn't hers!",
    };
    /// Can you still see it on a cat peeking over something?
    public static bool ShowsFromAbove(this FindOdd o) => o == FindOdd.Glasses || o == FindOdd.Ears || o == FindOdd.Spot;
    public static string Name(this FindOdd o) => o.ToString().ToLowerInvariant();
}

/// One cat in the scene. `Feet` is where it sits (scene points); `Scale` × the 46×62 character box.
sealed class FindCat
{
    public int Id;
    public CatCoat Coat = CatCoat.Ginger;
    public CatState State;
    public Point Feet;
    public double Scale;
    public bool Flip;
    public FRect? Cover;             // the prop it peeks over, if any
    public double? Top;              // …and the highest point that prop draws over it (FindProp.FaceTop)
    public FindOdd? Odd;             // a lookalike: the one detail that's wrong

    /// The drawn cat, unhidden, in scene points (each pose's own outline inside the 46×62 box).
    public FRect Rect
    {
        get
        {
            FRect b = State switch
            {
                CatState.Sleeping => new FRect(0, 28, 46, 22.5),
                CatState.Nap => new FRect(2, 24.5, 42, 25.5),
                CatState.Running => new FRect(0, 6, 46, 44),
                _ => new FRect(0, 6, 43, 45),
            };
            double x0 = Flip ? 46 - b.MaxX : b.MinX;
            return new FRect(Feet.X + (x0 - 23) * Scale, Feet.Y + (b.MinY - 49) * Scale, b.W * Scale, b.H * Scale);
        }
    }
    /// The part you can actually see (above whatever it hides behind).
    public FRect Visible
    {
        get
        {
            var r = Rect;
            double? t = Top ?? Cover?.MinY;
            return t is double tt ? r with { H = Math.Max(6, tt - r.MinY) } : r;
        }
    }
    public FRect Box => new(Feet.X - 23 * Scale, Feet.Y - 49 * Scale, 46 * Scale, 62 * Scale);
}

sealed record FindGuess(int Id, Point At, string Hint);

/// How hard a level is: where, how many cats, how many lookalikes, how much of yours shows.
sealed class FindRound
{
    public int Level;                // 1…5, 6 = endless
    public FindPlace Place;
    public bool Night;
    public int Decoys;
    public int Lookalikes;
    public (double Lo, double Hi) Show;   // how much of your cat's height peeks out
    public double Mine;              // your cat's scale
    public int Peekers = 8;          // decoys tucked behind props, beside yours (more = your hiding spot stands out less)

    public static FindRound Make(int level, FindRandom rng)
    {
        switch (level)
        {
            // `show` never goes below the face line (FindFair): the face always shows, ~50 % of the body at 1–2, ~35 %+ after.
            case 1: return new FindRound { Level = 1, Place = FindPlace.Kingdom, Night = false, Decoys = 30, Lookalikes = 0, Show = (0.55, 0.66), Mine = 0.6 };
            case 2: return new FindRound { Level = 2, Place = FindPlace.Kingdom, Night = false, Decoys = 32, Lookalikes = 4, Show = (0.52, 0.62), Mine = 0.58 };
            case 3: return new FindRound { Level = 3, Place = FindPlace.Harbour, Night = false, Decoys = 38, Lookalikes = rng.IntClosed(5, 6), Show = (0.5, 0.58), Mine = 0.52 };
            case 4: return new FindRound { Level = 4, Place = FindPlace.Snow, Night = false, Decoys = 44, Lookalikes = 6, Show = (0.5, 0.58), Mine = 0.52 };
            // 5 and endless (owner 13 Oct 2026: "too open"): a smaller cat, more cats, every prop hides a cat or two, half the
            // lookalikes peek over props too, and a smaller lantern. The face still always shows.
            case 5: return new FindRound { Level = 5, Place = FindPlace.Kingdom, Night = true, Decoys = 52, Lookalikes = 8, Show = (0.5, 0.56), Mine = 0.46, Peekers = 30 };
            default:
            {
                var p = rng.Pick(new[] { FindPlace.Kingdom, FindPlace.Harbour, FindPlace.Snow });
                return new FindRound { Level = 6, Place = p, Night = p == FindPlace.Kingdom, Decoys = 54, Lookalikes = 8, Show = (0.5, 0.55), Mine = 0.46, Peekers = 30 };
            }
        }
    }
}

sealed class FindGame
{
    public enum Phases { Playing, Found, Lost }

    public List<FindCat> Cats = new();
    public int Yours;
    public List<FindGuess> Guesses = new();
    public Phases Phase = Phases.Playing;
    public FindRound Round;
    public bool Celebrating;                  // the found-it wave + sparkles run a clock only while this is on
    public DateTime FoundAt = DateTime.MinValue;
    public bool Unfair;                       // no fair spot was found for your cat (the check asserts this stays false)
    /// Something on the board changed (a deal, a guess, the end of the wave): the view redraws what it shows.
    public event Action? Changed;

    public static int SavedLevel
    {
        get => Math.Min(6, Math.Max(1, Prefs.Int("findLevel")));
        set => Prefs.Set("findLevel", (double)value);
    }
    /// The furthest level reached (owner 13 Oct 2026: "can people change levels?") — the picker unlocks up to here.
    /// Older builds only kept `findLevel`, which was then always the furthest.
    public static int MaxLevel
    {
        get => Math.Min(6, Math.Max(1, Prefs.Has("findMaxLevel") ? Prefs.Int("findMaxLevel") : Prefs.Int("findLevel")));
        set => Prefs.Set("findMaxLevel", (double)Math.Min(6, Math.Max(MaxLevel, value)));   // never lowers
    }
    public static string LevelName(int l) => l switch
    {
        1 => "1 — Kingdom",
        2 => "2 — Kingdom lookalikes",
        3 => "3 — Harbour",
        4 => "4 — Snowy village",
        5 => "5 — Kingdom at night",
        _ => "Endless — anywhere",
    };
    static ulong NewSeed() => (ulong)Random.Shared.NextInt64(1, long.MaxValue);

    /// Start a fresh round on a level picked from the header (only levels already reached).
    public void Play(int level, ulong? seed = null)
    {
        level = Math.Min(MaxLevel, Math.Max(1, level));
        SavedLevel = level;
        var rng = new FindRandom(seed ?? NewSeed());
        Round = FindRound.Make(level, rng);
        Deal(rng);
        Changed?.Invoke();
    }

    readonly CatCoat? coat;                   // the self-check deals for every coat without touching your pick

    public FindGame(int? level = null, ulong? seed = null, CatCoat? coat = null)
    {
        this.coat = coat;
        var rng = new FindRandom(seed ?? NewSeed());
        Round = FindRound.Make(level ?? SavedLevel, rng);
        Deal(rng);
    }

    public FindCat Mine => Cats[Yours];
    public int GuessesLeft => 3 - Guesses.Count;
    /// What counts as a hit: the part of your cat you can see, plus a little slack.
    public FRect Target => Mine.Visible.InsetBy(-9, -9);
    public Point Center { get { var r = Target; return new Point(r.MidX, r.MidY); } }

    public void Guess(Point p)
    {
        if (Phase != Phases.Playing) return;
        if (Target.Contains(p))
        {
            Phase = Phases.Found;
            FoundAt = DateTime.Now;
            if (!CatPrefs.Quiet) CatSound.Meow();           // the one sound: a real click on your cat
            if (!Fx.Reduced)
            {
                Celebrating = true;
                Later.After(FindCelebrationInfo.Duration, () => { Celebrating = false; Changed?.Invoke(); });
            }
            Changed?.Invoke();
            return;
        }
        string hint;
        var twin = Cats.FirstOrDefault(c => c.Odd != null && c.Visible.InsetBy(-4, -4).Contains(p));
        if (twin?.Odd is FindOdd odd) hint = odd.Hint();
        else
        {
            var center = Center;
            double d = Math.Sqrt((p.X - center.X) * (p.X - center.X) + (p.Y - center.Y) * (p.Y - center.Y));
            hint = d < 140 ? "So warm!" : d < 300 ? "Warmer" : d < 520 ? "Colder" : "So cold";
        }
        Guesses.Add(new FindGuess(Guesses.Count, p, hint));
        if (Guesses.Count == 3) Phase = Phases.Lost;
        Changed?.Invoke();
    }

    /// After a find: the next level (remembered). After three misses: the same level, a new layout.
    public void Next()
    {
        int level = Phase == Phases.Found ? Math.Min(6, Round.Level + 1) : Round.Level;
        SavedLevel = level;
        MaxLevel = level;
        var rng = new FindRandom(NewSeed());
        Round = FindRound.Make(level, rng);
        Deal(rng);
        Changed?.Invoke();
    }

    /// A lookalike's coat: yours with one detail wrong (the coat id changes, so it never counts as yours).
    public static CatCoat Variant(CatCoat c, FindOdd odd)
    {
        bool dark = c.Id == "tux" || c.Id == "cocoa";
        var contrast = dark ? Gfx.C(0.98, 0.96, 0.92) : Gfx.C(0.30, 0.24, 0.23);
        var patches = new List<(Color Color, Rect Rect)>(c.Patches);
        if (odd == FindOdd.Spot) patches.Add((contrast, new Rect(25.5, 11, 10, 8)));
        return new CatCoat(c.Id + "-" + odd.Name(), c.Name, c.Fur, c.Stripe, c.Shade, c.Cream, c.Line, c.Eye)
        {
            Patches = patches,
            Glasses = odd == FindOdd.Glasses ? !c.Glasses : c.Glasses,
            EarL = odd == FindOdd.Ears ? contrast : c.EarL,
            EarR = odd == FindOdd.Ears ? contrast : c.EarR,
            NapPatches = c.NapPatches,
        };
    }

    /// A new layout for the current round: your cat behind a prop, the lookalikes (mostly out in the open, where their
    /// one wrong detail shows), then decoys in the other seven coats — behind props, on roofs, on the open ground —
    /// never covering yours, never wearing your coat.
    void Deal(FindRandom rng)
    {
        var me = coat ?? CatCoat.Current; var R = Round; var place = R.Place;
        var others = CatCoat.All.Where(c => c.Id != me.Id).ToList();
        if (place == FindPlace.Snow)                              // the snowy village is full of pale cats (the hard part)
        {
            var paleIds = new[] { "snow", "smokey", "lilac", "butter", "patches" };
            var pale = others.Where(c => paleIds.Contains(c.Id)).ToList();
            others.AddRange(pale); others.AddRange(pale);
        }
        var props = FindScene.Props(place);
        var @out = new List<FindCat>();
        var taken = new List<FRect>();
        bool Free(FRect r, double pad = 4) => !taken.Any(t => t.InsetBy(-pad, -pad).Intersects(r));
        FindCat? Behind(int i, CatCoat cc, double scale, double show)
        {
            var c = props[i].Cover; double w = 43 * scale;
            if (!(c.W > w + 6)) return null;
            double x = rng.DoubleClosed(c.MinX + w / 2 + 3, c.MaxX - w / 2 - 3);
            double y = props[i].FaceTop - show * 45 * scale + 43 * scale;      // `show` of its height above what the prop draws
            if (!(y + 2 * scale <= c.MaxY - 1)) return null;
            return new FindCat { Id = @out.Count, Coat = cc, State = CatState.Look, Feet = new Point(x, y), Scale = scale, Flip = rng.Bool(), Cover = c, Top = props[i].FaceTop };
        }
        bool Ok(FindCat c) => Free(c.Rect) && !FindScene.Blocked(place, c.Rect, c.Cover) && FindScene.Inside(c.Box);
        // 1. Yours: tucked behind a prop where it is still FAIR (face and ears show, enough body — FindFair);
        //    if no prop gives that, out in the open.
        double minShow = R.Show.Lo;
        bool Fair(FindCat c) => FindFair.Fair(c, place, minShow);
        var hideouts = rng.Shuffled(Enumerable.Range(0, props.Count).Where(i => props[i].Hides));
        foreach (int i in hideouts.Where(i => props[i].FairHide))
        {
            bool placed = false;
            for (int k = 0; k < 3; k++)
            {
                double show = rng.DoubleClosed(R.Show.Lo, R.Show.Hi);
                var c = Behind(i, me, R.Mine, show);
                if (c != null && Ok(c) && Fair(c)) { @out.Add(c); placed = true; break; }
            }
            if (placed) break;
        }
        int t0 = 0;
        while (@out.Count == 0 && t0 < 1500)
        {
            t0++;
            var spot = FindScene.OpenSpot(place, rng);
            var c = new FindCat { Id = 0, Coat = me, State = CatState.Look, Feet = new Point(spot.X, spot.Y), Scale = R.Mine * spot.Scale, Flip = rng.Bool() };
            if (Ok(c) && Fair(c)) @out.Add(c);
        }
        Unfair = @out.Count == 0;
        if (@out.Count == 0 && hideouts.Count > 0 && Behind(hideouts[0], me, R.Mine, R.Show.Hi) is FindCat fb) @out.Add(fb);   // never happens (the check asserts it)
        if (@out.Count == 0) @out.Add(new FindCat { Id = 0, Coat = me, State = CatState.Look, Feet = new Point(550, 520), Scale = R.Mine });
        taken.Add(@out[0].Rect.InsetBy(-12, -12));
        Yours = 0;
        // 2. Lookalikes: about a third peek over props (with a detail you can see from above), the rest in the open.
        var odds = rng.Shuffled(FindOddInfo.All);
        int tries = 0, made = 0;
        while (made < R.Lookalikes && tries < 3000)
        {
            tries++;
            if (odds.Count == 0) odds = rng.Shuffled(FindOddInfo.All);
            bool peek = R.Level >= 5 ? made % 2 == 1 : made % 3 == 2;
            int pick = peek ? Math.Max(0, odds.FindIndex(o => o.ShowsFromAbove())) : 0;
            var odd = odds[pick];
            double scale = R.Mine * rng.DoubleClosed(0.98, 1.12);
            FindCat? c = null;
            if (peek && odd.ShowsFromAbove() && rng.TryPick(hideouts.Where(i => props[i].FairHide).ToList(), out int hi))
            {
                c = Behind(hi, Variant(me, odd), scale, rng.DoubleClosed(0.55, 0.75));
            }
            else if (!peek)
            {
                var spot = FindScene.OpenSpot(place, rng);
                c = new FindCat { Id = @out.Count, Coat = Variant(me, odd), State = CatState.Look, Feet = new Point(spot.X, spot.Y), Scale = scale * spot.Scale, Flip = rng.Bool() };
            }
            if (c == null || !Ok(c) || !FindFair.Fair(c, place, 0.5)) continue;   // lookalikes are findable too
            c.Odd = odd;
            @out.Add(c); taken.Add(c.Rect); odds.RemoveAt(pick); made++;
        }
        // 3. Decoys peeking over the other props, then out in the open: sitting, napping, running about.
        int total = @out.Count + R.Decoys;
        foreach (int i in hideouts.Skip(1))
        {
            if (!(@out.Count < made + 1 + R.Peekers)) continue;                  // `for … where`: tested before every prop
            int n = props[i].Cover.W > 150 ? 2 : 1;
            for (int k = 0; k < n; k++)
            {
                var cc = rng.Pick(others); double sc = rng.DoubleClosed(0.5, 0.68); double sh = rng.DoubleClosed(0.45, 0.8);
                var c = Behind(i, cc, sc, sh);
                if (c == null || !Ok(c)) continue;
                @out.Add(c); taken.Add(c.Rect);
            }
        }
        tries = 0;
        var states = new[] { CatState.Look, CatState.Look, CatState.Look, CatState.Working, CatState.Sleeping, CatState.Nap, CatState.Running, CatState.Running };
        while (@out.Count < total && tries < 5000)
        {
            tries++;
            var spot = FindScene.OpenSpot(place, rng);
            var st = spot.Perch ? CatState.Look : rng.Pick(states);
            var coatPick = rng.Pick(others);
            double sc = rng.DoubleClosed(0.5, 0.68) * spot.Scale;
            var c = new FindCat { Id = @out.Count, Coat = coatPick, State = st, Feet = new Point(spot.X, spot.Y), Scale = sc, Flip = rng.Bool() };
            if (!Ok(c)) continue;
            @out.Add(c); taken.Add(c.Rect);
        }
        Cats = @out;
        Guesses = new List<FindGuess>();
        Phase = Phases.Playing;
        Celebrating = false;
    }
}

static class FindCelebrationInfo
{
    public const double Duration = 2.2;
    public const double Grow = 1.9;
    /// How far it rises: clear of whatever it hid behind.
    public static double Rise(FindCat cat)
    {
        if (cat.Cover is not FRect c) return 6;
        return Math.Max(6, cat.Feet.Y - c.MinY + 4);
    }
    /// Where the popped cat ends up (scene points).
    public static FRect Popped(FindCat cat)
    {
        var r = cat.Rect; double k = Grow; var f = cat.Feet;
        return new FRect(f.X + (r.MinX - f.X) * k, f.Y - Rise(cat) + (r.MinY - f.Y) * k, r.W * k, r.H * k);
    }
}

// MARK: — the fairness self-check (`Cataholic --check-find [N]`, run by the workflow on every push)

/// Deals N layouts per level per coat, silently, and checks every placement of your cat and of every lookalike against
/// FindFair: the face and ears show, enough of the body shows, never behind a snowman. Prints a table; exit 0 only
/// when every placement passes (owner 13 Oct 2026, after a cat hid wholly behind a snowman).
static class FindCheck
{
    static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);
    public static int Run(int n, Action<string> print)
    {
        var sw = Stopwatch.StartNew();
        string? failed = null;
        print($"Find my cat fairness check — {n} layouts × {CatCoat.All.Count} coats × 5 levels + endless");
        for (int lv = 1; lv <= 6; lv++)
        {
            int mine = 0, minePass = 0, open = 0, twins = 0, twinPass = 0, hidden = 0, total = 0;
            double least = 1.0;
            foreach (var coat in CatCoat.All)
            {
                for (int k = 0; k < n; k++)
                {
                    ulong seed = (ulong)(lv * 1_000_003 + k * 7_919 + 17);
                    var g = new FindGame(lv, seed, coat);
                    var place = g.Round.Place; var m = g.Mine;
                    least = Math.Min(least, FindFair.Shown(m));
                    bool ok = !g.Unfair && m.Coat == coat && FindFair.Fair(m, place, g.Round.Show.Lo);
                    mine++;
                    if (ok) minePass++;
                    else if (failed == null) failed = $"L{lv} coat {coat.Id} seed {seed}: your cat (shows {FindFair.Shown(m):0.00})";
                    if (m.Cover == null) open++;
                    hidden += g.Cats.Count(c => c.Cover != null); total += g.Cats.Count;
                    foreach (var t in g.Cats.Where(c => c.Odd != null))
                    {
                        twins++;
                        if (FindFair.Fair(t, place, 0.5)) twinPass++;
                        else if (failed == null) failed = $"L{lv} coat {coat.Id} seed {seed}: lookalike {t.Id} ({t.Odd!.Value.Name()})";
                    }
                }
            }
            print(F("L{0}  your cat {1}/{2} fair ({3:0.0}%) · in the open {4} · lookalikes {5}/{6} fair · least shown {7:0}% · ~{8} cats, {9} behind props",
                    lv, minePass, mine, minePass * 100.0 / Math.Max(1, mine), open, twinPass, twins, least * 100, total / Math.Max(1, mine), hidden / Math.Max(1, mine)));
        }
        print(F("took {0:0.0} s", sw.Elapsed.TotalSeconds));
        if (failed != null) { print("FAIL — first failure: " + failed); return 1; }
        print("PASS — every placement is fair");
        return 0;
    }
}
