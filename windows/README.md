# Cataholic for Windows

The same cat as the Mac app, drawn in code with C# / .NET 8 / WPF. There are no NuGet packages.
It covers the core cat: the 8 coats, gravity on windows and the taskbar, pet and purr, emotes, zoomies, More cats,
On her own, Sleep, the menu and a tray paw.
`cataholic://sign?count=3&note=…` and `cataholic://zoomies` links work (see "Links" below).
The window grid works (right-click → Grid). Not yet ported: Find my cat, auto-update.

## Build

```bash
# One time, on a Mac or Linux box (no admin needed):
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir ~/.dotnet

~/.dotnet/dotnet build windows/Cataholic.Win -c Release        # compiles anywhere (EnableWindowsTargeting)
~/.dotnet/dotnet publish windows/Cataholic.Win -c Release -o windows/publish   # → Cataholic.exe (self-contained)
```

The app compiles on a Mac, but it only runs on Windows.

## Prove it on Windows (no Windows PC needed)

`.github/workflows/windows.yml` runs on GitHub's `windows-latest` (1024×768, 100% scale). It does three things:

1. It renders `coats.png` and `poses.png` (`--render-coats`, `--render-poses`). Compare them with the Mac's
   `docs/cats.png`.
2. It runs every test scenario next to a Notepad window (`--scenario rest|drop|taskbar|zoomies|herd20|herd100|own|menu|sleep|emotes|reduced|idlenap|touch|sign|grid|gridoutline`).
3. It takes screenshots and logs the CPU and memory of the cat process.

```bash
windows/scripts/ci-proof.sh   # pushes the branch, waits for the run, downloads to windows/ci-out/<sha>/
```

## Layout

- `CatPose.cs` holds the contract: every parameter of the Mac's `CrewCharacter`, with the same names and units
  (46 × 62 box points).
- `CatDrawing.cs` is a line-for-line port of `CrewCharacter.swift`. It is the only code that draws the cat.
- Each of the other files maps to one Swift file:
  - `CatModel` ← `CrewModel`
  - `CatView` ← `CrewView`
  - `CatPanel` ← `CrewPanel`
  - `Layout` ← `CrewLayout`
  - `Gravity` ← `CrewGravity`
  - `Herd` ← `CrewHerd`
  - `Own` ← `CrewOwn`
  - `Sound` ← `CrewSound`
  - `CatMenu` ← `CatMenu`
  - `WindowGuides` (+ `GuideOverlay`, `Wallpaper`) ← `WindowGuides`
- `Win32.cs` holds every P/Invoke and `Display`, the one DPI helper.
- Prefs are saved in `%APPDATA%\Cataholic\prefs.json` with the Mac's key names. Launch at login uses
  `HKCU\…\Run\Cataholic`.

## Links (`cataholic://`)

- `sign?count=3&note=Build%20failed` holds up a sign with 3 (it shows `9+` above 9) and whispers "New notification from
  <note>" for 3 s when the count goes **up**; `sign?count=0` puts it down; `zoomies` runs zoomies. The parsing rules
  are the Mac's (`Links.cs` ← `CataholicLinks`, `Sign.cs` ← `MailStore` and the whisper in `CrewModel`/`CrewView`).
- Every start writes `HKCU\Software\Classes\cataholic` (`URL Protocol`, `DefaultIcon`, `shell\open\command` =
  `"<exe>" --url "%1"`). It is per user, needs no admin and is left in place on quit.
- Windows starts a **new process** for a link. A named mutex (`Local\Cataholic`) makes the first process the cat; a
  later one (a link, or a double-click on the exe) passes its `--url` to her over the pipe `Cataholic.Link.<session>`
  and exits at once. Waiting on the pipe costs no CPU.
- Proof: `windows/scripts/ci-links.ps1` (its own script, because it acts between screenshots) opens real links with
  `Start-Process "cataholic://…"`, starts the exe a second time and presses Esc.

## Gotchas (learned the hard way)

- **Window rects:** get them from `DWMWA_EXTENDED_FRAME_BOUNDS`, never from `GetWindowRect`. On Windows 10/11,
  `GetWindowRect` includes about 7 px of invisible resize border, so the cat would float above every window.
- **One transparent window per herd cat, never one full-screen overlay:** a transparent WPF window is a layered
  window, and Windows renders it in software. A full-screen one repaints the whole screen bitmap on every frame,
  which used more than 1 CPU core at 20 cats. Small per-cat windows that move without a redraw, plus pose bitmaps
  drawn once and reused, bring it down to about 8% at 20 cats and about 40% at 100.
- **Reduce Motion:** this setting comes from `SystemParameters.ClientAreaAnimation`. The CI desktop and RDP
  sessions report it as ON, and then the cat stays still. The test scenarios pin it OFF.
- **DPI:** the app is PerMonitorV2. Win32 values are in pixels and WPF values are in DIPs, so every conversion goes
  through `Display`. CI only runs at 100% scale, so scaled displays are untested.
- **Memory:** a WPF process starts at about 200 MB working set. That is the .NET and WPF baseline. Don't quote the
  Mac app's "~30 MB" for Windows.
- **Show window contents while dragging:** the CI desktop has it OFF. Then Windows moves only an outline, and the window
  sends no LOCATIONCHANGE events until you release it. The grid follows the pointer instead.
