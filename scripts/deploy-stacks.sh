#!/usr/bin/env bash
# Copies the Docker stacks downloaded by download-all.sh into Dockge's stacks folder and starts them.
# Only containers whose configuration changed are recreated. Each stack's .env stays on the Pi and is never overwritten.
#
# Usage: deploy-stacks.sh [stack...]   (default: all stacks, booth first because the others join its network)
set -euo pipefail

SOURCE="$HOME/stacks"
TARGET="${STACKS_DIR:-/opt/stacks}"

if [ $# -gt 0 ]; then
  STACKS=("$@")
else
  STACKS=("booth" "observability" "dozzle")
fi

SUDO=""
if [ ! -w "$TARGET" ] || ! docker info >/dev/null 2>&1; then
  SUDO="sudo"
fi

for STACK in "${STACKS[@]}"; do
  echo "Deploying $STACK..."

  $SUDO mkdir -p "$TARGET/$STACK"
  $SUDO cp -r "$SOURCE/$STACK/." "$TARGET/$STACK/"

  if [ -f "$TARGET/$STACK/.env.example" ] && [ ! -f "$TARGET/$STACK/.env" ]; then
    $SUDO cp "$TARGET/$STACK/.env.example" "$TARGET/$STACK/.env"
    echo "  Created $TARGET/$STACK/.env from .env.example. Fill it in and run this script again."
    exit 1
  fi

  (cd "$TARGET/$STACK" && $SUDO docker compose up -d --remove-orphans)
done

echo "All stacks deployed."
