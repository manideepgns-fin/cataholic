using System;
using System.IO;
using System.Windows;

namespace Cataholic;

// `--scenario find`: the Find my cat proof. One timeline on the cloud desktop (seed 17, Ginger — the same layouts the
// Mac oracle renders for the side-by-side), with real mouse input where it matters:
//   1.5 s   a real double-click on the cat opens the game (level 1)            → find/L1.png     (shot 3.5 s)
//   4–10 s  levels 2, 3, 4, 5 (the pointer moved onto the hidden cat: lantern) → find/L2…L5.png
//   13 s    a real click far from her: a paw print and "So cold"               → find/miss.png
//   15 s    a real click on her: the pop and the wave, then settled + banner   → find/found.png, found-settled.png
//   20 s    zoomed 2.5× on her, then a real click-drag pans                    → find/zoom.png, pan.png
//   25–31 s nothing scheduled: the CPU log between the 26 s and 30 s shots is "nothing animates while you look"
//   31 s    three misses: the spotlight                                         → find/lost.png
//   34 s    Esc/close frees it (trace: open=False)
// `find/*.png` are the game window drawn off-screen at its own size (1100×704), so they do not depend on the 1024×768
// runner screen; the ci-run screenshots show the real window on the desktop.
static class FindScenario
{
    public static void Prepare()
    {
        Prefs.Set("findMaxLevel", 6.0);                 // every level in the picker
        FindMyCat.TestLevel = 1; FindMyCat.TestSeed = 17;
    }

    const ulong Seed = 17;
    static FindWindow? W => FindMyCat.Window;

    public static void Run(string? tracePath)
    {
        string? dir = tracePath != null ? Path.GetDirectoryName(tracePath) : null;
        void Snap(string name) { if (dir != null) W?.SavePng(Path.Combine(dir, name + ".png")); }
        void Say(string what)
        {
            var w = W;
            Scenario.Trace(w == null ? $"find {what}: closed" :
                $"find {what}: level={w.Game.Round.Level} place={w.Game.Round.Place} phase={w.Game.Phase} guesses={w.Game.Guesses.Count} cats={w.Game.Cats.Count} z={w.Camera.Z:0.0} open={FindMyCat.IsOpen}");
        }
        void Play(int level) { if (W is FindWindow w) { w.Camera.Reset(); w.Game.Play(level, Seed); } }
        void Click(Point scene)
        {
            if (W is not FindWindow w) return;
            var px = w.ScreenPixelOfScene(scene);
            Win32.SetCursorPos((int)Math.Round(px.X), (int)Math.Round(px.Y));
            Later.After(0.05, () => Scenario.Click(false));
        }
        Point Hidden() => W!.Game.Center;
        void At(double t, Action a) => Scenario.At(t, a);

        // a real double-click on the cat (CatView → FindMyCat.Open)
        At(1.5, () => { Scenario.MoveTo(CatPanel.CatCenter() ?? new Point(0, 0)); Scenario.Click(false); Later.After(0.2, () => Scenario.Click(false)); });
        At(2.0, () => { if (W is FindWindow w) { w.Topmost = true; w.Topmost = false; w.Activate(); } Say("opened"); });
        At(3.0, () => Snap("L1"));
        for (int i = 0; i < 3; i++)
        {
            int level = i + 2;
            At(4.0 + 2 * i, () => Play(level));
            At(5.0 + 2 * i, () => { Snap("L" + level); Say("level " + level); });
        }
        At(10.0, () => Play(5));
        At(10.4, () => { if (W is FindWindow w) { var px = w.ScreenPixelOfScene(Hidden()); Win32.SetCursorPos((int)Math.Round(px.X), (int)Math.Round(px.Y)); } });
        At(11.3, () => { Snap("L5"); Say("level 5, lantern on her"); });
        At(12.5, () => Play(1));
        At(13.0, () => Click(new Point(1040, 90)));
        At(14.0, () => { Snap("miss"); Say("one miss"); });
        At(15.0, () => Play(1));
        At(15.5, () => Click(Hidden()));
        At(16.2, () => { Snap("found"); Say("found, waving"); });
        At(19.0, () => { Snap("found-settled"); Say("found, settled"); });
        At(20.0, () => Play(1));
        At(20.2, () => { if (W is FindWindow w) w.Camera.Zoom(2.5, w.Camera.ToViewport(Hidden()), animated: true); });
        At(21.2, () => { Snap("zoom"); Say("zoomed"); });
        At(22.0, () =>
        {
            if (W is not FindWindow w) return;
            var px = w.ScreenPixelOfScene(new Point(550, 320));
            Win32.SetCursorPos((int)Math.Round(px.X), (int)Math.Round(px.Y));
            Later.After(0.05, () => Scenario.Mouse(Win32.MOUSEEVENTF_LEFTDOWN));
            Later.After(0.3, () => Scenario.Drag(new Point(px.X / Display.Scale - 220, px.Y / Display.Scale - 110), 0.7));
            Later.After(1.2, () => Scenario.Mouse(Win32.MOUSEEVENTF_LEFTUP));
        });
        At(24.0, () => { Snap("pan"); Say("panned"); });
        At(25.0, () => Play(1));
        At(26.0, () => Say("idle begins"));
        At(30.0, () => Say("idle ends"));
        At(31.0, () => Play(1));
        for (int i = 0; i < 3; i++)
        {
            var spots = new[] { new Point(1040, 90), new Point(1080, 120), new Point(980, 70) };
            int k = i;
            At(31.3 + 0.1 * i, () => W?.Game.Guess(spots[k]));
        }
        At(32.5, () => { Snap("lost"); Say("three misses"); });
        At(34.0, () => W?.Close());
        At(34.5, () => Say("after Esc/close"));
    }
}
