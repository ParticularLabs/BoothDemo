#!/usr/bin/env bash
set -euo pipefail

export GITHUB_TOKEN="TOKEN"

REPO="ParticularLabs/BoothDemo"
WORKFLOW="release.yml"
BRANCH="main"

RUNTIME="linux-arm64"          # must match matrix.runtime
NAME="Sales"        # must match steps.vars.outputs.name
ARTIFACT="${RUNTIME}-${NAME}"

DEST="$HOME/sales"

rm -Rf "$DEST"
mkdir -p "$DEST"

RUN_ID="$(gh run list \
  --repo "$REPO" \
  --workflow "$WORKFLOW" \
  --branch "$BRANCH" \
  --status success \
  --limit 1 \
  --json databaseId \
  --jq '.[0].databaseId')"

gh run download "$RUN_ID" \
  --repo "$REPO" \
  --name "$ARTIFACT" \
  --dir "$DEST"

chmod +x "$DEST/$NAME"