#!/usr/bin/env bash
# Build Cataholic.app into dist/ (swiftc, no Xcode project). Signed with the Developer ID Application certificate
# when this Mac has one (then scripts/notarize.sh), else ad-hoc. SIGN_ID=<name or hash> picks one; SIGN_ID=- forces ad-hoc.
# Sparkle (the auto-updater) is fetched once into .build/, checked against a pinned hash, linked, embedded and signed.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/dist/Cataholic.app"
SPARKLE_VER=2.10.0
SPARKLE_SHA256=c2bf58aa8387266ac179357b1415d6f2635f044da8be41042af32425dae6da0c
SPARKLE="$ROOT/.build/sparkle-$SPARKLE_VER"
mkdir -p "$ROOT/.build"
if [[ ! -d "$SPARKLE/Sparkle.framework" ]]; then
  TAR="$ROOT/.build/Sparkle-$SPARKLE_VER.tar.xz"
  curl -fsSL -o "$TAR" "https://github.com/sparkle-project/Sparkle/releases/download/$SPARKLE_VER/Sparkle-$SPARKLE_VER.tar.xz"
  echo "$SPARKLE_SHA256  $TAR" | shasum -a 256 -c -
  mkdir -p "$SPARKLE" && tar -xf "$TAR" -C "$SPARKLE"
fi
LINK=(-F "$SPARKLE" -framework Sparkle -Xlinker -rpath -Xlinker @executable_path/../Frameworks)
swiftc -O -parse-as-library -target arm64-apple-macos13.0 -module-cache-path "$ROOT/.build/mc" "${LINK[@]}" \
  -o "$ROOT/.build/Cataholic-arm64" "$ROOT"/Sources/Cataholic/*.swift
swiftc -O -parse-as-library -target x86_64-apple-macos13.0 -module-cache-path "$ROOT/.build/mc" "${LINK[@]}" \
  -o "$ROOT/.build/Cataholic-x86_64" "$ROOT"/Sources/Cataholic/*.swift
rm -rf "$APP"; mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Frameworks"
lipo -create "$ROOT/.build/Cataholic-arm64" "$ROOT/.build/Cataholic-x86_64" -output "$APP/Contents/MacOS/Cataholic"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"
cp -R "$ROOT/Resources/Sounds" "$APP/Contents/Resources/Sounds"
[[ -f "$ROOT/Resources/AppIcon.icns" ]] && cp "$ROOT/Resources/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
FW="$APP/Contents/Frameworks/Sparkle.framework"
ditto "$SPARKLE/Sparkle.framework" "$FW"
rm -rf "$FW/XPCServices" "$FW/Versions/B/XPCServices"      # only a sandboxed app needs Sparkle's XPC helpers
ID="${SIGN_ID:-$(security find-identity -v -p codesigning | sed -n 's/.*"\(Developer ID Application: .*\)"/\1/p' | head -1)}"
if [[ -n "$ID" && "$ID" != "-" ]]; then
  # Inside-out: Sparkle's helpers, then the framework, then the app. Never --deep on the app — it would push our
  # Apple-Events entitlement onto Sparkle's binaries. Notarization rejects anything left ad-hoc.
  for part in "$FW/Versions/B/Autoupdate" "$FW/Versions/B/Updater.app" "$FW"; do
    codesign --force --options runtime --timestamp --sign "$ID" "$part"
  done
  codesign --force --options runtime --timestamp --entitlements "$ROOT/Resources/Cataholic.entitlements" --sign "$ID" "$APP"
  echo "Signed: $ID"
else
  codesign --force --deep --sign - "$APP" >/dev/null
  echo "Signed: ad-hoc (no Developer ID certificate on this Mac)"
fi
codesign --verify --deep --strict "$APP"
echo "Built: $APP"
