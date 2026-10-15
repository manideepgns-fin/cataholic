using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Cataholic;

// CatOwn — what she does next, on her own (owner 13 Oct 2026: "does it crawl all over laptop, actually do something on
// her own … just keep doing something"). CatModel.OwnTurn asks for a turn the moment she settles (Chain); this picks ONE
// thing from a weighted pool — only what is possible from where she is — and starts it:
//   travel ~60%  patrol to the far end of her floor and peek over · leap to another window top · climb up a window side ·
//                hop off / climb down her window · chase the pointer a few steps · zoomies · up the screen edge to the pin
//   on the spot  the "Do something" emotes (hi at most every 30 min)
//   rest         a short nap (held as one still frame) · a 2–4 s still sit
// The screen is a 4×3 grid of cells with last-visit times: travel that lands somewhere she hasn't been lately weighs more,
// so over a few minutes she covers the whole screen. Silent throughout (sounds only when you interact, owner 27 Sep 2026).
// She never moves your windows or your pointer.
static class CatOwn
{
    public enum Kind { Travel, Spot, Rest }
    public sealed class Option
    {
        public string Name = ""; public Kind Kind; public double W;
        public Point? To;                          // where her box (top-left) ends up — null: unknown / stays
        public System.Action Run = () => { };
    }
    /// Where she is: box top-left, the floor under her, the windows (front to back), the pointer when it is moving, and
    /// whether hi is allowed.
    public sealed record Here(Point At, bool Hanging, CatGravity.Floor Floor, List<CatGravity.Win> Wins, Point? Pointer, bool HiOK);

    static readonly DateTime[] visited = Enumerable.Repeat(DateTime.MinValue, 12).ToArray();
    static readonly List<(string Name, Kind Kind)> recent = new();
    public static IReadOnlyList<(string Name, Kind Kind)> Recent => recent;
    static (int Cell, int N) sameCell = (-1, 0);
    static int lowPicks;                           // picks in a row made from the bottom row of the screen
    public static DateTime LastZoomies = DateTime.MinValue;
    /// A line per pick (the test scenarios trace with it). Production leaves it alone.
    public static System.Action<string>? Log;

    /// The 4×3 cell of the screen her box centre is in.
    public static int Cell(Point p, Rect sf)
    {
        double cx = p.X + CatLayout.ScreenW / 2 - sf.Left, cy = p.Y + CatLayout.ScreenH / 2;
        int c = Math.Min(Math.Max((int)(cx / sf.Width * 4), 0), 3), r = Math.Min(Math.Max((int)(cy / sf.Height * 3), 0), 2);
        return r * 4 + c;
    }

    static Point? lastPointerSeen;
    /// A live turn: read where she is, pick, start it.
    public static void Turn(CatModel m, bool hiOK)
    {
        if (CatPanel.CharTopLeft() is not Point at) { m.Chain(); return; }
        var wins = CatGravity.Windows();
        double w = CatLayout.ScreenW;
        var fl = CatGravity.FloorBelow(at.X + w / 2, at.Y + CatGravity.Feet - 3, wins);
        // the pointer, only while it is moving (the Mac asks the event source; here: input in the last 0.5 s and not where it was last turn)
        Point? pointer = null;
        var p = Display.Pointer();
        if (Win32.IdleSeconds < 0.5 && lastPointerSeen != p) pointer = p;
        lastPointerSeen = p;
        var h = new Here(at, CatPanel.Hanging, fl, wins, pointer, hiOK);
        if (Pick(Options(h, m), h) is not Option o) { m.Chain(); return; }
        o.Run();
    }

