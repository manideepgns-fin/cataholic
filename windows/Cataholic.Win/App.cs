using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// Cataholic for Windows — the app shell. Flags (used by CI and the README image):
//   --render-coats <file.png>   draw every coat, sitting then napping, into one image, then quit
//   --render-poses <file.png>   draw Ginger, Patches and Tux in 30 poses into one image, then quit
//   --quit-after <seconds>      run normally, then quit (the cloud test takes screenshots meanwhile)
//   --scenario <name>           set the cat up in a known state for a screenshot (Scenario.cs); prefs stay in memory
//   --trace <file>              with a scenario: one line a second — where the cat is and what she is doing
//   --check-find [N]            Find my cat: the fairness check over N layouts per level per coat (default 200), then quit
//   --find-level N --find-seed S  Find my cat: the first game starts on this level / layout (tests)
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        // Find my cat: `--check-find [N]` is the fairness self-check (no window; exit 0 only if every layout is fair);
        // `--find-level N` / `--find-seed S` start the first game there (tests)
        if (Array.IndexOf(args, "--check-find") is int cf && cf >= 0) return FindCli.Check(args, cf);
        if (int.TryParse(Arg("--find-level"), out var findLevel)) FindMyCat.TestLevel = findLevel;
        if (ulong.TryParse(Arg("--find-seed"), out var findSeed)) FindMyCat.TestSeed = findSeed;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (Arg("--render-coats") is string png) { Render.Coats(png); return 0; }
        if (Arg("--render-poses") is string poses) { Render.Poses(poses); return 0; }
        if (double.TryParse(Arg("--quit-after"), out var secs))
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
            t.Tick += (_, _) => app.Shutdown();
            t.Start();
        }
        // A cat that hits a bug keeps living; the bug goes to stderr, and a scenario run ends non-zero so CI turns red.
        app.DispatcherUnhandledException += (_, e) =>
        {
            Scenario.Failures++;
            Console.Error.WriteLine(e.Exception);
            e.Handled = true;
        };
        string? scenario = Arg("--scenario"), trace = Arg("--trace");
        if (scenario != null) Prefs.Ephemeral = true;                  // a test run never reads or writes the real prefs
        app.Startup += (_, _) => Shell.Start(scenario, trace);
        app.Exit += (_, _) => Shell.Stop();
        int code = app.Run();
        return code != 0 ? code : (Scenario.Failures > 0 ? 3 : 0);
    }
}
