#!/usr/bin/env bash
# Render the app icon from the cat drawing itself and pack Resources/AppIcon.icns (every macOS size, @1x + @2x).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ -x "$ROOT/dist/Cataholic.app/Contents/MacOS/Cataholic" ]] || "$ROOT/scripts/build.sh"
WORK="$(mktemp -d)"; SET="$WORK/AppIcon.iconset"; mkdir "$SET"
"$ROOT/dist/Cataholic.app/Contents/MacOS/Cataholic" --render-icon "$WORK/icon-1024.png"
for s in 16 32 128 256 512; do
  sips -z $s $s "$WORK/icon-1024.png" --out "$SET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) "$WORK/icon-1024.png" --out "$SET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$SET" -o "$ROOT/Resources/AppIcon.icns"
cp "$WORK/icon-1024.png" "$ROOT/docs/icon.png"
echo "Icon: $ROOT/Resources/AppIcon.icns (+ docs/icon.png)"
