# Cataholic for Windows

The same cat as the Mac app, drawn in code with C# / .NET 8 / WPF. There are no NuGet packages.
It covers the core cat: the 8 coats, gravity on windows and the taskbar, pet and purr, emotes, zoomies, More cats,
On her own, Sleep, the menu, a tray paw and Find my cat (double-click her).
`cataholic://sign?count=3&note=…` and `cataholic://zoomies` links work (see "Links" below).
It updates itself (see "Updates and releases" below).

**The window grid is Mac-only, on purpose** (owner, 15 Oct 2026). Windows already snaps windows (Snap Layouts, Snap
Assist, PowerToys FancyZones). A port needs WinEvent hooks on every window and a full-screen overlay while you drag,
and that costs a lot for an off-by-default extra. A working port existed on branch `lane/win-grid` (merge reverted).

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
2. It runs every test scenario next to a Notepad window (`--scenario rest|drop|taskbar|zoomies|herd20|herd100|own|menu|sleep|emotes|reduced|idlenap|touch|sign|find`).
   After them come the release dry run and the update proof (see "Updates and releases").
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

## Updates and releases

- **The release:** push a tag `win-v<version>` (the csproj `<Version>` must match) and `.github/workflows/windows-release.yml`
  publishes `Cataholic-win-x64.zip` (only `Cataholic.exe`) + `.sha256` with `windows/RELEASE_NOTES.md`. A Windows release is
  **never** marked Latest (`--latest=false`): the Mac's Sparkle feed is `releases/latest/download/appcast.xml`, and a Windows
  release there would cut off every Mac copy. Mac tags are `v*`; never reuse them. `workflow_dispatch` takes the tag and a
  `dry_run` switch. `windows/scripts/package-release.ps1` does the packaging for both the release and CI's dry run.
- **The updater** (`Updates.cs`, no NuGet): 60 s after start, then daily, or **Check for updates** in the menu, it reads
  `api.github.com/repos/manideepgns-fin/cataholic/releases`, takes the newest non-draft, non-prerelease `win-v*` release, and
  updates only if it is higher than the running version. It downloads the zip and `.sha256` into `%LOCALAPPDATA%\Cataholic\update`,
  checks the hash, unzips, checks the new exe's `FileVersionInfo`, then renames the running `Cataholic.exe` to `.old`, moves the
  new one into place and starts it with `--after-update <old pid>`; the old cat quits. The new one waits for that pid before it
  takes the mutex (`Links.cs`) and deletes `Cataholic.exe.old`. Any failure leaves the folder as it was. It does nothing if the
  folder is not writable or the exe is not called `Cataholic.exe` (`dotnet run`). The last check time is the pref `lastUpdateCheck`.
- **Proof:** `windows/scripts/ci-update.ps1` (run by `windows.yml`) serves a feed shaped like the GitHub API from the CI job, with
  a newer 0.1.1 build, and runs 0.1.0 against it with `--scenario update --update-feed <url>`: once with a wrong `.sha256` (no swap)
  and once with the right one (swap, one process, 0.1.1, `.old` gone). The feed also holds a Mac `v9.9.9`, a draft and a prerelease,
  all of which must be ignored. The release workflow can't be dispatched from a branch until it is on the default branch, so
  `windows.yml` runs the same packaging script as a dry run.

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
