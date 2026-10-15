using System;
using System.Collections.Generic;

namespace Cataholic;

/// SplitMix64 — a seeded layout, so a round can be drawn again exactly (and the probe can render one). The same
/// generator as the Mac's `FindRandom`, and the helpers below follow Swift's standard library step for step
/// (`Double.random(in:using:)`, `Int.random(in:using:)`, `Bool.random(using:)`, `shuffled(using:)`,
/// `randomElement(using:)`), so a seed gives the Mac's layout. No WPF in here: windows/scripts/… can run it anywhere.
sealed class FindRandom
{
    ulong s;
    public FindRandom(ulong seed) { s = seed; }

    public ulong Next()
    {
        unchecked
        {
            s += 0x9E3779B97F4A7C15UL;
            ulong z = s;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// RandomNumberGenerator.next(upperBound:) — Lemire's multiply-and-reject, 0 ..< upper.
    public ulong Next(ulong upper)
    {
        ulong r = Next();
        ulong hi = Math.BigMul(r, upper, out ulong lo);
        if (lo < upper)
        {
            ulong t = unchecked(0UL - upper) % upper;
            while (lo < t) { r = Next(); hi = Math.BigMul(r, upper, out lo); }
        }
        return hi;
    }

    /// Int.random(in: lo ..< hi)
    public int Int(int lo, int hi) => (int)((ulong)lo + Next((ulong)(hi - lo)));
    /// Int.random(in: lo ... hi)
    public int IntClosed(int lo, int hi) => (int)((ulong)lo + Next((ulong)(hi - lo) + 1));

    const double Unit = 1.0 / 9007199254740992.0;           // ulpOfOne / 2 = 2^-53
    /// Double.random(in: lo ..< hi): 53 random bits.
    public double Double(double lo, double hi)
    {
        double delta = hi - lo;
        while (true)
        {
            ulong rand = Next() & ((1UL << 53) - 1);
            double v = delta * (rand * Unit) + lo;
            if (v != hi) return v;
        }
    }
    /// Double.random(in: lo ... hi) (also CGFloat's): 0 … 2^53 inclusive, so the top of the range can be reached.
    public double DoubleClosed(double lo, double hi)
    {
        double delta = hi - lo;
        ulong rand = Next((1UL << 53) + 1);
        return delta * (rand * Unit) + lo;
    }

    /// Bool.random(using:)
    public bool Bool() => ((Next() >> 17) & 1) == 0;

    /// collection.randomElement(using:)! — an empty collection draws nothing.
    public T Pick<T>(IReadOnlyList<T> c) => c[Int(0, c.Count)];
    public bool TryPick<T>(IReadOnlyList<T> c, out T item)
    {
        if (c.Count == 0) { item = default!; return false; }
        item = c[Int(0, c.Count)]; return true;
    }

    /// collection.shuffled(using:) — Fisher–Yates from the front.
    public List<T> Shuffled<T>(IEnumerable<T> src)
    {
        var a = new List<T>(src);
        if (a.Count < 2) return a;
        int amount = a.Count, cur = 0;
        while (amount > 1)
        {
            int random = Int(0, amount);
            amount--;
            (a[cur], a[cur + random]) = (a[cur + random], a[cur]);
            cur++;
        }
        return a;
    }
}
