using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Cataholic;

// `--scenario <name>`: set the cat up in a known state and drive her, so a screenshot proves a behaviour (the cloud test
// takes them; see windows/scripts/ci-run.ps1 and the workflow). Scenarios keep prefs in memory (Prefs.Ephemeral) and
// never touch %APPDATA%. `--trace <file>` writes one line a second: where the cat is, what she is doing.
//   rest · drop · taskbar · zoomies · herd20 · own · menu · sleep · emotes · reduced · idlenap
static class Scenario
{
    public static int Failures;
    static StreamWriter? trace;
    // the cloud test's clock starts when it launches us, but WPF takes a moment to come up: schedule against process start
    static readonly DateTime T0 = Process.GetCurrentProcess().StartTime;
    static void At(double sec, System.Action a) => Later.After(Math.Max(0.01, sec - (DateTime.Now - T0).TotalSeconds), a);

    // Notepad is moved to (25%, 40%, 50% × 35%) of the screen by ci-run.ps1: the window the cat lands on.
    static Rect Notepad => new(Display.Frame.Width * 0.25, Display.Frame.Height * 0.40, Display.Frame.Width * 0.5, Display.Frame.Height * 0.35);

    /// Before the cat window exists: prefs she starts with.
    public static void Prepare(string name)
    {
        Prefs.Ephemeral = true;
        Fx.Snapshot = false;
        Fx.ReducedOverride = name == "reduced";           // the cloud desktop reports Reduce Motion; scenarios pin it
        Win32.IdleOverride = name == "idlenap" ? 400 : 0; // …and that the user is at the PC (or away, for idlenap)
        switch (name)
        {
            case "drop": Stand(Notepad.Left + Notepad.Width / 2 - CatLayout.ScreenW / 2, 90); break;
            case "taskbar": Stand(80, 90); break;
            case "zoomies": case "sleep": case "emotes": Stand(Notepad.Left + Notepad.Width / 2 - CatLayout.ScreenW / 2, Notepad.Top - CatGravity.Feet); break;
            case "own": Stand(Display.Frame.Width * 0.12, Display.Visible.Bottom - CatGravity.Feet); Prefs.Set("crewOnHerOwn", true); break;
            case "herd20": Prefs.Set("crewHerd", 20.0); break;
            case "reduced": Stand(Notepad.Left + Notepad.Width / 2 - CatLayout.ScreenW / 2, Notepad.Top - CatGravity.Feet); Prefs.Set("crewHerd", 10.0); break;
        }
        if (name == "sleep") Prefs.Set("crewQuiet", true);
    }
    static void Stand(double left, double top)
    {
        Prefs.Set("crewHang", false); Prefs.Set("crewLeft", left); Prefs.Set("crewTop", top);
    }

    /// After the cat window is up.
    public static void Run(string name, string? tracePath)
    {
        if (tracePath != null) { try { trace = new StreamWriter(tracePath, true) { AutoFlush = true }; } catch { } }
        CatOwn.Log = s => Trace("own " + s);
        Later.Every(1.0, _ => Trace($"t={(DateTime.Now - T0).TotalSeconds:0.0}"));
        switch (name)
        {
            case "drop": case "taskbar":
                At(1.5, () => CatPanel.Drop());
                break;
            case "zoomies":
                At(1.5, CatPanel.Zoomies);
                break;
            case "reduced":                      // Reduce Motion: a still pose, no zoomies, no herd, no flight
                At(1.5, () => CatPanel.CurrentModel?.StartAction(CatModel.Action.Hi, user: true));
                At(4, CatPanel.Zoomies);
                break;
            case "menu":
                At(1.5, () => CatMenuPanel.Show(new Point(Display.Frame.Width * 0.55, 30)));
                break;
            case "emotes":
            {
                // each emote in turn, one every 3 s from 1.1 s (the nap is last and runs on); the cloud test shoots 0.9 s into each
                // (t = 2, 5, 8, … 23) and 2.9 s into the nap (t = 28) — see the workflow
                double at = 1.1;
                foreach (var (a, _) in CatModel.RingActions)
                {
                    var aa = a;
                    At(at, () => { Trace("emote " + aa); CatPanel.CurrentModel?.StartAction(aa, user: true); });
                    at += 3;
                }
                break;
            }
        }
    }

    static void Trace(string msg)
    {
        if (trace == null) return;
        var m = CatPanel.CurrentModel;
        if (msg.StartsWith("t=") && m != null)
        {
            var r = CatPanel.CharScreenRect();
            var fl = r is Rect rr ? CatGravity.FloorBelow(rr.Left + rr.Width / 2, rr.Top + CatGravity.Feet - 3, CatGravity.Windows()) : default;
            msg += $" box={(r is Rect b ? $"{b.Left:0},{b.Top:0}" : "-")} feet={(r is Rect f ? $"{f.Top + CatGravity.Feet:0}" : "-")} floor={fl.Y:0}/{(fl.Window?.ToString() ?? "taskbar")}"
                 + $" hang={CatPanel.Hanging} air={m.Airborne} act={m.Act} run={m.RunFacing} zoom={CatPanel.Zooming} busy={CatPanel.Busy}"
                 + $" herd={CatHerd.Shared.Cats.Count} idle={Win32.IdleSeconds:0}";
        }
        trace.WriteLine(msg);
    }
}
