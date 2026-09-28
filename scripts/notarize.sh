#!/usr/bin/env bash
# Notarize + staple dist/Cataholic.app so any Mac opens it without a Gatekeeper warning. Run scripts/build.sh first.
#
# One-time setup (the secret goes into the macOS keychain, never into this repo):
#   xcrun notarytool store-credentials cataholic-notary --apple-id <id> --team-id <TEAM> --password <app-specific pw>
# Profile name override: NOTARY_PROFILE=<name>. Exits non-zero unless Gatekeeper accepts the stapled app.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/dist/Cataholic.app"
PROFILE="${NOTARY_PROFILE:-cataholic-notary}"
ZIP="$ROOT/dist/Cataholic-notarize.zip"

[[ -d "$APP" ]] || { echo "STOP: $APP missing — run scripts/build.sh first" >&2; exit 1; }
SIG="$(codesign -dv "$APP" 2>&1 || true)"      # not `| grep -q` — under pipefail SIGPIPE makes that test lie
if [[ "$SIG" == *"Signature=adhoc"* ]]; then
  echo "STOP: the app is ad-hoc signed — notarization needs the Developer ID Application certificate" >&2
  exit 1
fi
ditto -c -k --keepParent "$APP" "$ZIP"
xcrun notarytool submit "$ZIP" --keychain-profile "$PROFILE" --wait
rm -f "$ZIP"
xcrun stapler staple "$APP"
spctl -a -vv --type execute "$APP"          # prints "accepted … source=Notarized Developer ID" or fails the script
echo "Notarized + stapled: $APP"
