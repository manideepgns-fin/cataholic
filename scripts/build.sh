#!/usr/bin/env bash
# Build Cataholic.app into dist/ (swiftc, no Xcode project). Signed with the Developer ID Application certificate
# when this Mac has one (then scripts/notarize.sh), else ad-hoc. SIGN_ID=<name or hash> picks one; SIGN_ID=- forces ad-hoc.
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
ID="${SIGN_ID:-$(security find-identity -v -p codesigning | sed -n 's/.*"\(Developer ID Application: .*\)"/\1/p' | head -1)}"
if [[ -n "$ID" && "$ID" != "-" ]]; then
  codesign --force --options runtime --timestamp --entitlements "$ROOT/Resources/Cataholic.entitlements" --sign "$ID" "$APP"
  echo "Signed: $ID"
else
  codesign --force --deep --sign - "$APP" >/dev/null
  echo "Signed: ad-hoc (no Developer ID certificate on this Mac)"
fi
echo "Built: $APP"
