from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Callable, List

from pywinauto import Desktop, mouse
from pywinauto.application import Application
from pywinauto.keyboard import send_keys


TIME_PATTERN = re.compile(r"(?P<hours>\d{2}):(?P<minutes>\d{2}):(?P<seconds>\d{2})\.(?P<millis>\d{3})")


@dataclass
class Sample:
    elapsed_seconds: float
    playback_state: str
    loop_status: str
    position_text: str
    frame_text: str
    position_seconds: float | None
    frame_index: int | None


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("exe")
    parser.add_argument("media")
    parser.add_argument("--title", default="Frame Player")
    parser.add_argument("--slider-id", default="PositionSlider")
    parser.add_argument("--loop-status-id", default="LoopStatusTextBlock")
    parser.add_argument("--playback-state-id", default="PlaybackStateTextBlock")
    parser.add_argument("--position-id", default="CurrentPositionTextBlock")
    parser.add_argument("--frame-id", default="FrameNumberTextBox")
    parser.add_argument("--current-file-id", default="CurrentFileTextBlock")
    parser.add_argument("--play-button-id", default="PlayPauseButton")
    parser.add_argument("--ffprobe", default="Runtime\\ffmpeg-tools\\ffprobe.exe")
    parser.add_argument("--a-ratio", type=float, default=None)
    parser.add_argument("--b-ratio", type=float, default=None)
    parser.add_argument("--sample-count", type=int, default=20)
    parser.add_argument("--sample-interval", type=float, default=0.30)
    parser.add_argument("--required-wraps", type=int, default=2)
    parser.add_argument("--output", default="")
    return parser.parse_args()


def wait_until(predicate: Callable[[], bool], timeout_seconds: float, description: str) -> None:
    deadline = time.time() + timeout_seconds
    last_error = None
    while time.time() < deadline:
        try:
            if predicate():
                return
        except Exception as exc:  # pragma: no cover - diagnostics only
            last_error = exc
        time.sleep(0.10)
    if last_error is not None:
        raise RuntimeError(f"Timed out waiting for {description}: {last_error}")
    raise RuntimeError(f"Timed out waiting for {description}.")


def find_control(window, automation_id: str, control_type: str | None = None, timeout_seconds: float = 15.0):
    def resolve():
        kwargs = {"auto_id": automation_id}
        if control_type:
            kwargs["control_type"] = control_type
        control = window.child_window(**kwargs)
        return control.wrapper_object()

    result = [None]

    def try_resolve() -> bool:
        wrapper = resolve()
        if wrapper is None:
            return False
        result[0] = wrapper
        return True

    wait_until(try_resolve, timeout_seconds, f"control {automation_id}")
    return result[0]


def get_text(window, automation_id: str, control_type: str | None = None) -> str:
    control = find_control(window, automation_id, control_type=control_type)
    return (control.window_text() or "").strip()


def parse_time_seconds(value: str) -> float | None:
    match = TIME_PATTERN.search(value or "")
    if match is None:
        return None
    hours = int(match.group("hours"))
    minutes = int(match.group("minutes"))
    seconds = int(match.group("seconds"))
    millis = int(match.group("millis"))
    return hours * 3600.0 + minutes * 60.0 + seconds + millis / 1000.0


def parse_frame_index(value: str) -> int | None:
    digits = re.findall(r"\d+", value or "")
    if not digits:
        return None
    return int(digits[-1])


def get_menu_item(title: str, timeout_seconds: float = 8.0):
    desktop = Desktop(backend="uia")
    holder = [None]

    def resolve() -> bool:
        try:
            holder[0] = desktop.window(title=title, control_type="MenuItem", top_level_only=False).wrapper_object()
            return True
        except Exception:
            return False

    wait_until(resolve, timeout_seconds, f"menu item {title}")
    return holder[0]


