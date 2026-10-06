#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
GAME_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
TEST_FILE="$GAME_DIR/Tests/IdentityTests.cs"

if [ ! -f "$TEST_FILE" ]; then
    echo "Identity test sources are missing." >&2
    exit 2
fi

# MONO_BIN can point at any compatible mono runtime. MCS_BIN is also accepted
# for callers that need to override the compiler independently.
if [ -n "${MONO_BIN:-}" ]; then
    MONO_RUNTIME="$MONO_BIN"
else
    MONO_RUNTIME=""
    for candidate in \
        "/Applications/Tuanjie/Hub/Editor/2022.3.61t14/Tuanjie.app/Contents/MonoBleedingEdge/bin/mono" \
        /Applications/Tuanjie/Hub/Editor/*/Tuanjie.app/Contents/MonoBleedingEdge/bin/mono \
        /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MonoBleedingEdge/bin/mono
    do
        if [ -x "$candidate" ]; then
            MONO_RUNTIME="$candidate"
            break
        fi
    done
    if [ -z "$MONO_RUNTIME" ]; then
        MONO_RUNTIME=$(command -v mono || true)
    fi
fi

if [ -n "${MCS_BIN:-}" ]; then
    MCS_COMPILER="$MCS_BIN"
else
    MCS_COMPILER=""
    for candidate in \
        "/Applications/Tuanjie/Hub/Editor/2022.3.61t14/Tuanjie.app/Contents/MonoBleedingEdge/bin/mcs" \
        /Applications/Tuanjie/Hub/Editor/*/Tuanjie.app/Contents/MonoBleedingEdge/bin/mcs \
        /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MonoBleedingEdge/bin/mcs
    do
        if [ -x "$candidate" ]; then
            MCS_COMPILER="$candidate"
            break
        fi
    done
    if [ -z "$MCS_COMPILER" ]; then
        MCS_COMPILER=$(command -v mcs || true)
    fi
fi

if [ -z "$MONO_RUNTIME" ] || [ ! -x "$MONO_RUNTIME" ]; then
    echo "Could not find mono. Set MONO_BIN to the mono executable." >&2
    exit 2
fi
if [ -z "$MCS_COMPILER" ] || [ ! -x "$MCS_COMPILER" ]; then
    echo "Could not find mcs. Set MCS_BIN to the mcs executable." >&2
    exit 2
fi

TMP_DIR=$(mktemp -d "${TMPDIR:-/tmp}/palm-identity-tests.XXXXXX")
trap 'rm -rf "$TMP_DIR"' EXIT HUP INT TERM

# Core/Shift + AirportSimulation：Ver=2 快照（NetProtocol）引用 Shift/Flights 镜像块类型。
# shellcheck disable=SC2086
"$MCS_COMPILER" -nologo -out:"$TMP_DIR/IdentityTests.exe" \
    "$GAME_DIR"/Assets/Scripts/Core/Net/*.cs \
    "$GAME_DIR"/Assets/Scripts/Core/Auth/*.cs \
    "$GAME_DIR"/Assets/Scripts/Core/Shift/*.cs \
    "$GAME_DIR/Assets/Scripts/Core/AirportSimulation.cs" \
    "$GAME_DIR/Assets/Scripts/Core/GameEvents.cs" \
    "$GAME_DIR/Assets/Scripts/Identity/AgeVerificationStore.cs" \
    "$TEST_FILE"
"$MONO_RUNTIME" "$TMP_DIR/IdentityTests.exe"
