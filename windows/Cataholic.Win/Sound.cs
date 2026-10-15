using System;
using System.Collections.Generic;
using System.Media;
using System.Reflection;

namespace Cataholic;

// CatSound — the cat's voice (owner 27 Sep 2026: "can we make the cat do sounds?" → only when you interact). Real
// recordings from BigSoundBank, all CC0 (Sounds/CREDITS.md). The cat is silent on its own: sounds play only from a click,
// a menu pick, a double-click or a drag — never from the random life actions. Silent in Sleep mode and with "Sounds"
// switched off (the menu). The WAVs are embedded resources Sounds.<name>.wav.
static class CatSound
{
    public static bool Enabled => !CatPrefs.Quiet && !Fx.Snapshot && IsOn;
    public static bool IsOn => Prefs.Bool("crewSounds", true);
    public static void SetEnabled(bool on)
    {
        Prefs.Set("crewSounds", on);
        if (!on) PurrStop();
    }

    static readonly Dictionary<string, SoundPlayer?> players = new();
    static SoundPlayer? Player(string name)
    {
        if (players.TryGetValue(name, out var p)) return p;
        try
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Sounds.{name}.wav");
            p = s == null ? null : new SoundPlayer(s);
            p?.Load();
        }
        catch { p = null; }                                   // no audio device / unreadable: the cat is silent
        players[name] = p;
        return p;
    }
    static void Play(string name)
    {
        if (!Enabled) return;
        try { Player(name)?.Play(); } catch { }
    }

    /// A short meow (one of three recordings, never the same twice in a row).
    static string lastMeow = "";
    public static void Meow()
    {
        var pool = new List<string>();
        foreach (var n in new[] { "meow1", "meow2", "meow3" }) if (n != lastMeow) pool.Add(n);
        string pick = pool[Rnd.R.Next(pool.Count)];
        lastMeow = pick;
        Play(pick);
    }
    /// "Hi": a brighter, pleading meow.
    public static void Hi() => Play("hi");

    /// A soft purr that loops while you hold the cat (no volume control or fade in SoundPlayer: it just starts and stops).
    static bool purring;
    public static void PurrStart()
    {
        if (!Enabled || purring) return;
        try { Player("purr")?.PlayLooping(); purring = true; } catch { }
    }
    public static void PurrStop()
    {
        if (!purring) return;
        purring = false;
        try { Player("purr")?.Stop(); } catch { }
    }

    /// The sound for an action picked from the menu.
    public static void ForAction(CatModel.Action a)
    {
        switch (a)
        {
            case CatModel.Action.Hi: Hi(); break;
            case CatModel.Action.Nap: break;                  // napping is quiet
            default: Meow(); break;
        }
    }
}
