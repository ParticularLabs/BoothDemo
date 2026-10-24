#!/usr/bin/env bash
set -euo pipefail

IPS=("$Pi_2_IP" "$Pi_3_IP" "$Pi_4_IP" "$Pi_5_IP")
FOLDERS=("sales" "client" "scripts" "stacks")

for IP in "${IPS[@]}"; do
    echo "Deploying to nsb@$IP..."

    for FOLDER in "${FOLDERS[@]}"; do
        echo "  Copying $FOLDER..."
        scp -r "$HOME/$FOLDER" "nsb@$IP:~/"
    done

    echo "Deployment to $IP completed."
done

echo "All transfers completed."