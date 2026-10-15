using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Cataholic;

// Auto-update (Cataholic.swift Updates / UpdateDelegate; the Mac uses Sparkle, this is our own small class — no NuGet).
// The Windows app is released as `win-v<version>` on the same Releases page as the Mac app and is never marked Latest
// (the Mac's feed is releases/latest). So the feed is the releases list, filtered to `win-v*` tags.
//   check (60 s after start, then daily, or the menu's "Check for updates")
//   → newest non-draft, non-prerelease `win-v*` release with a higher version than this exe
//   → download Cataholic-win-x64.zip + .sha256 into %LOCALAPPDATA%\Cataholic\update, check the hash, unzip,
//     check the new exe's own version → swap: Windows lets a running exe be renamed, so Cataholic.exe → Cataholic.exe.old,
//     the new one takes its name → start it with `--after-update <pid>` and quit. Like the Mac, a found update installs AT ONCE.
// Any mismatch or error stops the update with nothing replaced; the user sees nothing (a manual check says so in a balloon).
static class Updates
{
    const string Feed = "https://api.github.com/repos/manideepgns-fin/cataholic/releases?per_page=30";
    const string ZipName = "Cataholic-win-x64.zip", ExeName = "Cataholic.exe", TagPrefix = "win-v";

    /// `--update-feed <url>`: scenarios only (App.cs ignores it otherwise), so CI can serve a feed of its own.
    public static string? FeedOverride;
    /// `--after-update <pid>` was given: this start is the relaunch after an update.
    public static bool AfterUpdate;
    /// Where the log goes (the scenario trace); stderr always.
    public static Action<string>? Log;

    static readonly HttpClient http = MakeHttp();
    static bool busy;
    static string UpdateDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cataholic", "update");

