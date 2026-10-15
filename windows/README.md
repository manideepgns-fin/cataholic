# Cataholic for Windows

The same cat as the Mac app, drawn in code with C# / .NET 8 / WPF. There are no NuGet packages.
It covers the core cat: the 8 coats, gravity on windows and the taskbar, pet and purr, emotes, zoomies, More cats,
On her own, Sleep, the menu, a tray paw and Find my cat (double-click her).
Not yet ported: the window grid, auto-update, `cataholic://` links.

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
2. It runs every test scenario next to a Notepad window (`--scenario rest|drop|taskbar|zoomies|herd20|herd100|own|menu|sleep|emotes|reduced|idlenap|touch|find`).
3. It takes screenshots and logs the CPU and memory of the cat process.
4. `Cataholic.exe --check-find 60` (its own step) deals 60 Find-my-cat layouts per level per coat and fails the build if any is unfair.

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
  - Find my cat: `FindMyCat.cs` (round, game, fairness check) + `FindScenes.cs` (places, props, spots) ← `FindMyCat.swift` + `FindScenes.swift`; `FindView.cs` (window, camera, layers), `FindArt.cs` (the pictures), `FindGfx.cs` (a SwiftUI-Canvas-shaped kit over WPF), `FindRandom.cs` (Swift's seeded random, step for step)
- `Win32.cs` holds every P/Invoke and `Display`, the one DPI helper.
- Prefs are saved in `%APPDATA%\Cataholic\prefs.json` with the Mac's key names. Launch at login uses
  `HKCU\…\Run\Cataholic`.

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
- **Find my cat layouts are the Mac's, bit for bit:** `FindRandom` follows Swift's `random(in:)`, `shuffled` and `randomElement`
  exactly, so a seed gives the Mac's layout (checked cat by cat against a scratch Mac build, and the `--check-find` table is
  identical). Note that on the Mac `CGPoint(x: .random(in: 24...1076), …)` picks the `Int` initialiser, so open spots are whole numbers.
- **Find my cat on a small screen:** the window is 1100 × 704 DIPs; where the work area is smaller (CI is 1024 × 728) the whole
  window is scaled down to fit instead of being clipped.
