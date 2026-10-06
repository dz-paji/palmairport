#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
GAME_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

if [ -n "${MONO_BIN:-}" ]; then
    MONO_RUNTIME="$MONO_BIN"
else
    MONO_RUNTIME=""
    for candidate in \
        /Applications/Tuanjie/Hub/Editor/*/Tuanjie.app/Contents/MonoBleedingEdge/bin/mono \
        /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MonoBleedingEdge/bin/mono
    do
        if [ -x "$candidate" ]; then MONO_RUNTIME="$candidate"; break; fi
    done
    if [ -z "$MONO_RUNTIME" ]; then MONO_RUNTIME=$(command -v mono || true); fi
fi

if [ -n "${MCS_BIN:-}" ]; then
    MCS_COMPILER="$MCS_BIN"
else
    MCS_COMPILER=""
    for candidate in \
        /Applications/Tuanjie/Hub/Editor/*/Tuanjie.app/Contents/MonoBleedingEdge/bin/mcs \
        /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MonoBleedingEdge/bin/mcs
    do
        if [ -x "$candidate" ]; then MCS_COMPILER="$candidate"; break; fi
    done
    if [ -z "$MCS_COMPILER" ]; then MCS_COMPILER=$(command -v mcs || true); fi
fi

if [ -z "$MONO_RUNTIME" ] || [ ! -x "$MONO_RUNTIME" ]; then
    echo "Could not find mono. Set MONO_BIN to the mono executable." >&2
    exit 2
fi
if [ -z "$MCS_COMPILER" ] || [ ! -x "$MCS_COMPILER" ]; then
    echo "Could not find mcs. Set MCS_BIN to the mcs executable." >&2
    exit 2
fi

TEST_TMP_DIR=$(mktemp -d "${TMPDIR:-/tmp}/palm-settlement-tests.XXXXXX")
trap 'rm -rf "$TEST_TMP_DIR"' EXIT HUP INT TERM

# RoomPhase belongs to RoomManager; compile its actual pure core dependencies.
"$MCS_COMPILER" -nologo -out:"$TEST_TMP_DIR/SettlementEligibilityTests.exe" \
    "$GAME_DIR"/Assets/Scripts/Core/Net/*.cs \
    "$GAME_DIR"/Assets/Scripts/Core/Shift/*.cs \
    "$GAME_DIR/Assets/Scripts/Core/AirportSimulation.cs" \
    "$GAME_DIR/Assets/Scripts/Core/Results/SettlementEligibility.cs" \
    "$GAME_DIR/Assets/Scripts/Core/Results/ReplayExportTimeline.cs" \
    "$GAME_DIR/Tests/SettlementEligibilityTests.cs"
"$MONO_RUNTIME" "$TEST_TMP_DIR/SettlementEligibilityTests.exe"