    static HttpClient MakeHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Cataholic-for-Windows/" + Current);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// This exe's version as major.minor.build (the csproj `<Version>`).
    static Version Current
    {
        get { var v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v?.Major ?? 0, v?.Minor ?? 0, Math.Max(v?.Build ?? 0, 0)); }
    }

    /// At app start: tidy up after an update, then schedule the checks. Scenarios other than `update` never touch the network.
    public static void Start(string? scenario)
    {
        CleanLeftovers();
        if (AfterUpdate) Say($"after update: now running {Current}");
        if (scenario != null && scenario != "update") return;
        double first = 60;
        if (scenario == "update") first = 3;
        else
        {
            double since = Now() - Prefs.Double("lastUpdateCheck");              // a cat restarted within the hour doesn't ask GitHub again
            if (since >= 0 && since < 3600) first = Math.Max(first, 3600 - since);
        }
        Later.After(first, () =>
        {
            Check();
            if (scenario == null) Later.Every(24 * 3600, _ => Check());
        });
    }

    /// Look for an update now (the menu's button passes manual: the user then hears the answer). Everything runs off the UI thread.
    public static void Check(bool manual = false)
    {
        if (busy) { if (manual) Notify("Cataholic is already looking for updates."); return; }
        busy = true;
        Ui(() => Prefs.Set("lastUpdateCheck", Now()));
        Task.Run(async () =>
        {
            try { await Run(manual); }
            catch (Exception e)
            {
                Say("failed: " + e.GetType().Name + ": " + e.Message);
                if (manual) Notify("Couldn't check for updates right now.");
            }
            finally { busy = false; }
        });
    }

    sealed record Release(string Tag, Version Version, string ZipUrl, string ShaUrl);

    static async Task Run(bool manual)
    {
        var cur = Current;
        string feed = FeedOverride ?? Feed;
        Say($"check: running {cur}, feed {feed}");
        var rel = await Newest(feed);
        if (rel == null || rel.Version <= cur)
        {
            Say(rel == null ? "no Windows release in the feed" : $"up to date ({rel.Tag} is not newer than {cur})");
            if (manual) Notify($"Cataholic is up to date ({cur}).");
            return;
        }
        Say($"found {rel.Tag}: {rel.Version} > {cur}");

        string exe = Environment.ProcessPath ?? "";
        if (!CanSwap(exe, out string why))
        {
            Say("not updating: " + why);
            if (manual) Notify($"Cataholic {rel.Version} is available, but this copy can't update itself. Download it from the Releases page.");
            return;
        }

        // 1. download both into a clean update folder; only https from the real feed (a test feed may be http)
        if (FeedOverride == null && (!rel.ZipUrl.StartsWith("https://") || !rel.ShaUrl.StartsWith("https://"))) { Say("not updating: asset URLs are not https"); return; }
        string dir = UpdateDir;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        string zip = Path.Combine(dir, ZipName);
        await Download(rel.ZipUrl, zip);
        string shaText = (await http.GetStringAsync(rel.ShaUrl)).Trim();
        Say($"downloaded {ZipName} ({new FileInfo(zip).Length} bytes) and its .sha256");

        // 2. the hash: the .sha256 is hex, maybe followed by a file name ("<hex>  Cataholic-win-x64.zip")
        string want = shaText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        string got;
        using (var fs = File.OpenRead(zip)) got = Convert.ToHexString(SHA256.HashData(fs));
        if (!string.Equals(want, got, StringComparison.OrdinalIgnoreCase))
        {
            Say($"sha256 MISMATCH (feed says {want}, download is {got}); stopping, nothing replaced");
            CleanUpdateDir();
            return;
        }
        Say("sha256 ok " + got);

        // 3. unzip ONLY Cataholic.exe (by name — no entry path is ever used), and check it is the version the tag promises
        string fresh = Path.Combine(dir, ExeName);
        using (var za = ZipFile.OpenRead(zip))
        {
            var entry = za.GetEntry(ExeName);
            if (entry == null) { Say("zip has no " + ExeName + "; stopping"); CleanUpdateDir(); return; }
            entry.ExtractToFile(fresh, true);
        }
        var fv = FileVersionInfo.GetVersionInfo(fresh);
        var reported = new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart);
        if (reported != rel.Version) { Say($"new exe reports {reported}, the tag says {rel.Version}; stopping"); CleanUpdateDir(); return; }
        Say($"new exe reports {reported}");

        Swap(exe, fresh);
        CleanUpdateDir();
        Say("started the new cat; quitting");
        Ui(() => Application.Current.Shutdown());
    }

    /// The newest complete `win-v*` release that is neither a draft nor a prerelease. Mac releases (`v*`) are skipped.
    static async Task<Release?> Newest(string feed)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(feed));
        Release? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            string tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!tag.StartsWith(TagPrefix, StringComparison.Ordinal)) { Say($"skip {tag}: not a Windows release"); continue; }
            if (Flag(r, "draft") || Flag(r, "prerelease")) { Say($"skip {tag}: draft or prerelease"); continue; }
            if (!Version.TryParse(tag[TagPrefix.Length..], out var v) || v.Revision != -1) { Say($"skip {tag}: not a version"); continue; }
            string? zip = null, sha = null;
            if (r.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    if (name == ZipName) zip = url; else if (name == ZipName + ".sha256") sha = url;
                }
            if (zip == null || sha == null) { Say($"skip {tag}: assets missing"); continue; }
            var cand = new Release(tag, new Version(v.Major, v.Minor, Math.Max(v.Build, 0)), zip, sha);
            if (best == null || cand.Version > best.Version) best = cand;
        }
        return best;
    }
    static bool Flag(JsonElement r, string name) => r.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.True;

    static async Task Download(string url, string to)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(to);
        await src.CopyToAsync(dst);
    }

    /// Only a real Cataholic.exe in a folder we can write to swaps itself (not `dotnet run`, not Program Files).
    static bool CanSwap(string exe, out string why)
    {
        why = "";
        if (!Path.GetFileName(exe).Equals(ExeName, StringComparison.OrdinalIgnoreCase)) { why = $"running as {Path.GetFileName(exe)}, not {ExeName}"; return false; }
        try { using (new FileStream(exe + ".probe", FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { } }
        catch (Exception e) { why = $"the folder {Path.GetDirectoryName(exe)} is not writable ({e.GetType().Name})"; return false; }
        return true;
    }

    /// The swap. Copy the new exe next to the old one first (a copy across drives is not atomic, a rename in one folder is),
    /// then rename; if anything fails the old exe is put back, so the folder never ends up with half an update.
    static void Swap(string exe, string fresh)
    {
        string old = exe + ".old", next = exe + ".new";
        File.Copy(fresh, next, true);
        if (new FileInfo(next).Length != new FileInfo(fresh).Length) { File.Delete(next); throw new IOException("copy of the new exe is the wrong size"); }
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);                                              // the running exe may be renamed
        try { File.Move(next, exe); }
        catch { File.Move(old, exe); File.Delete(next); throw; }
        Say($"swapped: {Path.GetFileName(exe)} → {Path.GetFileName(old)}, the new exe is in place");
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            psi.ArgumentList.Add("--after-update"); psi.ArgumentList.Add(Environment.ProcessId.ToString());
            if (FeedOverride != null)                                      // a scenario run carries its flags into the new cat
            {
                var a = Environment.GetCommandLineArgs();
                foreach (var flag in new[] { "--scenario", "--update-feed", "--trace" })
                {
                    int i = Array.IndexOf(a, flag);
                    if (i >= 0 && i + 1 < a.Length) { psi.ArgumentList.Add(flag); psi.ArgumentList.Add(a[i + 1]); }
                }
            }
            Process.Start(psi)?.Dispose();
        }
        catch { File.Delete(exe); File.Move(old, exe); throw; }
    }

    /// `--after-update <pid>`: the new cat waits for the old one to be gone before it takes the single-instance mutex.
    public static void WaitForExit(int pid)
    {
        AfterUpdate = true;
        try { using var p = Process.GetProcessById(pid); p.WaitForExit(20000); } catch { /* already gone */ }
    }

    /// Delete what an update left behind (the old exe, once nothing runs from it any more). Off the UI thread, a few tries.
    static void CleanLeftovers()
    {
        string exe = Environment.ProcessPath ?? "";
        if (!Path.GetFileName(exe).Equals(ExeName, StringComparison.OrdinalIgnoreCase)) return;
        Task.Run(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    foreach (var f in new[] { exe + ".old", exe + ".new" })
                        if (File.Exists(f)) { File.Delete(f); Say("removed " + Path.GetFileName(f)); }
                    return;
                }
                catch { Thread.Sleep(1000); }
            }
        });
        CleanUpdateDir();
    }
    static void CleanUpdateDir() { try { if (Directory.Exists(UpdateDir)) Directory.Delete(UpdateDir, true); } catch { } }

    static double Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    static void Notify(string text) => Ui(() => Shell.Notify(text));
    static void Ui(System.Action a) { try { Application.Current?.Dispatcher.Invoke(a); } catch { } }
    static void Say(string s)
    {
        Console.Error.WriteLine("updates: " + s);
        Ui(() => Log?.Invoke(s));
    }
}
