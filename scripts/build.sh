#!/usr/bin/env bash
# Build Cataholic.app into dist/ (swiftc, no Xcode project). Ad-hoc signed; release signing comes later.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/dist/Cataholic.app"
mkdir -p "$ROOT/.build"
swiftc -O -parse-as-library -target arm64-apple-macos13.0 -module-cache-path "$ROOT/.build/mc" \
  -o "$ROOT/.build/Cataholic-arm64" "$ROOT"/Sources/Cataholic/*.swift
swiftc -O -parse-as-library -target x86_64-apple-macos13.0 -module-cache-path "$ROOT/.build/mc" \
  -o "$ROOT/.build/Cataholic-x86_64" "$ROOT"/Sources/Cataholic/*.swift
rm -rf "$APP"; mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create "$ROOT/.build/Cataholic-arm64" "$ROOT/.build/Cataholic-x86_64" -output "$APP/Contents/MacOS/Cataholic"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"
cp -R "$ROOT/Resources/Sounds" "$APP/Contents/Resources/Sounds"
[[ -f "$ROOT/Resources/AppIcon.icns" ]] && cp "$ROOT/Resources/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
codesign --force --deep --sign - "$APP" >/dev/null
echo "Built: $APP"
