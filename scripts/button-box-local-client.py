#!/usr/bin/env python3
import argparse
import subprocess
import time
from evdev import InputDevice, ecodes


TARGET_SESSION = "client"


def tmux_send_keys(keys: str):
    return [
        "tmux",
        "send-keys",
        "-t",
        TARGET_SESSION,
        keys,
    ]


def send_tmux(keys: str):
    try:
        subprocess.run(tmux_send_keys(keys), check=False)
    except Exception as exc:
        print(f"Failed to send tmux keys {keys}: {exc}", flush=True)


COMMANDS_PRESS = {
    "BTN_TOP": tmux_send_keys("b"),
    "BTN_TRIGGER": tmux_send_keys("R1"),
}

COMMANDS_RELEASE = {
    "BTN_TRIGGER": tmux_send_keys("R0"),
}


def run_command(cmd, button_name: str, action: str) -> None:
    if not cmd:
        return

    try:
        print(f"{button_name} {action}", flush=True)
        subprocess.run(cmd, check=False)
    except Exception as exc:
        print(f"Failed to run command for {button_name} {action}: {exc}", flush=True)


def map_to_bucket(value: int) -> int:
    """Map 0–255 → 0–9"""
    bucket = int(value * 10 / 256)
    return min(bucket, 9)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "device",
        help="Input device path, e.g. /dev/input/event7 or /dev/input/by-id/usb-...-event-joystick",
    )
    args = parser.parse_args()

    dev = InputDevice(args.device)
    print(f"Listening on: {dev.path} - {dev.name}", flush=True)
    print(f"Sending tmux keys to session: {TARGET_SESSION}", flush=True)

    watched_buttons = {
        ecodes.ecodes["BTN_TOP"]: "BTN_TOP",
        ecodes.ecodes["BTN_TRIGGER"]: "BTN_TRIGGER",
    }

    last_rx = None
    last_ry = None
    last_rz = None

    while True:
        try:
            for event in dev.read_loop():

                if event.type == ecodes.EV_KEY:
                    button_name = watched_buttons.get(event.code)
                    if not button_name:
                        continue

                    if event.value == 1:
                        run_command(COMMANDS_PRESS.get(button_name), button_name, "press")
                    elif event.value == 0:
                        run_command(COMMANDS_RELEASE.get(button_name), button_name, "release")

                elif event.type == ecodes.EV_ABS:

                    if event.code == ecodes.ABS_RX:
                        bucket = map_to_bucket(event.value)

                        if bucket != last_rx:
                            last_rx = bucket
                            cmd = f"Q{bucket}"
                            print(f"ABS_RX -> {cmd}", flush=True)
                            send_tmux(cmd)

                    elif event.code == ecodes.ABS_RY:
                        bucket = map_to_bucket(event.value)

                        if bucket != last_ry:
                            last_ry = bucket
                            cmd = f"W{bucket}"
                            print(f"ABS_RY -> {cmd}", flush=True)
                            send_tmux(cmd)
                    
                    elif event.code == ecodes.ABS_RZ:
                        bucket = map_to_bucket(event.value)

                        if bucket != last_rz:
                            last_rz = bucket
                            cmd = f"E{bucket}"
                            print(f"ABS_RZ -> {cmd}", flush=True)
                            send_tmux(cmd)

        except OSError as exc:
            print(f"Device error: {exc}. Retrying in 2 seconds...", flush=True)
            time.sleep(2)
            dev = InputDevice(args.device)


if __name__ == "__main__":
    main()
