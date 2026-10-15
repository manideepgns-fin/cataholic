using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// The cat, drawn in code: a line-for-line port of Sources/Cataholic/CrewCharacter.swift (same coordinates, colours,
// line widths and paint order). The Swift Canvas works in screen points at zoom 2 (s = 2); here everything is in
// character-box points (s = 1) and the caller applies the zoom — so every width the Swift writes WITHOUT `* s`
// (the pin string, the text halo) is halved.
public static class CatDrawing
{
    /// Draws the cat for `p` in character-box points (46 × 62, origin top-left, y down; feet at y ≈ 49).
    /// The caller sets any scale (zoom 2), mirroring and offset on `dc` before calling.
    public static void Draw(DrawingContext dc, CatPose p) => new Painter(dc, p).Draw();

    /// Crop the drawing to the 46 × 62 box, like the Mac's panel does (the live app's window ends at the box). The Mac's
    /// ImageRenderer sheets do NOT crop (the melted nap's tail pokes out at x = 46), so --render-coats/--render-poses switch it off.
    public static bool CropToBox = true;

    /// A path in the Swift Path vocabulary: move / line / quad curve / close.
    sealed class PB
    {
        readonly List<(Point start, List<(bool quad, Point c, Point to)> segs, bool closed)> figs = new();
        public PB M(double x, double y) { figs.Add((new Point(x, y), new(), false)); return this; }
        public PB L(double x, double y) { figs[^1].segs.Add((false, default, new Point(x, y))); return this; }
        public PB Q(double x, double y, double cx, double cy) { figs[^1].segs.Add((true, new Point(cx, cy), new Point(x, y))); return this; }
        public PB Close() { var f = figs[^1]; figs[^1] = (f.start, f.segs, true); return this; }
        public Geometry Build()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
                foreach (var (start, segs, closed) in figs)
                {
                    c.BeginFigure(start, true, closed);
                    foreach (var (quad, cp, to) in segs)
                        if (quad) c.QuadraticBezierTo(cp, to, true, true); else c.LineTo(to, true, true);
                }
            g.Freeze();
            return g;
        }
    }

    sealed class Painter
    {
        readonly DrawingContext dc;
        readonly CatPose p;
        readonly CatCoat coat;
        int pushed;                                   // transforms/clips pushed on dc by the current pass; popped at its end

        Color fur, stripe, shade, cream, line, eye;
        static Color Pink = CatCoat.C(0.97, 0.65, 0.63), Collar = CatCoat.C(0.33, 0.44, 0.84), Gold = CatCoat.C(0.93, 0.76, 0.34);
        static Color Paper = CatCoat.C(1.0, 0.98, 0.94), Amber = CatCoat.C(0.91, 0.69, 0.29), White = Colors.White;
        const double LineW = 1.25;

        public Painter(DrawingContext dc, CatPose p)
        {
            this.dc = dc; this.p = p;
            coat = p.CoatOverride ?? CatCoat.Current;
            fur = coat.Fur; stripe = coat.Stripe; shade = coat.Shade; cream = coat.Cream; line = coat.Line; eye = coat.Eye;
        }

        // ---- the Swift helpers ------------------------------------------------------------------------------------
        static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
        static Brush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static Pen PenOf(Color c, double w, bool round)
        {
            var pen = new Pen(Br(c), w);
            if (round) { pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round; pen.LineJoin = PenLineJoin.Round; }
            pen.Freeze();
            return pen;
        }
        static Geometry Oval(double x, double y, double w, double h) => new EllipseGeometry(new Rect(x, y, w, h));
        static Geometry RRect(double x, double y, double w, double h, double r) => new RectangleGeometry(new Rect(x, y, w, h), r, r);
        static Point P(double x, double y) => new(x, y);

        void Push(Transform t) { dc.PushTransform(t); pushed++; }
        void Translate(double x, double y) => Push(new TranslateTransform(x, y));
        void Scale(double x, double y) => Push(new ScaleTransform(x, y));
        void Rotate(double deg) => Push(new RotateTransform(deg));
        void PopAll() { while (pushed > 0) { dc.Pop(); pushed--; } }

        void Fill(Geometry g, Color c) { if (c.A > 0) dc.DrawGeometry(Br(c), null, g); }
        void Stroke(Geometry g, Color c, double w, bool round = false) { if (c.A > 0) dc.DrawGeometry(null, PenOf(c, w, round), g); }

        /// The sticker outline: stroke first, then fill — only the outer half of the line shows.
        void F(Geometry g, Color c) { dc.DrawGeometry(null, new Pen(Br(line), LineW) { LineJoin = PenLineJoin.Round }, g); Fill(g, c); }
        /// A thick stroked limb/tail with the same outline around it.
        void T(PB path, double w, Color c)
        {
            var g = path.Build();
            Stroke(g, line, w + LineW, true);
            Stroke(g, c, w, true);
        }

        /// A text run as a filled outline: `center` puts (x, y) at the middle of the text box, otherwise at its top-left.
        /// `tallY` stretches the glyphs vertically about their baseline and `lift` moves them up — Segoe UI's lowercase z is
        /// shorter than SF Rounded's, so the ink is matched to the Mac's.
        Geometry Txt(string s, double size, string family, FontWeight weight, double x, double y, bool center, double tallY = 1, double lift = 0)
        {
            var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, 1.0);
            var g = ft.BuildGeometry(center ? new Point(x - ft.Width / 2, y - ft.Height / 2) : new Point(x, y));
            if (tallY != 1 || lift != 0)
            {
                double baseY = (center ? y - ft.Height / 2 : y) + ft.Baseline;
                g.Transform = new MatrixTransform(1, 0, 0, tallY, 0, baseY * (1 - tallY) - lift);
            }
            g.Freeze();
            return g;
        }

        // ---- entry ------------------------------------------------------------------------------------------------
        public void Draw()
        {
            if (p.State == CatState.Running) { Canvas(Runner, 0); return; }
            Canvas(Hanging, p.BreatheY / Zoom);        // .offset(y:) is in screen points; the drawing is in box points
            // the z overlays sit OUTSIDE the Canvas in the Swift ZStack: not clipped, not moved by breatheY
            if (p.State == CatState.Nap && p.ZOpacity > 0)
            {
                // the nap's z's drift up off the head — ink-dark with a paper halo; a second, smaller z follows half a breath behind
                var (zx, zy, drift) = p.Pin ? (36.0, 16.0, 0.3) : (27.0, 24.0, 0.55);
                for (int k = 0; k < 2; k++)
                {
                    double rise = Math.Max(0, p.ZRise - k * 5), op = k == 0 ? p.ZOpacity : (p.ZRise > 5 ? p.ZOpacity : 0);
                    var g = Txt("z", k == 0 ? 9 : 7, "Segoe UI", FontWeights.Black, zx + rise * drift + k * 2, zy - rise, false, 1.14, 0.5);
                    dc.PushOpacity(op);
                    foreach (var (hw, ha) in new[] { (1.8, 0.2), (1.2, 0.3), (0.7, 0.4) }) Stroke(g, A(Paper, ha), hw, true);   // .shadow(Paper, radius 1.2) twice: a soft halo ~0.6 box pt wide
                    Fill(g, CatCoat.C(0.24, 0.26, 0.45));
                    dc.Pop();
                }
            }
            if (p.State == CatState.Sleeping)
            {
                var g = Txt("z", 10, "Consolas", FontWeights.SemiBold, 2 - p.ZRise * 0.8, 6 - p.ZRise, false, 1.1);
                Fill(g, A(SecondaryLabel, p.ZOpacity));
            }
        }

        const double Zoom = 2;                         // CrewLayout.zoom

        /// NSColor.secondaryLabelColor as the Mac reference renders it (light appearance: black at 50 %) — the one colour with no
        /// Windows equivalent.
        static readonly Color SecondaryLabel = Color.FromArgb(128, 0, 0, 0);
        static readonly Color SwiftGray = Color.FromRgb(142, 142, 147);

        /// Runs `body` inside the Swift Canvas: moved by `offsetY`, cropped to the 46 × 62 box.
        void Canvas(Action body, double offsetY)
        {
            dc.PushTransform(new TranslateTransform(0, offsetY));
            if (CropToBox) dc.PushClip(new RectangleGeometry(new Rect(0, 0, 46, 62)));
            body();
            PopAll();
            if (CropToBox) dc.Pop();
            dc.Pop();
        }

        // ---- zoomies ----------------------------------------------------------------------------------------------
        void Runner()
        {
            // A walk is a diagonal gait with the tail up; a gallop bounds, tail streaming, the body stretching and squashing each stride.
            bool walk = p.Stride < 30;
            double runPhase = p.RunGait == 2 ? 0.9 : p.RunPhase;      // a skid holds its legs braced; the dust keeps the real phase
            double swing = Math.Sin(runPhase) * p.Stride, swing2 = Math.Sin(runPhase + (walk ? Math.PI : 0.7)) * p.Stride;
            void Leg(Point hip, double deg, Color c)
            {
                double a = deg * Math.PI / 180, len = 5.5;
                var foot = new Point(hip.X + Math.Sin(a) * len, hip.Y + Math.Cos(a) * len);
                T(new PB().M(hip.X, hip.Y).L(foot.X, foot.Y), 5.6, c);
                F(Oval(foot.X - 2.6, foot.Y - 1.4, 5.2, 3), cream);
            }
            double bob = Math.Abs(Math.Sin(runPhase)) * (walk ? 0.8 : 1.4);
            // Speed: speed lines and dust drawn behind/under the cat first.
            double fast = !walk && p.RunGait == 0 ? Math.Min(Math.Max(p.RunSpeed, 0), 1) : 0;
            if (fast > 0.35)
                foreach (var (y, l) in new[] { (20.5, 1.0), (40.5, 0.75), (44.5, 0.55) })
                    Stroke(new PB().M(1.5, y).L(1.5 + 7 * l * fast, y).Build(), A(line, 0.45 * fast), 1.1, true);
            void Puff(double x, double y, double r, double a)
            {
                var c = Oval(x - r, y - r, 2 * r, 2 * r);
                Fill(c, A(CatCoat.C(0.86, 0.82, 0.76), a));
                Stroke(c, A(line, a * 0.5), 0.6);
            }
            if (!walk && (p.RunGait != 0 || fast > 0.5))
                for (int i = 0; i < 3; i++)                                   // little dust puffs kicked up, drifting back and fading
                {
                    double u = (p.RunPhase / (2 * Math.PI) * (p.RunGait == 1 ? 1.5 : 1) + i / 3.0) % 1;
                    if (p.RunGait == 2) Puff(33 + u * 6, 48.5 - u * 3, 1.4 + u * 1.6, (1 - u) * 0.85);   // skid: sprays ahead of the braced paws
                    else Puff(10 - u * 6, 48.5 - u * 3, 1.2 + u * 1.6, (1 - u) * 0.8);
                }
            Translate(0, -bob);
            if (!walk && runPhase != 0)                                       // gallop: stretch on the reach, squash on the gather
            {
                double k = Math.Sin(runPhase * 2) * (p.RunGait == 2 ? 0.02 : 0.1);
                Translate(20, 48); Scale(1 + k, 1 - k); Translate(-20, -48);
            }
            var back = new Point(11, 42.5); var front = new Point(27, 42.5);
            Leg(back, swing2, shade); Leg(front, -swing2, shade);             // far legs, in shade
            var tail = new PB().M(6, 33);
            if (walk)                                                         // tail up, a question-mark hook at the tip
                tail.Q(3.5 + Math.Sin(runPhase * 0.5) * 1.2, 18, 1.5, 29).Q(7.5, 15.5, 4.5, 14);
            else
                tail.Q(1.6, 24 + Math.Sin(runPhase) * 2.5, 0.5, 33);
            T(tail, 4.6, fur);
            F(Oval(3, 23, 33, 24), fur);                                      // the barrel
            // the coat runs too: the sitting patches, mapped onto the side-on barrel and head
            Spots(Oval(3, 23, 33, 24), false, 0, coat.Patches.Where(t => t.Rect.Top >= 26).Select(t => (t.Color,
                new Rect(3 + (t.Rect.Left - 10.5) * 33 / 25, 23 + (t.Rect.Top - 28.5) * 24 / 21, t.Rect.Width * 33 / 25, t.Rect.Height * 24 / 21))).ToList());
            Fill(Oval(8.5, 38.5, 22, 7.5), cream);                            // belly
            foreach (var (x, y) in new[] { (11.5, 24.2), (16.5, 23.4), (21.5, 23.6) })
                Stroke(new PB().M(x, y).Q(x - 1.2, y + 5, x + 0.6, y + 2.8).Build(), stripe, 1.8, true);
            Leg(back, swing, fur); Leg(front, -swing, fur);                   // near legs over the body
            // Head: ears (far one shaded), a big round head, muzzle
            double flat = !walk && p.RunGait != 2 && runPhase != 0 ? 1 : 0;  // ears pinned back at a gallop, up in a skid
            var earB = new PB().M(25.5, 18).Q(27.5 - 4 * flat, 8 + 3.5 * flat, 24.5, 11).Q(32.5, 14, 30.5, 9.5 + 2 * flat).Close();
            F(earB.Build(), coat.EarL ?? shade);
            var earF = new PB().M(32, 15).Q(36 - 5 * flat, 6.5 + 4 * flat, 32, 9).Q(41, 14.5, 39.5, 8.5 + 2.5 * flat).Close();
            F(earF.Build(), coat.EarR ?? fur);
            var inF = new PB().M(34, 13.5).L(36 - 3.5 * flat, 9 + 3 * flat).L(38.8, 13.5).Close();
            Fill(inF.Build(), Pink);
            F(Oval(22.5, 12.5, 21.5, 20.5), fur);
            Spots(Oval(22.5, 12.5, 21.5, 20.5), true, 0, coat.Patches.Where(t => t.Rect.Top < 26).Select(t => (t.Color,
                new Rect(22.5 + (t.Rect.Left - 7) * 21.5 / 32, 12.5 + (t.Rect.Top - 10.5) * 20.5 / 21.5, t.Rect.Width * 21.5 / 32, t.Rect.Height * 20.5 / 21.5))).ToList());
            foreach (var (x, y) in new[] { (28.5, 13.6), (31.5, 13.2) })
                Stroke(new PB().M(x, y).L(x + 0.4, y + 3.2).Build(), stripe, 1.5, true);
            Fill(Oval(34.5, 22.5, 9, 7.5), cream);
            Fill(Oval(41.6, 23, 2.4, 1.9), Pink);                             // nose
            Stroke(new PB().M(42.6, 25.2).Q(39.8, 27, 42.4, 27.2).Build(), eye, 0.8);
            Stroke(new PB().M(39.5, 25).L(45.5, 23.6).M(39.5, 26.2).L(45.5, 27.4).Build(), A(eye, 0.45), 0.6);
            Fill(Oval(30.5, 24, 3.8, 2.1), A(Pink, 0.55));                    // cheek
            if (walk || runPhase == 0)                                        // a walk (or standing): calm round eye
            {
                Fill(Oval(35.2, 17, 3.8, 4.6), eye);
                Fill(Oval(36.4, 17.7, 1.5, 1.5), White);
            }
            else                                                              // zoomies: huge eye, it is having the time of its life
            {
                Fill(Oval(34.8, 16.2, 4.6, 5.6), eye);
                Fill(Oval(36.1, 16.9, 1.8, 1.8), White);
                Fill(Oval(37.6, 20, 0.9, 0.9), White);
            }
            if (coat.Glasses)                                                 // side-on reading glasses: one round lens and its arm
            {
                Stroke(Oval(33.4, 15.4, 7.6, 7.6), line, 1.1);
                Stroke(new PB().M(33.5, 18.6).L(28.5, 17.4).Build(), line, 1.1, true);
            }
            Stroke(new PB().M(27, 30).L(29.6, 35.5).Build(), Collar, 2.4, true);
            Fill(Oval(28.2, 34, 3.6, 3.6), Gold);
        }

        // ---- the cat on her pin -------------------------------------------------------------------------------------
        void Hanging()
        {
            var s = p.State;
            // Side-on poses are off the pin: the leap and the stretch. The 0.2 s take-off crouch before the leap is drawn below, front-on.
            if (s == CatState.Jump && !p.JumpCrouch) { Leap(); return; }
            if (s == CatState.Stretch) { Bow(); return; }
            if (s == CatState.Nap && !p.Pin) { Curl(); return; }
            if (s == CatState.Sleeping && !p.Pin) { Melt(); return; }
            // Pin + string (the grown cat, the crouching cat and the ring step off it)
            if (p.Pin && s != CatState.Big && s != CatState.Jump)
            {
                Stroke(new PB().M(23, 0).L(23, 8).Build(), A(SwiftGray, 0.8), 0.5);      // Swift lineWidth: 1 is one SCREEN point = 0.5 box points
                Fill(RRect(19, 3, 8, 5, 1.5), CatCoat.C(0.72, 0.74, 0.80));
            }
            bool crouching = s == CatState.Jump && p.JumpCrouch;
            bool leaning = s == CatState.Clingy;
            // Whole-body lean for clingy: rotate the cat (not the string) toward the pointer.
            if (leaning) { Translate(23, 46); Rotate(p.BodyTilt); Translate(-23, -46); }
            // Tail — curls up behind the body (life: slow sway). Alert: up with a hook at the tip.
            var tail = new PB();
            if (s == CatState.Alert)
                tail.M(31, 45).Q(37.5 + p.TailSway, 32, 38, 44).Q(34 + p.TailSway, 27.5, 38.5, 28);
            else
                tail.M(31, 46).Q(37.5 + p.TailSway, 37, 38.5, 46.5).Q(35 + p.TailSway, 32.5, 38.5, 33);
            T(tail, 4.4, fur);
            if (p.TailTip is Color tip)                                       // a lookalike's tail tip
            {
                var e = s == CatState.Alert ? new Point(34 + p.TailSway, 27.5) : new Point(35 + p.TailSway, 32.5);
                Stroke(new PB().M(e.X + 2.2, e.Y + 3.4).L(e.X, e.Y).Build(), tip, 4.4, true);
            }
            // Body + cream belly + back paws (life: breathing scale about the base). Crouch: body low, haunches up, take-off pose.
            Translate(23, 48); Scale(p.Breathe, p.Breathe); Translate(-23, -48);
            if (crouching)
            {
                F(Oval(11, 35.5, 24, 13.5), fur);                             // low, coiled, round
                Fill(Oval(16, 39, 14, 8), cream);
                F(Oval(13, 45.5, 7, 4), cream);
                F(Oval(26, 45.5, 7, 4), cream);
                F(Oval(25, 33.5, 10, 8), fur);                                // haunches up
            }
            else
            {
                // A loaf: wide round body, hips bulging at the base. Alert sits a touch taller.
                double bh = s == CatState.Alert ? 23 : 21, by = s == CatState.Alert ? 26.5 : 28.5;
                F(Oval(10.5, by, 25, bh), fur);
                F(Oval(9, 37, 9, 11.5), fur);                                 // left haunch
                F(Oval(28, 37, 9, 11.5), fur);                                // right haunch
                Spots(Oval(10.5, by, 25, bh), false, 0, null);
                Fill(Oval(16, 33, 14, 14.5), cream);                          // round belly
                F(Oval(12.5, 45.2, 8, 4.3), cream);                           // stubby paws
                F(Oval(25.5, 45.2, 8, 4.3), cream);
                var toes = new PB();
                foreach (var x in new[] { 15.2, 17.8, 28.2, 30.8 }) toes.M(x, 47.6).L(x, 49);
                Stroke(toes.Build(), A(line, 0.35), 0.6, true);
            }
            // Ears (drawn before the head so the head overlaps their base; life: twitch flicks the left tip).
            double hdx = p.HeadTurn;
            if (s == CatState.Alert)
            {
                // Alert: taller, forward ears (drawn INSTEAD of the base pair, not over it).
                F(new PB().M(11 + hdx, 17).L(12.5 + hdx, 3.5).L(19.5 + hdx, 10.5).Close().Build(), fur);
                F(new PB().M(35 + hdx, 17).L(33.5 + hdx, 3.5).L(26.5 + hdx, 10.5).Close().Build(), fur);
                Fill(new PB().M(13.5 + hdx, 14).L(14 + hdx, 7).L(17.5 + hdx, 11).Close().Build(), Pink);
                Fill(new PB().M(32.5 + hdx, 14).L(32 + hdx, 7).L(28.5 + hdx, 11).Close().Build(), Pink);
            }
            else if (crouching)
            {
                // Crouch: ears flattened back.
                F(new PB().M(11.5, 18).L(6, 12).L(14, 11).Close().Build(), fur);
                F(new PB().M(34.5, 18).L(40, 12).L(32, 11).Close().Build(), fur);
            }
            else
            {
                // Rounded, wide-set ears (a chunky head wears small soft ears).
                double eb = p.EarsBack, tw = p.EarTwitch;   // flattening slides each tip out and down
                var earL = new PB().M(9 + hdx, 19).Q(12 + hdx - tw * 2.2 - 5.5 * eb, 7.5 - tw * 1.6 + 5 * eb, 8.5 + hdx, 11).Q(20 + hdx, 12.5, 15.5 + hdx, 8 + 2 * eb).Close();
                var earR = new PB().M(37 + hdx, 19).Q(34 + hdx + 5.5 * eb, 7.5 + 5 * eb, 37.5 + hdx, 11).Q(26 + hdx, 12.5, 30.5 + hdx, 8 + 2 * eb).Close();
                F(earL.Build(), coat.EarL ?? fur); F(earR.Build(), coat.EarR ?? fur);
                var inL = new PB().M(11.5 + hdx, 16).L(12.8 + hdx - tw * 1.6 - 3.5 * eb, 10.5 - tw * 1.2 + 3 * eb).L(17.5 + hdx, 13).Close();
                var inR = new PB().M(34.5 + hdx, 16).L(33.2 + hdx + 3.5 * eb, 10.5 + 3 * eb).L(28.5 + hdx, 13).Close();
                Fill(inL.Build(), Pink); Fill(inR.Build(), Pink);
            }
            // Head group: shift sideways and tilt for look / clingy.
            Translate(23 + hdx, 21);
            if (p.HeadTilt != 0) Rotate(p.HeadTilt);
            Translate(-23, -21);
            double hx = hdx;
            // Head, forehead stripes, muzzle. A wide, squishy head (wider than tall).
            F(Oval(7 + hx, 10.5, 32, 21.5), fur);
            Spots(Oval(7 + hx, 10.5, 32, 21.5), true, hx, null);
            foreach (var (x0, x1) in new[] { (20.3, 20.8), (23.0, 23.0), (25.7, 25.2) })
                Stroke(new PB().M(x0 + hx, 11.6).L(x1 + hx, 15).Build(), stripe, 1.5, true);
            Fill(Oval(16.5 + hx, 21.5, 13, 8.5), cream);
            Fill(new PB().M(21.8 + hx, 23).L(24.2 + hx, 23).L(23 + hx, 24.5).Close().Build(), Pink);   // nose
            if (s == CatState.Yawn && p.MouthOpen > 0.08) YawnMouth(hx, p.MouthOpen);
            else if (s == CatState.Groom && p.Tongue)
                Fill(Oval(22.1 + hx, 25.2, 1.8, 2.6), Pink);                  // tongue peeks out mid-lick
            else
                Stroke(new PB().M(23 + hx, 24.5).Q(21 + hx, 26, 22.8 + hx, 26.3).M(23 + hx, 24.5).Q(25 + hx, 26, 23.2 + hx, 26.3).Build(), eye, 0.8);
            Stroke(new PB().M(17.5 + hx, 24.5).L(5.5 + hx, 23).M(17.5 + hx, 26).L(5.8 + hx, 27.4)
                .M(28.5 + hx, 24.5).L(40.5 + hx, 23).M(28.5 + hx, 26).L(40.2 + hx, 27.4).Build(), A(eye, 0.45), 0.6);
            Fill(Oval(11.5 + hx, 22.4, 4.4, 2.4), A(Pink, p.Happy ? 0.85 : 0.5));
            Fill(Oval(30.1 + hx, 22.4, 4.4, 2.4), A(Pink, p.Happy ? 0.85 : 0.5));
            var pupil = p.Pupil;
            if (p.Eyes is CatEyes e2)
                switch (e2)
                {
                    case CatEyes.Open: OpenEyes(pupil, false, hx + pupil.X * 0.4); break;
                    case CatEyes.Half: OpenEyes(default, true, hx); break;
                    case CatEyes.Shut: ShutEyes(hx); break;
                    case CatEyes.Happy: HappyEyes(hx); break;
                    case CatEyes.Wide: OpenEyes(default, false, hx, wide: true); break;
                }
            else if (p.Happy) HappyEyes(hx);
            else
                switch (s)
                {
                    case CatState.Working: case CatState.Reading:    // at work over the letter: content "^ ^" eyes
                        HappyEyes(hx); break;
                    case CatState.Needs: case CatState.Jump: case CatState.Hi: case CatState.Big: case CatState.Look:
                        if (p.BlinkShut) ShutEyes(hx); else OpenEyes(pupil, false, p.HeadTurn + pupil.X * 0.4);
                        break;
                    case CatState.Alert:
                        OpenEyes(default, false, 0, wide: true);          // eyes WIDE: bigger whites + bigger pupils
                        break;
                    case CatState.Clingy:
                        OpenEyes(pupil, true, 0);
                        // purr mark: two short curved lines near the head (no text)
                        Stroke(new PB().M(34 + hx, 14).Q(36 + hx, 17, 35.5 + hx, 15.5).Build(), A(eye, 0.55), 0.9);
                        Stroke(new PB().M(37 + hx, 12).Q(39 + hx, 15, 38.5 + hx, 13.5).Build(), A(eye, 0.55), 0.9);
                        break;
                    case CatState.Sleeping: case CatState.Running: case CatState.Yawn: case CatState.Nap: case CatState.Stretch:
                        ShutEyes(hx); break;
                    case CatState.Groom:
                        ShutEyes(hx, only: 0);
                        OpenEyes(default, false, 0, only: 1);
                        break;
                }
            if (coat.Glasses && s != CatState.Sleeping && s != CatState.Nap) Glasses(hx);
            // Head-group transform ends here — paws, letter, sign stay on the body.
            Translate(23, 21);
            if (p.HeadTilt != 0) Rotate(-p.HeadTilt);
            Translate(-(23 + hdx), -21);
            // Collar + gold tag carrying the agent's glyph (on the body, unturned)
            double cb = p.CollarBold, ch = 2.8 * cb, bd = 6.4 * (1 + (cb - 1) * 0.5);
            Fill(RRect(14, 31.6 - ch / 2, 18, ch, ch / 2), p.CollarTint ?? Collar);
            if (p.Bell)
            {
                Fill(Oval(23 - bd / 2, 31.2 + (cb - 1), bd, bd), Gold);
                // Swift: size 4.6 * bd / 6.4. Segoe UI Symbol's heart is ~25 % smaller at the same size, so 6.1 matches the Mac's ink.
                var glyph = Txt(p.Glyph, 6.1 * bd / 6.4, "Segoe UI, Segoe UI Symbol", FontWeights.Black, 23, 31.2 + (cb - 1) + bd / 2, true);
                Fill(glyph, A(Colors.Black, 0.55));
            }
            if (s == CatState.Working || s == CatState.Reading)
            {
                // Holding a letter with both front paws
                Fill(RRect(15, 37, 16, 10, 1.5), Paper);
                Stroke(new PB().M(15.5, 37.5).L(23, 42).L(30.5, 37.5).Build(), CatCoat.C(0.81, 0.78, 0.72), 0.8);
                F(Oval(11.5, 38, 6.5, 5), fur);                               // chubby paws on the letter
                F(Oval(28, 38, 6.5, 5), fur);
            }
            if (s == CatState.Hi && p.PawRaise > 0.12)
            {
                // wave: a soft round arm and a mitten paw on a clear arc. pawRaise swings it up from a tuck at the side; pawWave waves it.
                double r = Math.Min(p.PawRaise, 1.2);
                double th = (110 + (33 - 110) * r + p.PawWave) * Math.PI / 180;   // from straight up, + = out to the left
                double len = 12.5 * (0.4 + 0.6 * Math.Min(r, 1));
                Mitten(new Point(17.5, 35), new Point(17.5 - Math.Sin(th) * len, 35 - Math.Cos(th) * len), 2.4, p.PawWrist);
            }
            if (s == CatState.Needs)
            {
                // A paw holds the sign up on the LEFT (the side facing into the screen)
                T(new PB().M(17, 36).Q(11.5, 29.5, 13.5, 34.5), 3, fur);
                // x ≥ 0: a Canvas crops anything drawn outside its frame
                var sign = RRect(0.5, 17, 13, 13, 3);
                Fill(sign, Paper);
                Stroke(sign, Amber, 1.2);
                F(Oval(9.2, 27.5, 4.2, 3.4), cream);                          // the paw on the sign
                if (p.Badge.Length > 0)
                {
                    string shown = int.TryParse(p.Badge, out var n) && n > 9 ? "9+" : p.Badge;
                    var tg = Txt(shown, 10.4, "Consolas", FontWeights.Bold, 7, 23, true);   // SF Mono Bold is a touch bigger and heavier than Consolas Bold
                    Fill(tg, CatCoat.C(0.48, 0.33, 0.06)); Stroke(tg, CatCoat.C(0.48, 0.33, 0.06), 0.3, true);
                }
            }
            if (s == CatState.Groom && p.GroomPaw is Point g && Hypot(g.X - 29, g.Y - 36) > 2)
            {
                // the same mitten, lifted to the mouth for the lick and the face wipe: the paw lands on `g`
                double d = Hypot(g.X - 28.5, g.Y - 35), k = Math.Max(0, d - 2.6) / Math.Max(d, 0.01);
                Mitten(new Point(28.5, 35), new Point(28.5 + (g.X - 28.5) * k, 35 + (g.Y - 35) * k), -1.8, p.PawWrist);
            }
        }

        static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

        /// A chunky arm and a round mitten paw: a soft fur arm as thick as the haunch line, ending in a cream mitten with
        /// a pink pad and three toe beans, turned along the arm plus the wrist bend.
        void Mitten(Point a, Point e, double bend, double wrist)
        {
            double dx = e.X - a.X, dy = e.Y - a.Y, len = Math.Max(Hypot(dx, dy), 0.01);
            var elbow = new Point((a.X + e.X) / 2 - dy / len * bend, (a.Y + e.Y) / 2 + dx / len * bend);
            T(new PB().M(a.X, a.Y).Q(e.X, e.Y, elbow.X, elbow.Y), 4.6, fur);
            int mark = pushed;                                                // Swift's `var c = ctx`: transforms scoped to this paw
            Translate(e.X, e.Y);
            Rotate(Math.Atan2(e.Y - elbow.Y, e.X - elbow.X) * 180 / Math.PI + wrist);
            F(Oval(-1.4, -2.9, 6.2, 5.8), cream);                             // the mitten
            Fill(Oval(0.2, -1.1, 2.4, 2.2), A(Pink, 0.8));                    // the pad
            foreach (var y in new[] { -2.0, -0.35, 1.3 }) Fill(Oval(3.0, y, 1.3, 1.1), A(Pink, 0.8));   // toe beans
            while (pushed > mark) { dc.Pop(); pushed--; }
        }

        /// The real nap: curled into a round loaf, the tail wrapped round the front, the head tucked down on the paws, eyes
        /// shut. `napBreath` lifts the back as it breathes; `napPeek` opens one eye just before it wakes.
        void Curl()
        {
            double b = p.NapBreath * 3;                                       // the back rises ~3 pt
            var body = Oval(7, 30.5 - b, 36, 19 + b);
            F(body, fur);
            Spots(body, false, 0, coat.NapPatches);
            foreach (var x in new[] { 26.0, 31.0, 36.0 })                     // tabby stripes over the curve of the back
                Stroke(new PB().M(x, 31.6 - b).Q(x + 1.6, 36 - b * 0.5, x + 1.6, 33).Build(), stripe, 1.3, true);
            // the tail wraps round the front of the loaf, its tip by the nose
            T(new PB().M(41, 41).Q(24, 48.2, 41, 49.5).Q(15.5, 46.2, 18, 48.6), 4.2, fur);
            F(Oval(7.5, 43.5, 8.5, 4.6), cream);                              // front paws, the chin rests on them
            F(Oval(13.5, 44.2, 8, 4.2), cream);
            // ears, then the tucked head (low, resting on the paws)
            var earL = new PB().M(5, 34).Q(5.5, 25.5, 3.6, 28.5).Q(12.5, 29.5, 9, 26).Close();
            var earR = new PB().M(17, 29).Q(23.5, 26, 20, 25.6).Q(24.5, 34, 25.6, 29).Close();
            F(earL.Build(), coat.EarL ?? fur); F(earR.Build(), coat.EarR ?? fur);
            var head = Oval(3, 28, 23, 18);
            F(head, fur);
            Spots(head, true, 0, coat.NapPatches.Select(t => (t.Color, new Rect(t.Rect.X + 1, t.Rect.Y + 2.5, t.Rect.Width, t.Rect.Height))).ToList());
            foreach (var (x0, x1) in new[] { (12.5, 12.8), (15.3, 15.3) })
                Stroke(new PB().M(x0, 29).L(x1, 32).Build(), stripe, 1.3, true);
            Fill(Oval(9, 38.6, 5.6, 4), cream); Fill(Oval(13.4, 38.6, 5.6, 4), cream);
            Fill(Oval(4.6, 37.6, 3.6, 2), A(Pink, 0.65)); Fill(Oval(20.6, 37.6, 3.6, 2), A(Pink, 0.65));
            Fill(new PB().M(13, 38).L(15, 38).L(14, 39.2).Close().Build(), Pink);
            var eyes = new PB().M(6.6, 35).Q(11, 35, 8.8, 37);
            if (!p.NapPeek) eyes.M(17, 35).Q(21.4, 35, 19.2, 37);
            Stroke(eyes.Build(), eye, 1.2, true);
            if (p.NapPeek)                                                    // one sleepy eye peeks open
            {
                Fill(Oval(17.2, 33.6, 4.2, 3.2), eye);
                Fill(Oval(18.4, 34, 1.4, 1.4), White);
            }
        }

        /// Round reading glasses over both eyes (the billing cat at work), arms to the edge of the head.
        void Glasses(double dx)
        {
            var g = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var cx in new[] { 17.5, 28.5 }) g.Children.Add(Oval(cx - 5.1 + dx, 13.9, 10.2, 10.2));
            g.Children.Add(new PB().M(22.6 + dx, 18.4).Q(23.4 + dx, 18.4, 23 + dx, 17.6).M(12.4 + dx, 18.2).L(8.6 + dx, 17.2)
                .M(33.6 + dx, 18.2).L(37.4 + dx, 17.2).Build());
            Fill(Oval(12.4 + dx, 13.9, 10.2, 10.2), A(White, 0.18));
            Fill(Oval(23.4 + dx, 13.9, 10.2, 10.2), A(White, 0.18));
            Stroke(g, line, 1.2, true);
        }

        /// Calico spots, painted inside a fur shape (head or body) so they follow its edge.
        void Spots(Geometry clip, bool head, double dx, List<(Color Color, Rect Rect)>? list)
        {
            var ps = (list ?? coat.Patches).Where(t => head ? t.Rect.Top < 26 : t.Rect.Top >= 26).ToList();
            if (ps.Count == 0) return;
            dc.PushClip(clip);
            foreach (var (c, r) in ps) Fill(Oval(r.Left + dx, r.Top, r.Width, r.Height), c);
            dc.Pop();
        }

        /// The lazy nap: melted flat on its side, head resting on its paws, eyes shut, back legs stretched out behind.
        void Melt()
        {
            double breathe = p.BreatheY * 0.15;
            T(new PB().M(38.5, 46.5).Q(45, 46, 42.5, 49.5), 3.2, fur);
            F(Oval(32, 45, 11, 4.4), fur);                                    // back legs, stretched out
            F(Oval(8.5, 37.5 - breathe, 32, 12 + breathe), fur);              // the melted body (breathing)
            Spots(Oval(8.5, 37.5, 32, 12), false, 0, coat.NapPatches);
            Fill(Oval(14, 46, 22, 3), cream);
            foreach (var x in new[] { 20.0, 25.0, 30.0, 35.0 })
                Stroke(new PB().M(x, 38.6).Q(x - 0.6, 42.4, x + 0.8, 40.6).Build(), stripe, 1.3, true);
            F(Oval(1, 46, 9, 3.6), fur);                                      // front paws, the head rests on them
            F(Oval(7.5, 46.6, 9, 3.2), fur);
            var earL = new PB().M(3, 36.5).L(3.2, 28.2).L(10, 32.6).Close();
            var earR = new PB().M(13.5, 32.2).L(19.5, 28).L(20, 36).Close();
            F(earL.Build(), coat.EarL ?? fur); F(earR.Build(), coat.EarR ?? fur);
            F(Oval(1.5, 29.5, 20, 16.5), fur);                                // the head, resting
            Spots(Oval(1.5, 29.5, 20, 16.5), true, 0, coat.NapPatches);
            foreach (var (x0, x1) in new[] { (10.0, 10.3), (12.8, 12.8) })
                Stroke(new PB().M(x0, 30.4).L(x1, 33).Build(), stripe, 1.2, true);
            Fill(Oval(7.8, 39.6, 5, 3.6), cream); Fill(Oval(11.4, 39.6, 5, 3.6), cream);
            Fill(Oval(2.8, 39, 3.2, 1.7), A(Pink, 0.7)); Fill(Oval(17.2, 39, 3.2, 1.7), A(Pink, 0.7));
            Stroke(new PB().M(5.2, 36.4).Q(9.2, 36.4, 7.2, 38.2).M(13.8, 36.4).Q(17.8, 36.4, 15.8, 38.2).Build(), eye, 1.1, true);
            Fill(new PB().M(10.6, 39).L(12.4, 39).L(11.5, 40).Close().Build(), Pink);
        }

        // ---- eyes -------------------------------------------------------------------------------------------------
        // only: 0 = left eye only, 1 = right eye only, -1 = both
        void OpenEyes(Vector pupil, bool half, double dx, int only = -1, bool wide = false)
        {
            double w = wide ? 5.8 : 5.0;
            double lx = 17.5 - w / 2 + dx, rx = 28.5 - w / 2 + dx;
            double h = wide ? 6.6 : (half ? 3.0 : 5.6);
            double y = wide ? 15.4 : (half ? 17.8 : 16.2);
            if (only == -1 || only == 0)
            {
                Fill(Oval(lx, y, w, h), eye);
                Fill(Oval(lx + 1.4 + pupil.X, y + 0.8 + pupil.Y, 1.9, 1.9), White);
            }
            if (only == -1 || only == 1)
            {
                Fill(Oval(rx, y, w, h), eye);
                Fill(Oval(rx + 1.4 + pupil.X, y + 0.8 + pupil.Y, 1.9, 1.9), White);
            }
        }
        void ShutEyes(double dx, int only = -1)
        {
            if (only == -1 || only == 0) Stroke(new PB().M(15 + dx, 19.5).Q(20 + dx, 19.5, 17.5 + dx, 21.3).Build(), eye, 1.3);
            if (only == -1 || only == 1) Stroke(new PB().M(26 + dx, 19.5).Q(31 + dx, 19.5, 28.5 + dx, 21.3).Build(), eye, 1.3);
        }
        /// Petted: content "^ ^" eyes.
        void HappyEyes(double dx) =>
            Stroke(new PB().M(15 + dx, 19.8).Q(20 + dx, 19.8, 17.5 + dx, 16.4).M(26 + dx, 19.8).Q(31 + dx, 19.8, 28.5 + dx, 16.4).Build(), eye, 1.4, true);
        void YawnMouth(double dx, double m)
        {
            double w = 5.2 * (0.55 + 0.45 * m), h = 6.5 * m;                  // at 1 it is the old wide-open mouth
            Fill(Oval(23 - w / 2 + dx, 24.5, w, h), CatCoat.C(0.35, 0.16, 0.18));
            Fill(Oval(23 - 1.4 * m + dx, 24.5 + h - 3 * m, 2.8 * m, 2 * m), Pink);
        }

        // ---- side-on poses ----------------------------------------------------------------------------------------
        /// The classic stretch, SIDE-ON (facing right, like the leap): forelegs flat forward on the ground, chest low, head
        /// lowered between them, rear end raised, tail straight up, eyes shut.
        void Bow()
        {
            void Ln(double ax, double ay, double bx, double by, double w, Color c)
            {
                var path = new PB().M(ax, ay).L(bx, by);
                if (w >= 2) T(path, w, c); else Stroke(path.Build(), c, w, true);
            }
            // Tail straight up from the raised rump
            T(new PB().M(9, 32).Q(4.5 + p.TailSway, 11, 3.5 + p.TailSway * 0.5, 24), 3.2, fur);
            // Far legs (shaded) first
            Ln(14.5, 37, 14, 47.4, 3.2, shade);
            Ln(28, 43, 38, 46.6, 3.2, shade);
            Fill(Oval(11.8, 45.9, 4.8, 3.2), cream);
            Fill(Oval(36.4, 45.9, 4.4, 3.2), cream);
            // Body: a capsule from the raised hips down to the low chest, cream belly, stripes
            T(new PB().M(13, 33).Q(27, 41.5, 22, 33.5), 11, fur);
            Stroke(new PB().M(15, 38.2).Q(26, 45.4, 21, 40.4).Build(), cream, 3.4, true);
            foreach (var (ax, ay, bx, by) in new[] { (18.7, 28.7, 17.9, 31.4), (23.7, 31.0, 22.3, 33.4), (28.0, 34.3, 26.0, 36.3) })
                Ln(ax, ay, bx, by, 1.3, stripe);
            // Near legs: hind under the raised rump, fore flat forward on the ground
            Ln(11.5, 36.5, 10.5, 47.4, 3.4, fur);
            Ln(26, 44, 40.2, 47.4, 3.4, fur);
            Fill(Oval(7.8, 45.9, 5.0, 3.2), cream);
            Fill(Oval(38.4, 45.9, 4.8, 3.2), cream);
            // Head lowered at the front, ears up, eyes shut in bliss
            F(new PB().M(28.5, 35).L(27, 28).L(33, 33.5).Close().Build(), shade);
            F(new PB().M(33, 34).L(32.5, 27).L(37.5, 33.5).Close().Build(), fur);
            Fill(new PB().M(33.8, 32.8).L(33.5, 29.2).L(36, 32.6).Close().Build(), Pink);
            F(Oval(24.5, 33.5, 15, 12.5), fur);
            Fill(Oval(33, 39.5, 7, 5.5), cream);
            Fill(new PB().M(39, 40.3).L(40.8, 40.3).L(39.9, 41.6).Close().Build(), Pink);
            Stroke(new PB().M(31.2, 38.4).Q(34.6, 38.4, 32.9, 40).M(36.2, 38.3).Q(38.4, 38.3, 37.3, 39.5).Build(), eye, 1.2, true);
            Stroke(new PB().M(36.5, 42.4).L(42.4, 41.2).M(36.5, 43.4).L(42.4, 44.4).Build(), A(eye, 0.45), 0.6);
            Fill(Oval(29.4, 41, 3, 1.8), A(Pink, 0.5));
            // Collar at the neck
            Ln(24.6, 36.6, 25.6, 44.2, 2.2, Collar);
            Fill(Oval(24, 43.6, 3.2, 3.2), Gold);
        }

        /// Jump mid-air: the tabby stretched long and LEVEL, front legs reaching forward, back legs trailing, tail streaming
        /// up-back, ears back.
        void Leap()
        {
            // Tail streams up-back
            T(new PB().M(12, 40).Q(2, 24, 4, 38), 3, fur);
            // Back legs trail behind
            T(new PB().M(14, 42).L(5, 46), 3, fur);
            T(new PB().M(16, 44).L(8, 49), 3, fur);
            Fill(Oval(3, 44.8, 4.4, 3.2), cream);
            Fill(Oval(6, 47.8, 4.4, 3.2), cream);
            // Long level body + belly
            F(Oval(8.5, 31.5, 27, 15), fur);
            Fill(Oval(12.5, 40.5, 19, 5.5), cream);
            foreach (var x in new[] { 17.0, 21.0, 25.0 })
                Stroke(new PB().M(x, 32.4).L(x - 0.8, 36.4).Build(), stripe, 1.3, true);
            // Front legs reach forward
            T(new PB().M(30, 38).L(40, 34), 3, fur);
            T(new PB().M(30, 41).L(40, 39), 3, fur);
            Fill(Oval(38, 32.5, 4.4, 3.2), cream);
            Fill(Oval(38, 37.5, 4.4, 3.2), cream);
            // Head at the front, ears swept back
            F(new PB().M(29, 32).L(25.5, 26).L(31, 27.5).Close().Build(), shade);
            F(new PB().M(32, 31).L(29.5, 24.5).L(34.5, 27).Close().Build(), fur);
            F(Oval(28, 27, 15, 12), fur);
            Fill(Oval(35.5, 32.5, 7, 5.5), cream);
            Fill(new PB().M(41, 33.5).L(42.8, 33.5).L(41.9, 34.8).Close().Build(), Pink);
            // Eyes wide open mid-leap
            Fill(Oval(33.5, 29.5, 3.2, 3.8), eye);
            Fill(Oval(34.6, 30.1, 1.1, 1.1), White);
            Fill(Oval(37.5, 29.5, 2.6, 3.2), eye);
            // Collar + tag at the neck
            Stroke(new PB().M(29.5, 37).L(31.5, 41).Build(), Collar, 2, true);
            Fill(Oval(30.2, 40.2, 3.2, 3.2), Gold);
        }
    }
}
