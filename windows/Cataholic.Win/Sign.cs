using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// The sign (Cataholic.swift MailStore + CrewModel.retick + CrewView's whisper): what a cataholic://sign link told the cat.
// The Mac's MailStore is shaped like Atlance's inbox; here it only ever holds what a link said. The cat reads the count
// for her "needs you" pose (the sign with the number) and whispers when it goes UP.

/// The sign the cat holds (set by `cataholic://sign`).
static class MailStore
{
    public static int Count { get; private set; }          // the sign's number, capped at 999 (the Mac builds one case per count, up to 999)
    public static string Note { get; private set; } = "";
    public static event System.Action? Changed;
    public static void SetSign(long count, string note)
    {
        Count = (int)Math.Min(Math.Max(0, count), 999); Note = Count > 0 ? note : "";
        Changed?.Invoke();
    }
}

sealed partial class CatModel
{
    /// "New notification from <note>" for 3 s, when the sign's count goes up; null = no bubble.
    string? whisper; public string? Whisper { get => whisper; private set => Set(ref whisper, value); }
    int lastNeeds = -1;
    Later? whisperWork, hushWatch;

    void InitSign()
    {
        MailStore.Changed += Retick;
        Retick();
    }

    void Retick()
    {
        int n = MailStore.Count;
        if (lastNeeds >= 0 && n > lastNeeds)
        {
            // The count went up: one line for 3 s, and the cat bumps where it sits (owner 29 Sep 2026: "wherever it is, bumps
            // and says you have a new notification from so and so… and goes back").
            string who = MailStore.Note;
            Whisper = who.Length == 0 ? "New notification" : "New notification from " + who;
            CatPanel.Refit();                 // pick the bubble's side before the view draws it (CatLayout.WhisperOnRight)
            CatPanel.Bump();
            whisperWork?.Cancel();
            whisperWork = Later.After(3, DismissWhisper);
            WatchHush();
        }
        lastNeeds = n;
        Publish();
    }

    public void DismissWhisper()
    {
        whisperWork?.Cancel(); whisperWork = null;
        hushWatch?.Cancel(); hushWatch = null;
        Whisper = null;
    }

    /// Esc hushes the whisper, and so does a click in another app (the Mac's global monitor). The window never takes focus,
    /// so — as the menu does — both are polled, and only while the bubble is up (3 s): at rest nothing runs.
    void WatchHush()
    {
        hushWatch?.Cancel();
        bool prevDown = Win32.AnyMouseButton, prevEsc = Win32.KeyDown(Win32.VK_ESCAPE);
        hushWatch = Later.Every(0.03, _ =>
        {
            bool down = Win32.AnyMouseButton, esc = Win32.KeyDown(Win32.VK_ESCAPE);
            if ((esc && !prevEsc) || (down && !prevDown && !CatPanel.PointerOnHer)) { DismissWhisper(); return; }
            prevDown = down; prevEsc = esc;
        });
    }
}

sealed partial class CatView
{
    Rect whisperBox = Rect.Empty;          // where the bubble was last drawn (the view's own coordinates), for the tap
    bool WhisperHit(Point p) => whisperBox.Contains(p);

    static readonly Brush WhisperShadow = new SolidColorBrush(Color.FromArgb(14, 0, 0, 0));
    static readonly Typeface WhisperFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    /// The whisper: one line, 13 pt, hugging its text (owner 28 Sep 2026: "too big"), in the fixed frame CatLayout reserves
    /// for it. As on the Mac the frame is scaled by Zoom but the text is not — drawn here at screen size, outside the
    /// × Zoom of the cat. Left of the cat the bubble sits at the frame's right end, right of her at its left end.
    void DrawWhisper(DrawingContext dc, Vector box)
    {
        whisperBox = Rect.Empty;
        if (model.Whisper is not string w) return;
        var b = CatLayout.Whisper;
        double fx = box.X + b.X0 * CatLayout.Zoom, fy = box.Y + b.Y0 * CatLayout.Zoom;
        double fw = (b.X1 - b.X0) * CatLayout.Zoom, fh = (b.Y1 - b.Y0) * CatLayout.Zoom;
        var ft = new FormattedText(w, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, WhisperFace, 13, Ink,
            VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = fw - 20, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        double bw = ft.Width + 20, bh = ft.Height + 12;
        var r = new Rect(CatLayout.WhisperOnRight ? fx : fx + fw - bw, fy + (fh - bh) / 2, bw, bh);
        // the soft shadow (black .18, blur 3, 1 down) as three fading rims — a real blur effect would slow a layered window
        for (int i = 3; i >= 1; i--)
            dc.DrawRoundedRectangle(WhisperShadow, null, new Rect(r.X - i, r.Y - i + 1, r.Width + 2 * i, r.Height + 2 * i), 10 + i, 10 + i);
        dc.DrawRoundedRectangle(Paper, null, r, 10, 10);
        dc.DrawText(ft, new Point(r.X + 10, r.Y + 6));
        whisperBox = r;
    }
}
