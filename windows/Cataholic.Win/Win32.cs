using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Cataholic;

// Every P/Invoke in one place, plus Display: the ONE helper that turns physical pixels (what Win32 speaks) into WPF
// units (DIPs, what everything else in the app speaks). The cat lives on the primary monitor, so one scale — that
// monitor's DPI / 96 — covers it; window rects, the work area and the pointer all pass through it.
static class Win32
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public const int DWMWA_CLOAKED = 14, DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int VK_LBUTTON = 1, VK_RBUTTON = 2, VK_ESCAPE = 0x1B;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT v, int size);

    public static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(64);
        return GetClassName(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }
    public static int ExStyle(IntPtr h) => (int)GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
    public static void AddExStyle(IntPtr h, int bits) => SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ExStyle(h) | bits));
    public static bool Cloaked(IntPtr h) => DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int c, 4) == 0 && c != 0;
    /// The visible frame (no invisible resize border — GetWindowRect would include ~7 px of it and the cat would float).
    public static bool FrameBounds(IntPtr h, out RECT r) => DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) == 0;
    public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    public static bool AnyMouseButton => KeyDown(VK_LBUTTON) || KeyDown(VK_RBUTTON);

    /// Seconds since the last keyboard/mouse input — the cat naps once you've been away 5 minutes.
    public static double? IdleOverride;          // test scenarios: pretend the user is here (0) or away (400)
    public static double IdleSeconds
    {
        get
        {
            if (IdleOverride is double o) return o;
            var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return GetLastInputInfo(ref i) ? ((uint)Environment.TickCount - i.dwTime) / 1000.0 : 0;
        }
    }

    /// The primary monitor: its pixel rect, work area and DPI.
    public static bool PrimaryMonitor(out RECT frame, out RECT work, out double scale)
    {
        frame = work = default; scale = 1;
        var m = MonitorFromPoint(new POINT(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (m == IntPtr.Zero || !GetMonitorInfo(m, ref mi)) return false;
        frame = mi.rcMonitor; work = mi.rcWork;
        if (GetDpiForMonitor(m, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0 && dx > 0) scale = dx / 96.0;
        return true;
    }
}

/// The primary monitor in WPF units (DIPs), y down from its top — what the Mac code calls `screen.frame` / `visibleFrame`.
/// `Visible` is the work area, so a taskbar on any side shrinks it; its bottom is the "Dock line", its top the "menu bar".
static class Display
{
    public static double Scale = 1;
    public static Rect Frame = new(0, 0, 1024, 768);
    public static Rect Visible = new(0, 0, 1024, 728);

    public static void Refresh()
    {
        if (!Win32.PrimaryMonitor(out var f, out var w, out var s)) return;
        Scale = s; Frame = ToDip(f); Visible = ToDip(w);
    }
    public static Rect ToDip(Win32.RECT r) => new(r.Left / Scale, r.Top / Scale, (r.Right - r.Left) / Scale, (r.Bottom - r.Top) / Scale);
    public static int Px(double dip) => (int)Math.Round(dip * Scale);
    public static Point Pointer()
    {
        Win32.GetCursorPos(out var p);
        return new Point(p.X / Scale, p.Y / Scale);
    }
    /// Move/size a window by its DIP rect (physical pixels on the wire; the window never activates).
    public static void Place(IntPtr hwnd, Rect r) =>
        Win32.SetWindowPos(hwnd, IntPtr.Zero, Px(r.X), Px(r.Y), Px(r.Width), Px(r.Height), Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    public static void Raise(IntPtr hwnd) =>
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
}
