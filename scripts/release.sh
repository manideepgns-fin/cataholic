#!/usr/bin/env bash
# Release the version in Resources/Info.plist: build → notarize → zip → signed update feed (appcast) → GitHub release.
# Usage: scripts/release.sh <release-notes.md> ["release title"]      DRY=1 stops just before publishing.
# Commit and push first (the release tags origin/main). Every release bumps CFBundleVersion — Sparkle compares that number.
# The appcast is signed with the EdDSA key in the login Keychain (Sparkle's generate_keys made it; back it up with
# `generate_keys -x <file>` — lose it and installed copies can no longer be updated automatically).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
NOTES="${1:?usage: scripts/release.sh <release-notes.md> [title]}"
V="$(plutil -extract CFBundleShortVersionString raw Resources/Info.plist)"
TITLE="${2:-Cataholic $V}"
if [[ "${DRY:-}" != 1 ]]; then
  git diff --quiet && git diff --cached --quiet || { echo "STOP: uncommitted changes — commit first" >&2; exit 1; }
  git fetch -q origin
  [[ "$(git rev-parse HEAD)" == "$(git rev-parse origin/main)" ]] || { echo "STOP: HEAD is not origin/main — push first" >&2; exit 1; }
fi

# The notary login lives in the keychain and has vanished once (13 Oct 2026, between two releases an hour apart) —
# check it before a 2-minute build instead of after.
xcrun notarytool history --keychain-profile "${NOTARY_PROFILE:-cataholic-notary}" >/dev/null 2>&1 || {
  echo "STOP: notary login missing. Make an app-specific password (appleid.apple.com → Sign-In and Security →" >&2
  echo "      App-Specific Passwords), then run in a normal Terminal:" >&2
  echo "      xcrun notarytool store-credentials cataholic-notary --team-id RCZPRL6BS4 --apple-id <your Apple ID>" >&2
  exit 1; }
scripts/build.sh
scripts/notarize.sh
ZIP="$ROOT/dist/Cataholic-$V.zip"; FEED="$ROOT/dist/feed-$V"
ditto -c -k --sequesterRsrc --keepParent dist/Cataholic.app "$ZIP"     # --sequesterRsrc: what Sparkle expects
mkdir -p "$FEED"; cp "$ZIP" "$FEED/"
GEN="$(ls -d "$ROOT"/.build/sparkle-*/bin | tail -1)/generate_appcast"
"$GEN" --download-url-prefix "https://github.com/manideepgns-fin/cataholic/releases/download/v$V/" "$FEED"
grep -q "sparkle:edSignature" "$FEED/appcast.xml" || { echo "STOP: appcast is unsigned" >&2; exit 1; }
[[ "${DRY:-}" == 1 ]] && { echo "DRY: built, notarized and signed the feed — not publishing. $ZIP  $FEED/appcast.xml"; exit 0; }

gh release create "v$V" "$ZIP" "$FEED/appcast.xml" --target main --title "$TITLE" --notes-file "$NOTES" --latest
echo "Released v$V — installed copies pick it up within a day (or right away via 'Check for updates…')."