    /// Choose one option: the caps first, then novelty and height balance, then a weighted draw. Records the pick.
    public static Option? Pick(List<Option> all, Here h)
    {
        var t = DateTime.Now; var sf = Display.Frame; int c = Cell(h.At, sf);
        sameCell = sameCell.Cell == c ? (c, sameCell.N + 1) : (c, 1);
        visited[c] = t;
        var opts = new List<Option>(all);
        // never the same thing 3× in a row; never two on-the-spot actions back to back
        if (recent.Count >= 2 && recent[^1].Name == recent[^2].Name) { var n = recent[^1].Name; opts.RemoveAll(o => o.Name == n); }
        if (recent.Count > 0 && recent[^1].Kind == Kind.Spot) opts.RemoveAll(o => o.Kind == Kind.Spot);
        // 3 picks in one cell → she moves on
        if (sameCell.N >= 3 && opts.Any(o => o.Kind == Kind.Travel)) opts.RemoveAll(o => o.Kind != Kind.Travel);
        // 2 picks on the bottom row → up, whenever there is a way up (owner 13 Oct 2026: on the real Mac she paced corner
        // to corner along the Dock line for a whole minute — patrol to the far corner kept winning the draw)
        lowPicks = !h.Hanging && c / 4 == 2 ? lowPicks + 1 : 0;
        if (lowPicks >= 2)
        {
            var ups = opts.Where(o => o.Kind == Kind.Travel && o.To is Point to && Cell(to, sf) / 4 < 2).ToList();
            if (ups.Count > 0) opts = ups;
        }
        foreach (var o in opts.Where(o => o.Kind == Kind.Travel))
        {
            if (o.To is Point to)
            {
                double since = (t - visited[Cell(to, sf)]).TotalSeconds;
                o.W *= (1 + Math.Min(since / 60, 2)) / 2;        // 0.5 … 1.5: steers WHERE she goes, keeps the ~60% travel share
            }
            o.W *= Flow(recent.Count > 0 ? recent[^1].Name : null, o.Name);
        }
        double total = opts.Sum(o => o.W);
        if (!(total > 0)) return null;
        double r = Rnd.In(0, total);
        var chosen = opts[^1];
        foreach (var o in opts) { if (r < o.W) { chosen = o; break; } r -= o.W; }
        recent.Add((chosen.Name, chosen.Kind)); if (recent.Count > 8) recent.RemoveAt(0);
        if (chosen.Name == "zoomies") LastZoomies = t;
        Log?.Invoke($"cell {c} -> {chosen.Name} ({(chosen.To is Point q ? "cell " + Cell(q, sf) : "here")})");
        return chosen;
    }

    /// Natural chains (desktop-pet prior art): zoomies → patrol → leap on; a climb up → a walk along the top → hop down.
    static double Flow(string? prev, string next) => (prev, next) switch
    {
        ("zoomies", "patrol") or ("patrol", "leap") or ("climb up", "patrol") or ("leap", "patrol") => 2,
        ("patrol", "hop down") or ("patrol", "climb down") => 1.5,
        ("edge to pin", "drop") => 0.4,                                  // up on the pin she stays a moment
        _ => 1,
    };