def invoke_timeline_context_item(window, slider_id: str, ratio: float, menu_title: str):
    slider = find_control(window, slider_id, control_type="Slider")
    rect = slider.rectangle()
    width = max(1, rect.width())
    left_padding = min(12, max(4, width // 20))
    usable_width = max(1, width - (left_padding * 2))
    local_x = left_padding + int(usable_width * max(0.0, min(1.0, ratio)))
    local_y = rect.height() // 2
    slider.right_click_input(coords=(local_x, local_y))
    menu_item = get_menu_item(menu_title)
    enabled = menu_item.is_enabled()
    if enabled:
        menu_item.click_input()
    else:
        send_keys("{ESC}")
    return enabled


def wait_for_file_loaded(window, media_path: Path, current_file_id: str) -> None:
    media_name = media_path.name.lower()

    def is_ready() -> bool:
        text = get_text(window, current_file_id, control_type="Text").lower()
        return bool(text) and "no file loaded" not in text and media_name in text

    wait_until(is_ready, 45.0, f"media {media_path.name} to load")


def wait_for_position_b_ready(window, slider_id: str, ratio: float) -> None:
    def is_ready() -> bool:
        return invoke_timeline_context_item(window, slider_id, ratio, "Set Position B Here")

    wait_until(is_ready, 30.0, "timeline position B command to become available")


def click_loop_playback(window, slider_id: str, ratio: float) -> None:
    if not invoke_timeline_context_item(window, slider_id, ratio, "Loop Playback"):
        raise RuntimeError("Loop Playback was disabled in the timeline context menu.")


def click_play(window, play_button_id: str) -> None:
    play_button = find_control(window, play_button_id, control_type="Button")
    play_button.click_input()


def wait_for_loop_enabled(window, loop_status_id: str) -> None:
    def is_enabled() -> bool:
        status = get_text(window, loop_status_id, control_type="Text").lower()
        return bool(status) and "loop: off" not in status

    wait_until(is_enabled, 10.0, "loop playback to enable")


def probe_duration_seconds(ffprobe_path: Path, media_path: Path) -> float | None:
    if not ffprobe_path.exists():
        return None

    completed = subprocess.run(
        [
            str(ffprobe_path),
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "json",
            str(media_path),
        ],
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode != 0:
        return None

    try:
        payload = json.loads(completed.stdout or "{}")
        duration_text = payload.get("format", {}).get("duration")
        return float(duration_text) if duration_text is not None else None
    except Exception:
        return None


def resolve_loop_ratios(args: argparse.Namespace, media_path: Path) -> tuple[float, float]:
    if args.a_ratio is not None and args.b_ratio is not None:
        return args.a_ratio, args.b_ratio

    ffprobe_path = Path(args.ffprobe)
    if not ffprobe_path.is_absolute():
        ffprobe_path = Path.cwd() / ffprobe_path

    duration_seconds = probe_duration_seconds(ffprobe_path.resolve(), media_path)
    if duration_seconds is None or duration_seconds <= 0.5:
        return 0.20, 0.35

    end_margin = min(1.0, max(0.15, duration_seconds * 0.05))
    span = min(2.0, max(0.6, duration_seconds * 0.15))
    start_seconds = min(5.5, max(0.25, duration_seconds * 0.20))
    latest_start = max(0.10, duration_seconds - end_margin - span)
    start_seconds = min(start_seconds, latest_start)
    end_seconds = min(duration_seconds - end_margin, start_seconds + span)

    if end_seconds <= start_seconds:
        start_seconds = max(0.05, duration_seconds * 0.10)
        end_seconds = min(duration_seconds - 0.05, start_seconds + max(0.25, duration_seconds * 0.20))

    return start_seconds / duration_seconds, end_seconds / duration_seconds


def collect_samples(
    window,
    sample_count: int,
    sample_interval: float,
    playback_state_id: str,
    loop_status_id: str,
    position_id: str,
    frame_id: str,
) -> List[Sample]:
    samples: List[Sample] = []
    started_at = time.time()
    for _ in range(sample_count):
        playback_state = get_text(window, playback_state_id, control_type="Text")
        loop_status = get_text(window, loop_status_id, control_type="Text")
        position_text = get_text(window, position_id, control_type="Text")
        frame_text = get_text(window, frame_id, control_type="Edit")
        samples.append(
            Sample(
                elapsed_seconds=round(time.time() - started_at, 3),
                playback_state=playback_state,
                loop_status=loop_status,
                position_text=position_text,
                frame_text=frame_text,
                position_seconds=parse_time_seconds(position_text),
                frame_index=parse_frame_index(frame_text),
            ))
        time.sleep(sample_interval)
    return samples


def count_wraps(samples: List[Sample]) -> int:
    wraps = 0
    previous_position = None
    for sample in samples:
        current_position = sample.position_seconds
        if current_position is None:
            continue
        if previous_position is not None and current_position + 0.010 < previous_position:
            wraps += 1
        previous_position = current_position
    return wraps


def frames_moved(samples: List[Sample]) -> bool:
    frames = [sample.frame_index for sample in samples if sample.frame_index is not None]
    return len(set(frames)) >= 2


def playback_stayed_active(samples: List[Sample]) -> bool:
    states = [sample.playback_state.lower() for sample in samples]
    return all("playing" in state for state in states if state)


def build_result(samples: List[Sample], required_wraps: int) -> dict:
    wraps = count_wraps(samples)
    moved = frames_moved(samples)
    stayed_active = playback_stayed_active(samples)
    success = wraps >= required_wraps and moved and stayed_active
    return {
        "success": success,
        "required_wraps": required_wraps,
        "observed_wraps": wraps,
        "frames_moved": moved,
        "playback_stayed_active": stayed_active,
        "samples": [asdict(sample) for sample in samples],
    }


def main() -> int:
    args = parse_args()
    exe_path = Path(args.exe).resolve()
    media_path = Path(args.media).resolve()
    if not exe_path.exists():
        raise FileNotFoundError(exe_path)
    if not media_path.exists():
        raise FileNotFoundError(media_path)

    a_ratio, b_ratio = resolve_loop_ratios(args, media_path)
    command = f'"{exe_path}" --open-file "{media_path}"'
    app = Application(backend="uia").start(command)
    result = None
    try:
        time.sleep(2.0)
        window = app.top_window()
        window.wait("visible", timeout=30)
        window.set_focus()
        wait_for_file_loaded(window, media_path, args.current_file_id)

        if not invoke_timeline_context_item(window, args.slider_id, a_ratio, "Set Position A Here"):
            raise RuntimeError("Set Position A Here was disabled in the timeline context menu.")

        wait_for_position_b_ready(window, args.slider_id, b_ratio)
        click_loop_playback(window, args.slider_id, b_ratio)
        wait_for_loop_enabled(window, args.loop_status_id)
        if not invoke_timeline_context_item(window, args.slider_id, a_ratio, "Set Position A Here"):
            raise RuntimeError("Could not seek back to loop position A before playback.")
        click_play(window, args.play_button_id)

        samples = collect_samples(
            window,
            args.sample_count,
            args.sample_interval,
            args.playback_state_id,
            args.loop_status_id,
            args.position_id,
            args.frame_id)
        result = build_result(samples, args.required_wraps)
        result["a_ratio"] = a_ratio
        result["b_ratio"] = b_ratio

        if args.output:
            output_path = Path(args.output)
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(json.dumps(result, indent=2), encoding="utf-8")

        if result["success"]:
            print(json.dumps(result, indent=2))
            return 0

        print(json.dumps(result, indent=2))
        return 1
    finally:
        try:
            app.kill()
        except Exception:
            pass


if __name__ == "__main__":
    sys.exit(main())
