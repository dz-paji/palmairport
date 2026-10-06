#!/usr/bin/env python3
"""Run M7's local console gates serially and retain their complete evidence.

Usage: python3 Tools/test-m7-console.py [--evidence-dir PATH]
No Editor, real network listener, ADB, or cloud service is started. Existing
MONO_BIN, MCS_BIN, UNITY_CONTENTS, JDK_ROOT and ANDROID_JAR overrides still apply.
Every invocation creates a separate evidence directory; a failed gate does not
prevent the remaining gates from running. The suite returns the first nonzero
gate exit code (signal exits use the conventional 128 + signal number).
"""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parents[1]
GATES = (
    "simulation", "single-cycle", "net", "identity", "shift", "bot-memory",
    "progress", "settlement", "m6-video", "auth-protocol", "android-manifest",
)


def utc_now():
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds")


def suite_exit_code(results):
    for result in results:
        code = result.get("exit_code")
        if code is not None and code != 0:
            return code if code > 0 else 128 - code
    return 0


def write_summary(directory, summary):
    results = summary["gates"]
    summary["passed"] = sum(item["status"] == "passed" for item in results)
    summary["failed"] = sum(item["status"] == "failed" for item in results)
    summary["suite_exit_code"] = suite_exit_code(results)
    temporary = directory / "summary.json.tmp"
    temporary.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(directory / "summary.json")
    lines = [
        "M7 local console gates",
        "Status: " + summary["status"],
        "Started UTC: " + summary["started_utc"],
        "Evidence: " + str(directory),
        "Passed: %d; failed: %d; total: %d" % (summary["passed"], summary["failed"], len(results)),
    ]
    for item in results:
        code = item.get("exit_code")
        lines.append("%-18s %-7s exit=%-4s log=%s" % (
            item["name"], item["status"], "-" if code is None else code, item["log"],
        ))
    lines.append("Suite exit code: %d" % summary["suite_exit_code"])
    (directory / "summary.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence-dir", type=Path, default=ROOT / "Evidence/m7-console",
                        help="parent evidence directory (each run gets a fresh child)")
    args = parser.parse_args()
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ") + "-" + str(os.getpid())
    directory = args.evidence_dir.resolve() / run_id
    directory.mkdir(parents=True, exist_ok=False)
    commands = [(name, ["sh", "Tools/test-" + name + ".sh"]) for name in GATES]
    commands.append(("unity-compile", [sys.executable, "Tools/check-unity-compile.py"]))
    summary = {
        "schema_version": 1,
        "run_id": run_id,
        "project_root": str(ROOT),
        "started_utc": utc_now(),
        "status": "running",
        "execution": "serial local console; no Editor, listeners, ADB or cloud",
        "gates": [{"name": name, "command": command, "status": "pending",
                   "log": name + ".log", "exit_code_file": name + ".exitcode"}
                  for name, command in commands],
    }
    write_summary(directory, summary)
    print("Evidence: " + str(directory), flush=True)
    for item in summary["gates"]:
        item["started_utc"] = utc_now()
        item["status"] = "running"
        write_summary(directory, summary)
        started = time.monotonic()
        print("RUN " + item["name"], flush=True)
        with (directory / item["log"]).open("wb") as log:
            header = "Command: " + " ".join(item["command"]) + "\nStarted UTC: " + item["started_utc"] + "\n\n"
            log.write(header.encode("utf-8"))
            log.flush()
            try:
                result = subprocess.run(item["command"], cwd=ROOT, stdin=subprocess.DEVNULL,
                                        stdout=log, stderr=subprocess.STDOUT, check=False)
                item["exit_code"] = result.returncode
            except OSError as error:
                item["exit_code"] = 127
                item["launch_error"] = str(error)
                log.write(("Could not launch gate: " + str(error) + "\n").encode("utf-8"))
            item["finished_utc"] = utc_now()
            item["duration_seconds"] = round(time.monotonic() - started, 3)
            item["status"] = "passed" if item["exit_code"] == 0 else "failed"
            log.write(("\nExit code: %d\nFinished UTC: %s\n" % (
                item["exit_code"], item["finished_utc"])).encode("utf-8"))
        (directory / item["exit_code_file"]).write_text(str(item["exit_code"]) + "\n", encoding="utf-8")
        write_summary(directory, summary)
        print("%s %s (exit %d)" % (item["status"].upper(), item["name"], item["exit_code"]), flush=True)
    summary["finished_utc"] = utc_now()
    summary["status"] = "failed" if suite_exit_code(summary["gates"]) else "passed"
    write_summary(directory, summary)
    print("Passed %d/%d; suite exit %d" % (
        summary["passed"], len(summary["gates"]), summary["suite_exit_code"]), flush=True)
    return summary["suite_exit_code"]


if __name__ == "__main__":
    raise SystemExit(main())
