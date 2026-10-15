using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cataholic;

// `--render-coats <file.png>`: every coat sitting (top row) then napping (bottom row), like CataholicRender.coats
// in Sources/Cataholic/Cataholic.swift (docs/cats.png is the Mac's output — compare side by side).
// `--render-poses <file.png>`: Ginger, Patches and Tux (one row each) in 30 poses (one column each) — the same grid the
// Mac reference renders, so the two sheets can be compared cell by cell. Cell 92 × 124 pt, gaps 14 / 6, padding 24, scale 2.
public static class Render
{
    static CatPose Pose(CatState state, Action<CatPose>? set = null)
    {
        var p = new CatPose { State = state };
        set?.Invoke(p);
        return p;
    }

    /// The pose columns, in order (the same list as MacPoses.swift in the Mac reference).
    public static readonly (string Name, Func<CatPose> Make)[] PoseList =
    {
        ("look", () => Pose(CatState.Look, p => p.Pin = false)),
        ("look-pin", () => Pose(CatState.Look)),
        ("alert", () => Pose(CatState.Alert)),
        ("clingy", () => Pose(CatState.Clingy, p => { p.HeadTilt = 6; p.BodyTilt = 8; })),
        ("needs-3", () => Pose(CatState.Needs, p => p.Badge = "3")),
        ("needs-12", () => Pose(CatState.Needs, p => p.Badge = "12")),
        ("working", () => Pose(CatState.Working)),
        ("hi-wave", () => Pose(CatState.Hi, p => { p.PawRaise = 1; p.PawWave = 20; p.PawWrist = 10; })),
        ("groom", () => Pose(CatState.Groom)),
        ("yawn", () => Pose(CatState.Yawn, p => p.MouthOpen = 1)),
        ("big", () => Pose(CatState.Big)),
        ("jump-crouch", () => Pose(CatState.Jump, p => p.JumpCrouch = true)),
        ("jump-leap", () => Pose(CatState.Jump)),
        ("stretch", () => Pose(CatState.Stretch)),
        ("nap-curl", () => Pose(CatState.Nap, p => { p.Pin = false; p.NapBreath = 0.5; p.ZRise = 6; p.ZOpacity = 1; })),
        ("nap-peek", () => Pose(CatState.Nap, p => { p.Pin = false; p.NapBreath = 0; p.NapPeek = true; p.ZRise = 8; p.ZOpacity = 0.7; })),
        ("nap-pin", () => Pose(CatState.Nap, p => { p.ZRise = 6; p.ZOpacity = 1; })),
        ("sleep-melt", () => Pose(CatState.Sleeping, p => { p.Pin = false; p.BreatheY = 2; p.ZRise = 4; p.ZOpacity = 0.8; })),
        ("sleep-pin", () => Pose(CatState.Sleeping, p => { p.ZRise = 4; p.ZOpacity = 0.8; })),
        ("run-walk", () => Pose(CatState.Running, p => { p.RunPhase = 1; p.Stride = 22; })),
        ("run-gallop", () => Pose(CatState.Running, p => { p.RunPhase = 1; p.RunSpeed = 1; })),
        ("run-skid", () => Pose(CatState.Running, p => { p.RunPhase = 1; p.RunSpeed = 1; p.RunGait = 2; })),
        ("run-scramble", () => Pose(CatState.Running, p => { p.RunPhase = 2; p.RunGait = 1; })),
        ("run-stand", () => Pose(CatState.Running, p => p.RunPhase = 0)),
        ("happy", () => Pose(CatState.Look, p => p.Happy = true)),
        ("eyes-half", () => Pose(CatState.Look, p => p.Eyes = CatEyes.Half)),
        ("eyes-wide", () => Pose(CatState.Look, p => p.Eyes = CatEyes.Wide)),
        ("blink", () => Pose(CatState.Look, p => p.BlinkShut = true)),
        ("tweaks", () => Pose(CatState.Look, p =>
        {
            p.TailSway = 2.5; p.EarTwitch = 1; p.Pupil = new Vector(1, -0.5); p.HeadTurn = 3; p.HeadTilt = 6; p.EarsBack = 0.7;
            p.CollarTint = CatCoat.C(0.2, 0.7, 0.3); p.TailTip = CatCoat.C(0.9, 0.2, 0.2); p.CollarBold = 1.6;
        })),
        ("no-bell", () => Pose(CatState.Look, p => p.Bell = false)),
    };

    public static void Poses(string path) => Sheet(() => PosesSheet(path));
    static void PosesSheet(string path)
    {
        const double w = 92, h = 124, gapX = 14, gapY = 6, pad = 24, scale = 2;
        var coats = new[] { CatCoat.Ginger, CatCoat.PatchesCoat, CatCoat.Tux };
        int n = PoseList.Length;
        double W = pad * 2 + n * w + (n - 1) * gapX, H = pad * 2 + coats.Length * h + (coats.Length - 1) * gapY;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(CatCoat.C(0.13, 0.13, 0.15)), null, new Rect(0, 0, W, H));
            for (int row = 0; row < coats.Length; row++)
                for (int i = 0; i < n; i++)
                {
                    dc.PushTransform(new TranslateTransform(pad + i * (w + gapX), pad + row * (h + gapY)));
                    dc.PushTransform(new ScaleTransform(2, 2));
                    var p = PoseList[i].Make();
                    p.CoatOverride = coats[row];
                    CatDrawing.Draw(dc, p);
                    dc.Pop(); dc.Pop();
                }
        }
        Save(dv, W, H, scale, path);
    }

    // The Mac's sheets are ImageRenderer output, which does not crop the cat to its box (see CatDrawing.CropToBox).
    static void Sheet(Action draw) { var was = CatDrawing.CropToBox; CatDrawing.CropToBox = false; try { draw(); } finally { CatDrawing.CropToBox = was; } }

    static void Save(DrawingVisual dv, double W, double H, double scale, string path)
    {
        var bmp = new RenderTargetBitmap((int)(W * scale), (int)(H * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = System.IO.File.Create(path);
        enc.Save(f);
    }

    public static void Coats(string path) => Sheet(() => CoatsSheet(path));
    static void CoatsSheet(string path)
    {
        const double zoom = 2, scale = 2, w = 46 * zoom, h = 62 * zoom, gap = 14, pad = 24;
        int n = CatCoat.All.Count;
        double W = pad * 2 + n * w + (n - 1) * gap, H = pad * 2 + h * 2 - 40;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(CatCoat.C(0.13, 0.13, 0.15)), null, new Rect(0, 0, W, H));
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < n; i++)
                {
                    dc.PushTransform(new TranslateTransform(pad + i * (w + gap), pad + row * (h - 40)));
                    dc.PushTransform(new ScaleTransform(zoom, zoom));
                    CatDrawing.Draw(dc, new CatPose
                    {
                        State = row == 0 ? CatState.Look : CatState.Sleeping, Pin = false, CoatOverride = CatCoat.All[i],
                    });
                    dc.Pop(); dc.Pop();
                }
        }
        Save(dv, W, H, scale, path);
    }
}
