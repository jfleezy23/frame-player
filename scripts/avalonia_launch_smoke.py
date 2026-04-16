#!/usr/bin/env python3
import argparse
import json
import subprocess
import sys
import time
from pathlib import Path


SUPPORTED_EXTENSIONS = {".avi", ".m4v", ".mkv", ".mov", ".mp4", ".wmv"}


def is_supported_media_file(path):
    return path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS


def collect_candidates(path):
    if path.is_dir():
        return [
            candidate
            for candidate in sorted(path.rglob("*"))
            if is_supported_media_file(candidate)
        ]

    if is_supported_media_file(path):
        return [path]

    return []


def collect_files(paths):
    collected = []
    for raw_path in paths:
        path = Path(raw_path)
        collected.extend(collect_candidates(path))

    unique = []
    seen = set()
    for candidate in collected:
        normalized = str(candidate.resolve()).lower()
        if normalized not in seen:
            seen.add(normalized)
            unique.append(candidate.resolve())
    return unique


def launch_smoke(exe_path, media_path, hold_seconds):
    process = None
    start_time = time.time()
    try:
        process = subprocess.Popen(
            [str(exe_path), str(media_path)],
            cwd=str(exe_path.parent),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            creationflags=getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0),
        )

        deadline = start_time + hold_seconds
        while time.time() < deadline:
            if process.poll() is not None:
                return {
                    "file": str(media_path),
                    "status": "failed",
                    "exit_code": process.returncode,
                    "message": "Process exited before smoke window elapsed.",
                    "elapsed_seconds": round(time.time() - start_time, 3),
                }
            time.sleep(0.25)

        return {
            "file": str(media_path),
            "status": "passed",
            "exit_code": None,
            "message": "Process stayed alive for smoke window.",
            "elapsed_seconds": round(time.time() - start_time, 3),
        }
    finally:
        if process is not None and process.poll() is None:
            try:
                process.terminate()
                process.wait(timeout=5)
            except Exception:
                try:
                    process.kill()
                    process.wait(timeout=5)
                except Exception:
                    pass


def main():
    parser = argparse.ArgumentParser(description="Launch-smoke the Avalonia host against supported media files.")
    parser.add_argument("--exe", required=True, help="Path to FramePlayer.Avalonia.exe")
    parser.add_argument("--hold-seconds", type=float, default=5.0, help="Seconds the app must remain alive")
    parser.add_argument("--output", required=True, help="Path to write a JSON report")
    parser.add_argument("paths", nargs="+", help="Media files or directories to test")
    args = parser.parse_args()

    exe_path = Path(args.exe).resolve()
    if not exe_path.is_file():
        print("Executable not found: {0}".format(exe_path), file=sys.stderr)
        return 2

    media_files = collect_files(args.paths)
    if not media_files:
        print("No supported media files were found.", file=sys.stderr)
        return 3

    results = []
    for media_file in media_files:
        print("Smoke launching {0}".format(media_file))
        result = launch_smoke(exe_path, media_file, args.hold_seconds)
        results.append(result)
        status_label = result["status"].upper()
        print("[{0}] {1}".format(status_label, media_file))

    passed = sum(1 for result in results if result["status"] == "passed")
    failed = len(results) - passed
    report = {
        "generated_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "executable": str(exe_path),
        "hold_seconds": args.hold_seconds,
        "passed": passed,
        "failed": failed,
        "results": results,
    }

    output_path = Path(args.output).resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print("Wrote report to {0}".format(output_path))
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
