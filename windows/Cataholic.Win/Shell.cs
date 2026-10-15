using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Cataholic;

// Shell — the app around the cat (Cataholic.swift): a paw in the tray (the menu-bar paw), launch at login, and startup.
// The tray paw opens the same menu as a right-click on the cat. The app shows no taskbar button (the windows are tool
// windows). Updates are Updates.cs (our own, no Sparkle); links are Links.cs.
public static class Shell
{
    static NotifyIcon? tray;
    static Icon? icon;

    public static void Start(string? scenario = null, string? trace = null)
    {
        Display.Refresh();
        CatCoat.Current = CatCoat.ById(Prefs.String("crewCoat"));
        MakeTray();
        // a monitor plugged in / unplugged / rescaled, or the taskbar moved: put the cat back on the primary display
        SystemEvents.DisplaySettingsChanged += (_, _) => { Display.Refresh(); CatPanel.ApplyPrefs(); CatHerd.Shared.Restart(); };
        SystemEvents.UserPreferenceChanged += (_, e) => { if (e.Category == UserPreferenceCategory.Desktop) { Display.Refresh(); CatPanel.ApplyPrefs(); } };
        if (scenario != null) Scenario.Prepare(scenario);
        CatPanel.Show();
        if (scenario != null) Scenario.Run(scenario, trace);
        Updates.Start(scenario);                                       // updates
    }

    /// A tray balloon (a manual "Check for updates" answers with one).
    public static void Notify(string text) => tray?.ShowBalloonTip(4000, "Cataholic", text, ToolTipIcon.None);   // updates

    public static void Stop()
    {
        Prefs.Flush();
        CatSound.PurrStop();
        if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        icon?.Dispose();
    }

    static void MakeTray()
    {
        icon = Paw();
        tray = new NotifyIcon { Icon = icon, Text = "Cataholic", Visible = true };
        // left- or right-click on the paw: the same menu as a right-click on the cat
        tray.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            if ((DateTime.Now - CatMenuPanel.ClosedAt).TotalSeconds < 0.4) return;     // that click just closed it: a second click toggles
            CatMenuPanel.Show(Display.Pointer());
        };
    }

    /// The tray paw, drawn in code: white on a dark taskbar, black on a light one.
    static Icon Paw()
    {
        bool light = true;
        try { light = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 1) as int? ?? 1) != 0; } catch { }
        const int n = 32;
        using var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var br = new SolidBrush(light ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.White);
            float k = n / 18f;                                          // the paw is drawn on an 18-unit grid (CatMenu.PawShape)
            foreach (var (x, y, rx, ry) in new[] { (3.2f, 6.2f, 1.6f, 2.1f), (6.6f, 3.2f, 1.7f, 2.3f), (11.4f, 3.2f, 1.7f, 2.3f), (14.8f, 6.2f, 1.6f, 2.1f) })
                g.FillEllipse(br, (x - rx) * k, (y - ry) * k, 2 * rx * k, 2 * ry * k);
            using var pad = new GraphicsPath();
            pad.AddBezier(9 * k, 7.6f * k, 6.2f * k, 7.6f * k, 3.6f * k, 11f * k, 3.9f * k, 13.1f * k);
            pad.AddBezier(3.9f * k, 13.1f * k, 4.1f * k, 14.7f * k, 5.6f * k, 15.1f * k, 7f * k, 14.7f * k);
            pad.AddBezier(7f * k, 14.7f * k, 8.1f * k, 14.4f * k, 8.5f * k, 14.3f * k, 9f * k, 14.3f * k);
            pad.AddBezier(9f * k, 14.3f * k, 9.5f * k, 14.3f * k, 9.9f * k, 14.4f * k, 11f * k, 14.7f * k);
            pad.AddBezier(11f * k, 14.7f * k, 12.4f * k, 15.1f * k, 13.9f * k, 14.7f * k, 14.1f * k, 13.1f * k);
            pad.AddBezier(14.1f * k, 13.1f * k, 14.4f * k, 11f * k, 11.8f * k, 7.6f * k, 9f * k, 7.6f * k);
            pad.CloseFigure();
            g.FillPath(br, pad);
        }
        var h = bmp.GetHicon();
        try { using var tmp = Icon.FromHandle(h); return (Icon)tmp.Clone(); }
        finally { DestroyIcon(h); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
}

/// Launch at login: HKCU\Software\Microsoft\Windows\CurrentVersion\Run, value "Cataholic" = the exe path in quotes.
static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "Cataholic";
    public static bool IsOn
    {
        get { try { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(Name) != null; } catch { return false; } }
    }
    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (on) k.SetValue(Name, "\"" + Environment.ProcessPath + "\""); else k.DeleteValue(Name, false);
        }
        catch { }
    }
}
