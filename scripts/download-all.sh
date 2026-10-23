#!/usr/bin/env bash
set -euo pipefail

REPO="ParticularLabs/BoothDemo"
WORKFLOW="release.yml"
BRANCH="main"

RUNTIME="linux-arm64"          # must match matrix.runtime

RUN_ID="$(gh run list \
  --repo "$REPO" \
  --workflow "$WORKFLOW" \
  --branch "$BRANCH" \
  --status success \
  --limit 1 \
  --json databaseId \
  --jq '.[0].databaseId')"

rm -Rf "$HOME/client"
mkdir -p "$HOME/client"
gh run download "$RUN_ID" --repo "$REPO" --name "${RUNTIME}-ClientUI" --dir "$HOME/client"
chmod +x "$HOME/client/ClientUI"

rm -Rf "$HOME/sales"
mkdir -p "$HOME/sales"
gh run download "$RUN_ID" --repo "$REPO" --name "${RUNTIME}-Sales" --dir "$HOME/sales"
chmod +x "$HOME/sales/Sales"

rm -Rf "$HOME/scripts"
mkdir -p "$HOME/scripts"
gh run download "$RUN_ID" --repo "$REPO" --name "scripts" --dir "$HOME/scripts"
chmod +x "$HOME"/scripts/*.sh



rm -Rf "$HOME/stacks"
mkdir -p "$HOME/stacks"
gh run download "$RUN_ID" --repo "$REPO" --name "stacks" --dir "$HOME/stacks"
