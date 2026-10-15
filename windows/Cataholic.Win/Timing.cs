using System;
using System.Windows.Threading;

namespace Cataholic;

/// A cancellable one-shot (DispatchWorkItem) or repeating (Timer) job on the UI thread.
sealed class Later
{
    readonly DispatcherTimer t;
    Later(double sec, Action<Later> tick, bool repeat)
    {
        t = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(Math.Max(sec, 0.001)) };
        t.Tick += (_, _) => { if (!repeat) Cancel(); tick(this); };
        t.Start();
    }
    public static Later After(double sec, Action a) => new(sec, _ => a(), false);
    public static Later Every(double sec, Action<Later> a) => new(sec, a, true);
    public void Cancel() => t.Stop();
}

static class Rnd
{
    public static readonly Random R = new();
    public static double In(double a, double b) => a + R.NextDouble() * (b - a);
    public static int In(int a, int b) => R.Next(a, b + 1);            // closed range, like Swift's a...b
    public static bool Bool() => R.Next(2) == 0;
}

/// Motion rules: reduced-motion users get the end states with no animation.
static class Fx
{
    /// Test scenarios pin this (the cloud desktop reports Reduce Motion); null = follow Windows.
    public static bool? ReducedOverride;
    public static bool Reduced => ReducedOverride ?? !System.Windows.SystemParameters.ClientAreaAnimation;
    /// Test scenarios freeze nothing, but they keep prefs in memory and skip sounds.
    public static bool Snapshot = false;
}
