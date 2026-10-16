#!/usr/bin/env bash
set -euo pipefail

LOCAL_SESSION="remote-grid"

LEFT_HOST="nsb@$Pi_2_IP"
RIGHT_TOP_HOST="nsb@$Pi_3_IP"
RIGHT_BOTTOM_HOST="nsb@$Pi_4_IP"

LEFT_REMOTE_SESSION="left"
RIGHT_TOP_REMOTE_SESSION="top"
RIGHT_BOTTOM_REMOTE_SESSION="bottom"

remote_cmd() {
  local host="$1"
  local session="$2"
  printf "exec ssh -tt %q 'tmux new-session -A -D -s %q'" "$host" "$session"
}

tmux new-session -d -s "$LOCAL_SESSION" \
  "$(remote_cmd "$LEFT_HOST" "$LEFT_REMOTE_SESSION")"

LEFT_PANE=$(tmux display-message -p -t "$LOCAL_SESSION":0 '#{pane_id}')

RIGHT_TOP_PANE=$(tmux split-window -h -t "$LEFT_PANE" -p 50 -P -F '#{pane_id}' \
  "$(remote_cmd "$RIGHT_TOP_HOST" "$RIGHT_TOP_REMOTE_SESSION")")

RIGHT_BOTTOM_PANE=$(tmux split-window -v -t "$RIGHT_TOP_PANE" -p 50 -P -F '#{pane_id}' \
  "$(remote_cmd "$RIGHT_BOTTOM_HOST" "$RIGHT_BOTTOM_REMOTE_SESSION")")

tmux select-pane -t "$LEFT_PANE"
tmux attach -t "$LOCAL_SESSION"
