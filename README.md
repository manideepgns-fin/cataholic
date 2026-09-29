# Cataholic

A chunky cat (or a hundred) that lives on your Mac.

![The eight Cataholic cats, sitting and napping](docs/cats.png)

- **Eight coats** — Ginger (in reading glasses), Smokey, Patches, Tux, Butter, Cocoa, Snow, Lilac.
- **Gravity** — drag your cat off the menu bar and let go: it falls and lands on the Dock or the top of a
  window. Throw it and it flies. Move the window it's sitting on and it falls off.
- **Zoomies** — a leg-scramble start, a galloping run, hops, and skids that lean back.
- **The multiplier** — right-click → *More cats* → up to 100 cats raining onto your screen. They never block a
  click; only your own cat is pettable.
- **Pet it** — click for a purr and a happy squint.
- **Quick launch** — save links and apps (right-click → *Quick launch*); double-click the cat opens your favourite
  (no favourite yet → zoomies).
- **Waiting for you** — right-click → *Waiting for you* → *Watch a link…* and paste a page you keep open in Chrome.
  When its tab title shows a number — like “(3) Billing Desk” or “Inbox (12) - Gmail” — your cat holds it up; when it goes up
  the cat hops where it sits and says who it's from for a few seconds, and a Mac notification lands in Notification Center. Click the notification (or the row) to jump to that tab. The first
  time, macOS asks whether Cataholic may control Chrome: that's how it reads your tab titles. Nothing leaves your Mac.
- **Updates itself** — checks once a day; a new version is downloaded, verified against the author's signing key and swapped in
  (the cat relaunches for a second). Right-click → *Check for updates…* to ask right now.
- **Signs** — any app or script can make your cat hold up a number, and it whispers when the number goes up:

  ```bash
  open "cataholic://sign?count=3&note=Build%20failed"   # hold up 3
  open "cataholic://sign?count=0"                         # put it down
  open "cataholic://zoomies"
  ```
- **Light as a feather** — at rest the cat is one still picture: ~0% CPU, ~30 MB. It only animates when it moves.
- Naps melted flat when you're away. Quiet mode and Reduce Motion calm everything down.

Cat sounds are CC0 / public-domain recordings — see `Resources/Sounds/CREDITS.md`.

## Install

1. Download `Cataholic-0.3.3.zip` from [Releases](../../releases), unzip it, and drag **Cataholic** into Applications.
2. Open it. It's signed and notarized by Apple, so it opens like any other app — and from here on it keeps itself up to date.
3. A paw appears in your menu bar and a cat in the top-right corner. Right-click the cat for everything.

Needs macOS 13 (Ventura) or later. Runs natively on Apple silicon and Intel.

## Build

```bash
./scripts/build.sh      # → dist/Cataholic.app (universal: Apple silicon + Intel), macOS 13+
```

No Xcode project — plain `swiftc`.
