using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Cataholic;

/// UserDefaults, as one JSON file: %APPDATA%\Cataholic\prefs.json, with the Swift key names. Written a moment after the
/// last change (a drag writes crewLeft/crewTop on every move). Test scenarios run `Ephemeral`: nothing is read or written.
static class Prefs
{
    public static bool Ephemeral;
    static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cataholic", "prefs.json");
    static Dictionary<string, object>? map;
    static Later? saving;

    static Dictionary<string, object> M
    {
        get
        {
            if (map != null) return map;
            map = new();
            if (Ephemeral) return map;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path));
                foreach (var p in doc.RootElement.EnumerateObject())
                    switch (p.Value.ValueKind)
                    {
                        case JsonValueKind.True: map[p.Name] = true; break;
                        case JsonValueKind.False: map[p.Name] = false; break;
                        case JsonValueKind.Number: map[p.Name] = p.Value.GetDouble(); break;
                        case JsonValueKind.String: map[p.Name] = p.Value.GetString() ?? ""; break;
                    }
            }
            catch { /* no file yet, or unreadable: defaults */ }
            return map;
        }
    }

    public static bool Has(string key) => M.ContainsKey(key);
    public static bool Bool(string key, bool def) => M.TryGetValue(key, out var v) && v is bool b ? b : def;
    public static double Double(string key) => M.TryGetValue(key, out var v) && v is double d ? d : 0;
    public static int Int(string key) => (int)Double(key);
    public static string? String(string key) => M.TryGetValue(key, out var v) ? v as string : null;
    public static void Set(string key, bool value) { M[key] = value; Save(); }
    public static void Set(string key, double value) { M[key] = value; Save(); }
    public static void Set(string key, string value) { M[key] = value; Save(); }
    public static void Remove(string key) { if (M.Remove(key)) Save(); }

    static void Save()
    {
        if (Ephemeral) return;
        saving?.Cancel();
        saving = Later.After(0.5, Flush);
    }
    public static void Flush()
    {
        if (Ephemeral || map == null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only profile: the cat just forgets */ }
    }
}
