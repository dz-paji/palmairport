#!/bin/sh
# 难度基准：真实 BotInput 跑单项任务 + 整局 300s，产物写入 Evidence/difficulty/。
# 编辑器不能同时打开本项目（项目锁）。不截 RT 图，可加 -nographics。
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
GAME_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
EDITOR=${UNITY_EDITOR:-/Applications/Tuanjie/Hub/Editor/2022.3.61t14/Tuanjie.app/Contents/MacOS/Tuanjie}
LOG=${TMPDIR:-/tmp}/palmbay-difficulty.log
"$EDITOR" -batchmode -nographics -projectPath "$GAME_DIR" -executeMethod DifficultyBenchmark.Run -logFile "$LOG" || {
    echo "Difficulty benchmark failed (log: $LOG)" >&2; exit 1; }
grep -q "DIFFICULTY_BENCHMARK_PASSED" "$LOG" || { echo "No PASS marker (log: $LOG)" >&2; exit 1; }
echo "PASS: $GAME_DIR/Evidence/difficulty/difficulty-metrics.md"