    /// Everything she could do from here, with its base weight. A weight exists only when the move is possible.
    public static List<Option> Options(Here h, CatModel m)
    {
        var sf = Display.Frame; double w = CatLayout.ScreenW, z = CatLayout.Zoom, feet = CatGravity.Feet;
        var fl = h.Floor; var at = h.At; double cx = at.X + w / 2; var t = DateTime.Now;
        bool onWin = !h.Hanging && fl.Window != null, onDock = !h.Hanging && fl.Window == null;
        bool high = h.Hanging || (onWin && at.Y + feet < sf.Height * 0.5);
        double up = onDock ? 1.5 : 1.0, down = high ? 1.5 : 1.0;
        var o = new List<Option>();
        Option Opt(string name, Kind kind, double wt, Point? to, System.Action run) => new() { Name = name, Kind = kind, W = wt, To = to, Run = run };

        // — travel —
        if (!h.Hanging)
        {
            // Patrol to the far end of her floor and peek over it (on the taskbar line: a screen corner, then look around).
            double inset = onDock ? 8 : 4;
            double lo = Math.Max(fl.X0, sf.Left) + inset, hi = Math.Min(fl.X1, sf.Right) - w - inset;
            if (hi - lo > 60)
            {
                double end = at.X - lo > hi - at.X ? lo : hi, dir = end == hi ? 1 : -1;
                if (Math.Abs(end - at.X) > 60)
                    o.Add(Opt("patrol", Kind.Travel, 14, new Point(end, at.Y), () => Patrol(end, at.Y, dir, m)));
            }
            // Leap to another window's top: within ~450 pt across and 250 pt up or down, onto the nearest spot of its top
            // you can SEE (a window half behind another is landed on where it shows — stacked real windows, 13 Oct 2026).
            // Up only from beside the window (never up through it); the arc clears the ledge (CatPanel.LifeJump).
            var leaps = new List<Point>();
            foreach (var win in h.Wins)
            {
                if (win.Id == fl.Window) continue;
                var r = win.Rect;
                if (r.Width < w + 12 || CatGravity.Ledge(r) is not double ledge) continue;
                double top = ledge - feet;
                if (!(Math.Abs(top - at.Y) <= 250 && (top > at.Y - 20 || cx < r.Left || cx > r.Right))) continue;
                double? spot = null;
                var cand = new List<double>();
                for (double lx = r.Left + 6; lx <= r.Right - w - 6; lx += 24) cand.Add(lx);
                cand.Add(Math.Min(Math.Max(at.X, r.Left + 6), r.Right - w - 6));
                foreach (double lx in cand)
                {
                    double dx = Math.Abs(lx - at.X);
                    if (dx <= 450 && Math.Sqrt(dx * dx + (top - at.Y) * (top - at.Y)) > 60 && lx >= sf.Left && lx + w <= sf.Right
                        && dx < Math.Abs((spot ?? double.PositiveInfinity) - at.X)
                        && CatGravity.FloorBelow(lx + w / 2, ledge - 0.5, h.Wins).Window == win.Id)
                        spot = lx;
                }
                if (spot is double sx) leaps.Add(new Point(sx, top));
            }
            foreach (var p in leaps)
            {
                var pp = p;
                o.Add(Opt("leap", Kind.Travel, 14 * Math.Max(up, down) / leaps.Count, pp, () =>
                {
                    CatPanel.leapTarget = (pp.X, pp.Y);
                    m.StartAction(CatModel.Action.Jump);
                }));
            }
            // Climb up a window side (walks to its foot first).
            if (CatPanel.Climb(at, fl, h.Wins, go: false) is Point cp)
                o.Add(Opt("climb up", Kind.Travel, 12 * up, cp, () => CatPanel.Climb(at, fl, h.Wins, go: true)));
            // Up the screen edge — only from the taskbar line: onto the top of a window flush with that edge (a maximised
            // window has no side on screen to climb), else to her pin.
            if (onDock && CatPanel.Climb(at, fl, h.Wins, edge: true, go: false) is Point ep)
            {
                bool pin = ep.X < sf.Left || ep.X + w > sf.Right;        // the pin hangs past the edge; a window landing never does
                o.Add(Opt(pin ? "edge to pin" : "edge up", Kind.Travel, pin ? 3 : 12 * up, ep, () => CatPanel.Climb(at, fl, h.Wins, edge: true, go: true)));
            }
        }
        if (onWin && fl.Window is int wid && h.Wins.FindIndex(x => x.Id == wid) is int wi and >= 0)
        {
            var r = h.Wins[wi].Rect;
            // Hop down off the nearer end, or climb down that side of her window head first.
            bool left = cx - r.Left < r.Right - cx; double dir = left ? -1 : 1;      // dir: the way she goes off
            double endX = left ? Math.Max(r.Left, sf.Left) + 4 : Math.Min(r.Right, sf.Right) - w - 4;
            double offX = endX + dir * 70;
            if (offX >= sf.Left && offX + w <= sf.Right)
            {
                var land = CatGravity.FloorBelow(offX + w / 2, fl.Y + 1, h.Wins);
                o.Add(Opt("hop down", Kind.Travel, 5 * down, new Point(offX, land.Y - feet), () =>
                    CatPanel.Glide(endX, at.Y, 1, 0, () => CatPanel.Drop(dir * 140, -220, wid))));
            }
            else
            {
                // a window as wide as the screen has no end to hop off: a hop off its front, when something lower catches her
                var land = CatGravity.FloorBelow(cx, fl.Y - 0.5, h.Wins.Where(x => x.Id != wid).ToList());
                if (land.Y > fl.Y + 120)
                    o.Add(Opt("hop down", Kind.Travel, 5 * down, new Point(at.X, land.Y - feet), () => CatPanel.Drop(dir * 60, -220, wid)));
            }
            // Climb down that side — or, for a window flush with the screen edge (maximised), down the screen edge itself.
            double gap = 12 * z; bool edgeWall = left ? r.Left <= sf.Left + 2 : r.Right >= sf.Right - 2;
            double bas = edgeWall ? (left ? sf.Left + gap : sf.Right - gap) - w / 2 : (left ? r.Left - gap : r.Right + gap) - w / 2;
            if (edgeWall || (bas >= sf.Left && bas + w <= sf.Right))
            {
                var below = CatGravity.FloorBelow(bas + w / 2, fl.Y + 1, h.Wins);
                double bottom = edgeWall ? below.Y : Math.Min(below.Y, r.Bottom);
                if (bottom - fl.Y > 120)
                    o.Add(Opt("climb down", Kind.Travel, 5 * down, new Point(bas, below.Y - feet), () =>
                        ClimbDown(endX, at.Y, bas, edgeWall ? dir : -dir, bottom - feet - 20 * z)));
            }
        }
        if (h.Hanging)
        {
            // Off the pin: let go and land on whatever is below (zoomies are their own pick).
            var land = CatGravity.FloorBelow(cx, at.Y + feet, h.Wins);
            o.Add(Opt("drop", Kind.Travel, 15, new Point(at.X, land.Y - feet), () => { CatPanel.SetHanging(false); CatPanel.Drop(0, -60); }));
        }
        // Chase the pointer a few steps — only while it moves, within 400 pt, over her floor.
        if (!h.Hanging && h.Pointer is Point pt && Math.Sqrt((pt.X - cx) * (pt.X - cx) + (pt.Y - (at.Y + feet / 2)) * (pt.Y - (at.Y + feet / 2))) < 400
            && pt.X > fl.X0 && pt.X < fl.X1 && pt.Y < fl.Y && Math.Abs(pt.X - cx) > w / 2 + 50)
            o.Add(Opt("chase", Kind.Travel, 5, new Point(Math.Min(Math.Max(pt.X - w / 2, fl.X0), fl.X1 - w), at.Y), () => Chase(Rnd.In(2, 3), m)));
        if ((t - LastZoomies).TotalSeconds > 120)
            o.Add(Opt("zoomies", Kind.Travel, 6, null, CatPanel.Zoomies));

        // — on the spot —
        var spot2 = new List<(CatModel.Action, double)>
        {
            (CatModel.Action.Look, 6), (CatModel.Action.Groom, 5), (CatModel.Action.Stretch, 5), (CatModel.Action.Yawn, 4),
            (CatModel.Action.Spin, 3), (CatModel.Action.Big, 2), (CatModel.Action.Jump, 3),
        };
        if (h.HiOK) spot2.Add((CatModel.Action.Hi, 1));
        foreach (var (a, wt) in spot2)
        {
            var aa = a;
            o.Add(Opt(aa.ToString().ToLowerInvariant(), Kind.Spot, wt, null, () => m.StartAction(aa)));
        }

        // — rest (each under ~10 s) —
        o.Add(Opt("nap", Kind.Rest, 6, null, () => m.StartAction(CatModel.Action.Nap, Rnd.In(6, 10))));
        o.Add(Opt("sit", Kind.Rest, 4, null, () => m.Chain(2, 4)));
        return o;
    }

