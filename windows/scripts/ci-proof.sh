#!/usr/bin/env bash
# Push the current branch, wait for its "windows" CI run, and download the screenshots to windows/ci-out/<sha>/.
# Usage: windows/scripts/ci-proof.sh            (from the repo root, with the change committed)
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
branch=$(git branch --show-current); sha=$(git rev-parse HEAD)
git push -q origin "$branch"
echo "waiting for the windows run on $branch @ ${sha:0:7}…"
for _ in $(seq 60); do
  id=$(gh run list --commit "$sha" --json databaseId,workflowName -q ".[] | select(.workflowName==\"windows\") | .databaseId" 2>/dev/null | head -1)
  [ -n "$id" ] && break; sleep 5
done
[ -n "${id:-}" ] || { echo "no run found"; exit 1; }
gh run watch "$id" --exit-status --interval 15 >/dev/null && ok=1 || ok=0
out="windows/ci-out/${sha:0:7}"; rm -rf "$out"; mkdir -p "$out"
gh run download "$id" -n proof -D "$out" || true
echo "run $id: $([ $ok = 1 ] && echo green || echo RED) → $out"; ls "$out"
[ -f "$out/log.txt" ] && cat "$out/log.txt"
[ $ok = 1 ]