    // ── the new moves (reuse the walk, the leap, the climb and the fall) ──

    /// Walk to the end of her floor, settle there, and peek over it (the look, facing out).
    static void Patrol(double x, double top, double dir, CatModel m) =>
        CatPanel.Glide(x, top, 1, 0, () =>
        {
            CatPanel.SettleHome(x, top);
            m.JumpFacing = dir;
            m.StartAction(CatModel.Action.Look);
        });

    /// Walk to the end of her window, step over the edge onto its side, climb down head first, and hop the last bit.
    static void ClimbDown(double endX, double top, double bas, double wallDir, double y1)
    {
        double z = CatLayout.Zoom;
        CatPanel.Glide(endX, top, 1, 0, () =>
            CatPanel.Glide(bas, top + 2 * z, 0.35, 8 * z, () =>
                CatPanel.ClimbWall(bas, y1, wallDir, () =>
                    CatPanel.Drop(-wallDir * 70, -120))));
    }

    /// A few short walks toward the moving pointer, stopping 40 pt short of it — then a look or a happy squint.
    static void Chase(int steps, CatModel m)
    {
        if (steps <= 0 || CatPanel.CharTopLeft() is not Point at) return;
        double w = CatLayout.ScreenW; var p = Display.Pointer(); double cx = at.X + w / 2;
        var fl = CatGravity.FloorBelow(cx, at.Y + CatGravity.Feet - 3, CatGravity.Windows());
        double dir = p.X > cx ? 1 : -1;
        double want = p.X - dir * (w / 2 + 40) - w / 2;
        double lo = Math.Max(fl.X0, Display.Frame.Left) + 4, hi = Math.Min(fl.X1, Display.Frame.Right) - w - 4;
        double x = Math.Min(Math.Max(Math.Min(Math.Max(want, at.X - 160), at.X + 160), lo), hi);   // Swift's min(max(v, lo), hi)
        if (!(Math.Abs(x - at.X) > 10)) { FinishChase(m); return; }
        CatPanel.Glide(x, at.Y, 1, 0, () =>
        {
            CatPanel.SettleHome(x, at.Y);
            if (steps > 1) Chase(steps - 1, m); else FinishChase(m);
        });
    }
    static void FinishChase(CatModel m)
    {
        if (Rnd.Bool()) { m.StartAction(CatModel.Action.Look); return; }
        m.Land(0.9);
        m.PetUntil = DateTime.Now.AddSeconds(1.2);
        Later.After(1.25, m.Publish);
        m.Chain(1.3, 2.2);
    }
}
