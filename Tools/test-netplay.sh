#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
GAME_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
FUEL_ONLY=${NETPLAY_FUEL_ONLY:-0}
BOARDING_ONLY=${NETPLAY_BOARDING_ONLY:-0}
case "$FUEL_ONLY:$BOARDING_ONLY" in
    0:0) SESSIONS="1 2 3 4 5 6"; EVIDENCE_DIR="$GAME_DIR/Evidence" ;;
    1:0) SESSIONS="5"; EVIDENCE_DIR="$GAME_DIR/Evidence/netplay-fuel-only" ;;
    0:1) SESSIONS="6"; EVIDENCE_DIR="$GAME_DIR/Evidence/netplay-boarding-only" ;;
    *) echo "NETPLAY_FUEL_ONLY and NETPLAY_BOARDING_ONLY must be 0 or 1 and cannot both be 1." >&2; exit 2 ;;
esac
DEFAULT_EDITOR="/Applications/Tuanjie/Hub/Editor/2022.3.61t14/Tuanjie.app/Contents/MacOS/Tuanjie"
EDITOR_BIN=${TUANJIE_BIN:-${UNITY_BIN:-$DEFAULT_EDITOR}}
JOIN_ADDRESS=${PALMBAY_JOIN:-127.0.0.1}
TIMEOUT_SECONDS=${NETPLAY_TIMEOUT_SECONDS:-300}
KEEP_TEMP=${NETPLAY_KEEP_TEMP:-0}

case "$TIMEOUT_SECONDS" in
    ''|*[!0-9]*) echo "NETPLAY_TIMEOUT_SECONDS must be a positive integer." >&2; exit 2 ;;
esac
if [ "$TIMEOUT_SECONDS" -lt 60 ]; then
    echo "NETPLAY_TIMEOUT_SECONDS must be at least 60." >&2
    exit 2
fi
if [ ! -x "$EDITOR_BIN" ]; then
    echo "Could not find the Tuanjie editor at: $EDITOR_BIN" >&2
    echo "Set TUANJIE_BIN to the installed editor executable." >&2
    exit 2
fi
if ! command -v rsync >/dev/null 2>&1; then
    echo "rsync is required to create credential-filtered project copies." >&2
    exit 2
fi
if ! command -v perl >/dev/null 2>&1; then
    echo "perl is required to isolate project PlayerPrefs identities." >&2
    exit 2
fi

mkdir -p "$EVIDENCE_DIR"
TEMP_ROOT=$(mktemp -d "${TMPDIR:-/tmp}/palmbay-netplay.XXXXXX")
TEMP_ROOT=$(CDPATH= cd -- "$TEMP_ROOT" && pwd -P)
RUN_TAG=${TEMP_ROOT##*.}
case "$RUN_TAG" in
    ''|*[!A-Za-z0-9]*)
        echo "mktemp returned a run tag that is not alphanumeric." >&2
        rm -rf "$TEMP_ROOT"
        exit 2
        ;;
esac
CONTROL_DIR="$TEMP_ROOT/control"
MARKER_PATH="$TEMP_ROOT/.netplay-test-marker"
HOST_PROJECT="$TEMP_ROOT/host"
CLIENT_PROJECT="$TEMP_ROOT/client"
mkdir -p "$CONTROL_DIR"
printf 'PALMBAY_NETPLAY_TEST:%s' "$RUN_TAG" > "$MARKER_PATH"
LAUNCHED=""
FAILURE_REASON=""
HOST1_CODE="not-started"
CLIENT1_CODE="not-started"
HOST2_CODE="not-started"
CLIENT2_CODE="not-started"
HOST3_CODE="not-started"
CLIENT3_CODE="not-started"
HOST4_CODE="not-started"
CLIENT4_CODE="not-started"
HOST5_CODE="not-started"
CLIENT5_CODE="not-started"
HOST6_CODE="not-started"
CLIENT6_CODE="not-started"

prepare_project() {
    project="$1"
    mkdir -p "$project/Assets"
    rsync -a \
        --exclude='/Resources/palmbay-auth.json' \
        --exclude='/Resources/palmbay-auth.json.meta' \
        --exclude='/google-services.json' \
        --exclude='/google-services.json.meta' \
        "$GAME_DIR/Assets/" "$project/Assets/"
    rsync -a "$GAME_DIR/Packages" "$GAME_DIR/ProjectSettings" "$GAME_DIR/UIElementsSchema" "$GAME_DIR/.codely.packages" "$project/"
    if [ -e "$project/Assets/Resources/palmbay-auth.json" ] || \
        [ -e "$project/Assets/Resources/palmbay-auth.json.meta" ] || \
        [ -e "$project/Assets/google-services.json" ] || \
        [ -e "$project/Assets/google-services.json.meta" ]; then
        echo "Credential filtering failed for project copy: $project" >&2
        return 1
    fi
    if ! grep -F '"cn.tuanjie.codely.bridge": "file:../.codely.packages/' "$project/Packages/manifest.json" >/dev/null; then
        echo "Expected the original local Codely package path in $project/Packages/manifest.json" >&2
        return 1
    fi
    if [ ! -d "$project/.codely.packages" ]; then
        echo "Local Codely package source was not copied to $project/.codely.packages" >&2
        return 1
    fi
    mkdir -p "$project/Library" "$project/Temp" "$project/Logs" "$project/Evidence"
}

set_project_identity() {
    project="$1"
    identity="$2"
    case "$identity" in
        host*) rm -f "$project/Evidence/net-host.txt" ;;
        client*) rm -f "$project/Evidence/net-client.txt" ;;
    esac
    company="Palm Bay Netplaytest $RUN_TAG $identity"
    product="Netplay-$RUN_TAG-$identity"
    NETPLAY_COMPANY="$company" NETPLAY_PRODUCT="$product" perl -0pi -e \
        's/^([\t ]*)companyName:.*$/${1}companyName: $ENV{NETPLAY_COMPANY}/m; s/^([\t ]*)productName:.*$/${1}productName: $ENV{NETPLAY_PRODUCT}/m' \
        "$project/ProjectSettings/ProjectSettings.asset"
    actual_company=$(sed -n 's/^[[:space:]]*companyName: //p' "$project/ProjectSettings/ProjectSettings.asset")
    actual_product=$(sed -n 's/^[[:space:]]*productName: //p' "$project/ProjectSettings/ProjectSettings.asset")
    if [ "$actual_company" != "$company" ] || [ "$actual_product" != "$product" ]; then
        echo "Could not apply isolated company/product identity to $project" >&2
        return 1
    fi
    printf '%s\n%s\n' "$company" "$product" > "$TEMP_ROOT/$identity.identity"
}

identity_value() {
    file="$TEMP_ROOT/$1.identity"
    sed -n "$2"p "$file"
}

report_matches_session() {
    role_name="$1"
    session="$2"
    source_report="$3"
    case "$session" in
        1) expected_scenario="$role_name-movement" ;;
        2) expected_scenario="$role_name-departure" ;;
        3) expected_scenario="$role_name-meal" ;;
        4) expected_scenario="$role_name-baggage" ;;
        5) expected_scenario="$role_name-fuel" ;;
        6) expected_scenario="$role_name-boarding" ;;
        *) return 1 ;;
    esac
    [ -f "$source_report" ] && grep -F "Diagnostics: scenario=$expected_scenario," "$source_report" >/dev/null
}

cleanup_identity() {
    identity="$1"
    if [ -z "${HOME:-}" ] || [ ! -f "$TEMP_ROOT/$identity.identity" ]; then
        return
    fi
    company=$(identity_value "$identity" 1)
    product=$(identity_value "$identity" 2)
    rm -rf "$HOME/Library/Application Support/$company/$product"
    rm -f "$HOME/Library/Preferences/unity.$company.$product.plist"
}

ensure_report() {
    role_name="$1"
    session="$2"
    project="$3"
    source_report="$project/Evidence/net-$role_name.txt"
    destination="$EVIDENCE_DIR/net-$role_name-session$session.txt"
    process_name="$role_name$session"
    if [ -f "$destination" ]; then return; fi
    if report_matches_session "$role_name" "$session" "$source_report"; then
        cp "$source_report" "$destination"
    else
        printf 'FAIL: no Unity evidence report was generated. Process exit code: %s. See Evidence/net-%s-session%s.log.\n' \
            "$(process_exit_code "$process_name")" "$role_name" "$session" > "$destination"
    fi
}

ensure_all_reports() {
    for session in $SESSIONS; do
        ensure_report host "$session" "$HOST_PROJECT"
        ensure_report client "$session" "$CLIENT_PROJECT"
    done
    for role_name in host client; do
        {
            for session in $SESSIONS; do
                printf 'Session %s\n' "$session"
                cat "$EVIDENCE_DIR/net-$role_name-session$session.txt"
                printf '\n'
            done
        } > "$EVIDENCE_DIR/net-$role_name.txt"
    done
}

launch_editor() {
    name="$1"
    project="$2"
    method="$3"
    log_path="$4"
    identity="$5"
    graphics="${6:-0}"
    editor_pid_file="$TEMP_ROOT/$name.editor.pid"
    wrapper_pid_file="$TEMP_ROOT/$name.wrapper.pid"
    exit_file="$TEMP_ROOT/$name.exit"
    : > "$log_path"
    (
        set +e
        cd "$project" || exit 98
        case "$name" in
            host*) rm -f "$project/Evidence/net-host.txt" ;;
            client*) rm -f "$project/Evidence/net-client.txt" ;;
        esac
        unset PALMBAY_FIREBASE_KEY
        unset GOOGLE_APPLICATION_CREDENTIALS
        unset FIREBASE_CONFIG
        export PALMBAY_NETPLAY_ROOT="$TEMP_ROOT"
        export PALMBAY_NETPLAY_MARKER="$MARKER_PATH"
        export PALMBAY_NETPLAY_RUN_TAG="$RUN_TAG"
        export PALMBAY_NETPLAY_CONTROL_DIR="$CONTROL_DIR"
        export PALMBAY_NETPLAY_ROLE_TAG="$name"
        if [ "$name" = "client1" ] || [ "$name" = "client2" ] || [ "$name" = "client3" ] || [ "$name" = "client4" ] || [ "$name" = "client5" ] || [ "$name" = "client6" ]; then
            export PALMBAY_JOIN="$JOIN_ADDRESS"
        else
            unset PALMBAY_JOIN
        fi
        # graphics=1 时不加 -nographics：sessions 3-6 需要 RenderTexture 双端截图（禁 RT 于 -nographics）。
        if [ "$graphics" = "1" ]; then
            "$EDITOR_BIN" -batchmode -projectPath "$project" -executeMethod "NetPlaytest.$method" -logFile "$log_path" &
        else
            "$EDITOR_BIN" -batchmode -nographics -projectPath "$project" -executeMethod "NetPlaytest.$method" -logFile "$log_path" &
        fi
        editor_pid=$!
        printf '%s\n' "$editor_pid" > "$editor_pid_file"
        wait "$editor_pid"
        exit_code=$?
        printf '%s\n' "$exit_code" > "$exit_file"
        exit "$exit_code"
    ) &
    wrapper_pid=$!
    printf '%s\n' "$wrapper_pid" > "$wrapper_pid_file"
    LAUNCHED="$LAUNCHED $name"
    for attempt in 1 2 3 4 5 6 7 8 9 10; do
        if [ -f "$editor_pid_file" ] || [ -f "$exit_file" ]; then return 0; fi
        sleep 1
    done
    FAILURE_REASON="$name Unity process did not start."
    return 1
}

process_exit_code() {
    name="$1"
    file="$TEMP_ROOT/$name.exit"
    if [ -f "$file" ]; then
        cat "$file"
    else
        echo "not-exited"
    fi
}

wait_for_signal() {
    name="$1"
    signal="$2"
    deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    while [ ! -f "$CONTROL_DIR/$signal" ]; do
        if [ -f "$TEMP_ROOT/$name.exit" ]; then
            FAILURE_REASON="$name exited with code $(process_exit_code "$name") before $signal."
            return 1
        fi
        if [ "$(date +%s)" -ge "$deadline" ]; then
            FAILURE_REASON="Timed out waiting for $signal from $name."
            return 1
        fi
        sleep 1
    done
    return 0
}

wait_for_pair() {
    host_name="$1"
    client_name="$2"
    deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    while [ ! -f "$TEMP_ROOT/$host_name.exit" ] || [ ! -f "$TEMP_ROOT/$client_name.exit" ]; do
        if [ "$(date +%s)" -ge "$deadline" ]; then
            FAILURE_REASON="Timed out waiting for $host_name and $client_name to exit."
            return 1
        fi
        sleep 1
    done
    host_code=$(process_exit_code "$host_name")
    client_code=$(process_exit_code "$client_name")
    if [ "$host_name" = "host1" ]; then HOST1_CODE="$host_code"; CLIENT1_CODE="$client_code"; fi
    if [ "$host_name" = "host2" ]; then HOST2_CODE="$host_code"; CLIENT2_CODE="$client_code"; fi
    if [ "$host_name" = "host3" ]; then HOST3_CODE="$host_code"; CLIENT3_CODE="$client_code"; fi
    if [ "$host_name" = "host4" ]; then HOST4_CODE="$host_code"; CLIENT4_CODE="$client_code"; fi
    if [ "$host_name" = "host5" ]; then HOST5_CODE="$host_code"; CLIENT5_CODE="$client_code"; fi
    if [ "$host_name" = "host6" ]; then HOST6_CODE="$host_code"; CLIENT6_CODE="$client_code"; fi
    if [ "$host_code" != "0" ] || [ "$client_code" != "0" ]; then
        FAILURE_REASON="$host_name exit=$host_code; $client_name exit=$client_code."
        return 1
    fi
    return 0
}

record_failure_detail() {
    failure_detail="$1"
    if [ -z "$FAILURE_REASON" ]; then
        FAILURE_REASON="$failure_detail"
    else
        FAILURE_REASON="$FAILURE_REASON; $failure_detail"
    fi
}

collect_report() {
    role_name="$1"
    session="$2"
    project="$3"
    source_report="$project/Evidence/net-$role_name.txt"
    destination="$EVIDENCE_DIR/net-$role_name-session$session.txt"
    aggregate="$EVIDENCE_DIR/net-$role_name.txt"
    if report_matches_session "$role_name" "$session" "$source_report"; then
        cp "$source_report" "$destination"
        {
            printf 'Session %s\n' "$session"
            cat "$source_report"
            printf '\n'
        } >> "$aggregate"
    else
        printf 'FAIL: Unity did not write %s for session %s.\n' "$role_name" "$session" > "$destination"
        {
            printf 'Session %s\n' "$session"
            cat "$destination"
            printf '\n'
        } >> "$aggregate"
    fi
}

terminate_owned_processes() {
    for name in $LAUNCHED; do
        if [ -f "$TEMP_ROOT/$name.exit" ]; then continue; fi
        if [ -f "$TEMP_ROOT/$name.editor.pid" ]; then
            pid=$(cat "$TEMP_ROOT/$name.editor.pid")
            case "$pid" in ''|*[!0-9]*) ;; *) kill -TERM "$pid" 2>/dev/null || true ;; esac
        fi
    done
    attempt=0
    while [ "$attempt" -lt 5 ]; do
        any_active=0
        for name in $LAUNCHED; do
            if [ ! -f "$TEMP_ROOT/$name.exit" ]; then any_active=1; fi
        done
        if [ "$any_active" -eq 0 ]; then break; fi
        sleep 1
        attempt=$((attempt + 1))
    done
    for name in $LAUNCHED; do
        if [ -f "$TEMP_ROOT/$name.exit" ]; then continue; fi
        if [ -f "$TEMP_ROOT/$name.editor.pid" ]; then
            pid=$(cat "$TEMP_ROOT/$name.editor.pid")
            case "$pid" in ''|*[!0-9]*) ;; *) kill -KILL "$pid" 2>/dev/null || true ;; esac
        fi
        if [ -f "$TEMP_ROOT/$name.wrapper.pid" ]; then
            pid=$(cat "$TEMP_ROOT/$name.wrapper.pid")
            case "$pid" in ''|*[!0-9]*) ;; *) kill -TERM "$pid" 2>/dev/null || true ;; esac
        fi
    done
    for name in $LAUNCHED; do
        if [ -f "$TEMP_ROOT/$name.wrapper.pid" ]; then
            pid=$(cat "$TEMP_ROOT/$name.wrapper.pid")
            case "$pid" in ''|*[!0-9]*) ;; *) wait "$pid" 2>/dev/null || true ;; esac
        fi
    done
}

cleanup() {
    exit_code=$?
    trap - EXIT HUP INT TERM
    terminate_owned_processes
    ensure_all_reports
    if [ -f "$EVIDENCE_DIR/m3-meal-captures.txt" ] && [ ! -s "$EVIDENCE_DIR/m3-meal-captures.txt" ]; then
        printf 'Session 3 did not reach meal capture collection; see netplay-summary.txt and session 3 reports.\n' \
            > "$EVIDENCE_DIR/m3-meal-captures.txt"
    fi
    if [ -f "$EVIDENCE_DIR/m3-baggage-captures.txt" ] && [ ! -s "$EVIDENCE_DIR/m3-baggage-captures.txt" ]; then
        printf 'Session 4 did not reach baggage capture collection; see netplay-summary.txt and session 4 reports.\n' \
            > "$EVIDENCE_DIR/m3-baggage-captures.txt"
    fi
    if [ -f "$EVIDENCE_DIR/m3-fuel-captures.txt" ] && [ ! -s "$EVIDENCE_DIR/m3-fuel-captures.txt" ]; then
        printf 'Session 5 did not reach fuel capture collection; see netplay-summary.txt and session 5 reports.\n' \
            > "$EVIDENCE_DIR/m3-fuel-captures.txt"
    fi
    if [ -f "$EVIDENCE_DIR/m3-boarding-captures.txt" ] && [ ! -s "$EVIDENCE_DIR/m3-boarding-captures.txt" ]; then
        printf 'Session 6 did not reach boarding capture collection; see netplay-summary.txt and session 6 reports.\n' \
            > "$EVIDENCE_DIR/m3-boarding-captures.txt"
    fi
    if [ -f "$TEMP_ROOT/host1.exit" ]; then HOST1_CODE=$(process_exit_code host1); fi
    if [ -f "$TEMP_ROOT/client1.exit" ]; then CLIENT1_CODE=$(process_exit_code client1); fi
    if [ -f "$TEMP_ROOT/host2.exit" ]; then HOST2_CODE=$(process_exit_code host2); fi
    if [ -f "$TEMP_ROOT/client2.exit" ]; then CLIENT2_CODE=$(process_exit_code client2); fi
    if [ -f "$TEMP_ROOT/host3.exit" ]; then HOST3_CODE=$(process_exit_code host3); fi
    if [ -f "$TEMP_ROOT/client3.exit" ]; then CLIENT3_CODE=$(process_exit_code client3); fi
    if [ -f "$TEMP_ROOT/host4.exit" ]; then HOST4_CODE=$(process_exit_code host4); fi
    if [ -f "$TEMP_ROOT/client4.exit" ]; then CLIENT4_CODE=$(process_exit_code client4); fi
    if [ -f "$TEMP_ROOT/host5.exit" ]; then HOST5_CODE=$(process_exit_code host5); fi
    if [ -f "$TEMP_ROOT/client5.exit" ]; then CLIENT5_CODE=$(process_exit_code client5); fi
    if [ -f "$TEMP_ROOT/host6.exit" ]; then HOST6_CODE=$(process_exit_code host6); fi
    if [ -f "$TEMP_ROOT/client6.exit" ]; then CLIENT6_CODE=$(process_exit_code client6); fi
    cleanup_identity host1
    cleanup_identity client1
    cleanup_identity host2
    cleanup_identity client2
    cleanup_identity host3
    cleanup_identity client3
    cleanup_identity host4
    cleanup_identity client4
    cleanup_identity host5
    cleanup_identity client5
    cleanup_identity host6
    cleanup_identity client6
    if [ "$KEEP_TEMP" = "1" ]; then
        echo "Netplay temporary projects kept at: $TEMP_ROOT"
    else
        rm -rf "$TEMP_ROOT"
    fi
    if [ "$exit_code" -ne 0 ]; then
        if [ -z "$FAILURE_REASON" ]; then FAILURE_REASON="Launcher exited with code $exit_code."; fi
        write_summary FAIL
    fi
    exit "$exit_code"
}
trap cleanup EXIT
trap 'FAILURE_REASON="Received SIGHUP."; exit 129' HUP
trap 'FAILURE_REASON="Received SIGINT."; exit 130' INT
trap 'FAILURE_REASON="Received SIGTERM."; exit 143' TERM

write_summary() {
    summary="$EVIDENCE_DIR/netplay-summary.txt"
    {
        if [ "$FUEL_ONLY" = "1" ]; then
            printf 'M3.3 fuel-only Unity UDP integration (session 5 subset): %s\n' "$1"
            printf 'Sessions 1-4 and 6: SKIPPED in this run; no full regression claim.\n'
        elif [ "$BOARDING_ONLY" = "1" ]; then
            printf 'M3.4 boarding-only Unity UDP integration (session 6 subset): %s\n' "$1"
            printf 'Sessions 1-5: SKIPPED in this run; no full regression claim.\n'
        else
            printf 'M2/M3 six-process-pair Unity UDP integration: %s\n' "$1"
        fi
        printf 'Session 1 host exit code: %s\n' "$HOST1_CODE"
        printf 'Session 1 client exit code: %s\n' "$CLIENT1_CODE"
        printf 'Session 2 host exit code: %s\n' "$HOST2_CODE"
        printf 'Session 2 client exit code: %s\n' "$CLIENT2_CODE"
        printf 'Session 3 (meal) host exit code: %s\n' "$HOST3_CODE"
        printf 'Session 3 (meal) client exit code: %s\n' "$CLIENT3_CODE"
        printf 'Session 4 (baggage) host exit code: %s\n' "$HOST4_CODE"
        printf 'Session 4 (baggage) client exit code: %s\n' "$CLIENT4_CODE"
        printf 'Session 5 (fuel) host exit code: %s\n' "$HOST5_CODE"
        printf 'Session 5 (fuel) client exit code: %s\n' "$CLIENT5_CODE"
        printf 'Session 6 (boarding) host exit code: %s\n' "$HOST6_CODE"
        printf 'Session 6 (boarding) client exit code: %s\n' "$CLIENT6_CODE"
        if [ -n "$FAILURE_REASON" ]; then printf 'Failure: %s\n' "$FAILURE_REASON"; fi
        printf 'Host logs: Evidence/net-host-session1.log Evidence/net-host-session2.log Evidence/net-host-session3.log Evidence/net-host-session4.log Evidence/net-host-session5.log Evidence/net-host-session6.log\n'
        printf 'Client logs: Evidence/net-client-session1.log Evidence/net-client-session2.log Evidence/net-client-session3.log Evidence/net-client-session4.log Evidence/net-client-session5.log Evidence/net-client-session6.log\n'
        printf 'Fuel captures: Evidence/m3-fuel-captures.txt\n'
        printf 'Boarding captures: Evidence/m3-boarding-captures.txt\n'
        if [ "$KEEP_TEMP" = "1" ]; then printf 'Temporary projects: %s\n' "$TEMP_ROOT"; fi
    } > "$summary"
}

rm -f "$EVIDENCE_DIR/netplay-summary.txt" "$EVIDENCE_DIR/m3-meal-captures.txt" \
    "$EVIDENCE_DIR/m3-baggage-captures.txt" "$EVIDENCE_DIR/m3-fuel-captures.txt" "$EVIDENCE_DIR/m3-boarding-captures.txt"
rm -f "$EVIDENCE_DIR"/net-host-session*.txt "$EVIDENCE_DIR"/net-client-session*.txt \
    "$EVIDENCE_DIR"/m3-meal-*.png "$EVIDENCE_DIR"/m3-baggage-*.png "$EVIDENCE_DIR"/m3-fuel-*.png "$EVIDENCE_DIR"/m3-boarding-*.png "$EVIDENCE_DIR"/m3-boarding-*-snapshot.txt
: > "$EVIDENCE_DIR/net-host.txt"
: > "$EVIDENCE_DIR/net-client.txt"
for session in $SESSIONS; do
    : > "$EVIDENCE_DIR/net-host-session$session.log"
    : > "$EVIDENCE_DIR/net-client-session$session.log"
done
if [ "$FUEL_ONLY" = "0" ] && [ "$BOARDING_ONLY" = "0" ]; then
    : > "$EVIDENCE_DIR/m3-meal-captures.txt"
    : > "$EVIDENCE_DIR/m3-baggage-captures.txt"
fi
if [ "$BOARDING_ONLY" = "0" ]; then : > "$EVIDENCE_DIR/m3-fuel-captures.txt"; fi
if [ "$FUEL_ONLY" = "0" ]; then : > "$EVIDENCE_DIR/m3-boarding-captures.txt"; fi
prepare_project "$HOST_PROJECT"
prepare_project "$CLIENT_PROJECT"

if [ "$FUEL_ONLY" = "0" ] && [ "$BOARDING_ONLY" = "0" ]; then
set_project_identity "$HOST_PROJECT" host1
set_project_identity "$CLIENT_PROJECT" client1
if ! launch_editor host1 "$HOST_PROJECT" RunHostMovement "$EVIDENCE_DIR/net-host-session1.log" host1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! wait_for_signal host1 s1-host-ready; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! launch_editor client1 "$CLIENT_PROJECT" RunClientMovement "$EVIDENCE_DIR/net-client-session1.log" client1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
session1_ok=1
if ! wait_for_pair host1 client1; then session1_ok=0; fi
collect_report host 1 "$HOST_PROJECT"
collect_report client 1 "$CLIENT_PROJECT"
cleanup_identity host1
cleanup_identity client1
if [ "$session1_ok" -ne 1 ]; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi

set_project_identity "$HOST_PROJECT" host2
set_project_identity "$CLIENT_PROJECT" client2
if ! launch_editor host2 "$HOST_PROJECT" RunHostDeparture "$EVIDENCE_DIR/net-host-session2.log" host2; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! wait_for_signal host2 s2-host-ready; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! launch_editor client2 "$CLIENT_PROJECT" RunClientDeparture "$EVIDENCE_DIR/net-client-session2.log" client2; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
session2_ok=1
if ! wait_for_pair host2 client2; then session2_ok=0; fi
collect_report host 2 "$HOST_PROJECT"
collect_report client 2 "$CLIENT_PROJECT"
cleanup_identity host2
cleanup_identity client2
if [ "$session2_ok" -ne 1 ]; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi

# Session 3（M3.1 餐食剧本）：不加 -nographics —— 双端同刻 RenderTexture 截图。
set_project_identity "$HOST_PROJECT" host3
set_project_identity "$CLIENT_PROJECT" client3
if ! launch_editor host3 "$HOST_PROJECT" RunHostMeal "$EVIDENCE_DIR/net-host-session3.log" host3 1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! wait_for_signal host3 s3-host-ready; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! launch_editor client3 "$CLIENT_PROJECT" RunClientMeal "$EVIDENCE_DIR/net-client-session3.log" client3 1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
session3_ok=1
if ! wait_for_pair host3 client3; then session3_ok=0; fi
collect_report host 3 "$HOST_PROJECT"
collect_report client 3 "$CLIENT_PROJECT"
cleanup_identity host3
cleanup_identity client3
# 双端同刻截图回收到主仓库 Evidence（临时项目随清理删除）。
meal_manifest="$EVIDENCE_DIR/m3-meal-captures.txt"
: > "$meal_manifest"
for shot in m3-meal-ready-host.png m3-meal-deliver-host.png; do
    if [ -f "$HOST_PROJECT/Evidence/$shot" ]; then
        cp "$HOST_PROJECT/Evidence/$shot" "$EVIDENCE_DIR/$shot"
    else
        printf 'MISSING host capture %s\n' "$shot" >> "$meal_manifest"
        session3_ok=0
    fi
done
for shot in m3-meal-ready-client.png m3-meal-deliver-client.png; do
    if [ -f "$CLIENT_PROJECT/Evidence/$shot" ]; then
        cp "$CLIENT_PROJECT/Evidence/$shot" "$EVIDENCE_DIR/$shot"
    else
        printf 'MISSING client capture %s\n' "$shot" >> "$meal_manifest"
        session3_ok=0
    fi
done
if [ "$session3_ok" -ne 1 ]; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
for shot in m3-meal-ready-host.png m3-meal-ready-client.png m3-meal-deliver-host.png m3-meal-deliver-client.png; do
    if [ ! -s "$EVIDENCE_DIR/$shot" ]; then
        FAILURE_REASON="Meal capture is missing or empty: $shot"
        write_summary FAIL
        echo "$FAILURE_REASON" >&2
        exit 1
    fi
    size=$(stat -f %z "$EVIDENCE_DIR/$shot")
    printf '%s  %s bytes  1920x1080\n' "$shot" "$size" >> "$meal_manifest"
done
for report_path in "$EVIDENCE_DIR/net-host-session3.txt" "$EVIDENCE_DIR/net-client-session3.txt"; do
    if ! grep -q '^Runtime errors: 0$' "$report_path"; then
        FAILURE_REASON="Meal session report has nonzero runtime errors: $report_path"
        write_summary FAIL
        echo "$FAILURE_REASON" >&2
        exit 1
    fi
done
printf 'Dual-end same-moment captures; %s; %s\n' \
    "host $(grep '^Runtime errors: ' "$EVIDENCE_DIR/net-host-session3.txt" | head -1)" \
    "client $(grep '^Runtime errors: ' "$EVIDENCE_DIR/net-client-session3.txt" | head -1)" >> "$meal_manifest"

# Session 4（M3.2 行李剧本）：真实 Remote UDP 输入，包含正常交付与错送失败双端截图。
set_project_identity "$HOST_PROJECT" host4
set_project_identity "$CLIENT_PROJECT" client4
if ! launch_editor host4 "$HOST_PROJECT" RunHostBaggage "$EVIDENCE_DIR/net-host-session4.log" host4 1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! wait_for_signal host4 s4-host-ready; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! launch_editor client4 "$CLIENT_PROJECT" RunClientBaggage "$EVIDENCE_DIR/net-client-session4.log" client4 1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
session4_ok=1
if ! wait_for_pair host4 client4; then session4_ok=0; fi
collect_report host 4 "$HOST_PROJECT"
collect_report client 4 "$CLIENT_PROJECT"
cleanup_identity host4
cleanup_identity client4
baggage_manifest="$EVIDENCE_DIR/m3-baggage-captures.txt"
for shot in m3-baggage-wrong-host.png m3-baggage-deliver-host.png; do
    if [ -f "$HOST_PROJECT/Evidence/$shot" ]; then
        cp "$HOST_PROJECT/Evidence/$shot" "$EVIDENCE_DIR/$shot"
    else
        printf 'MISSING host capture %s\n' "$shot" >> "$baggage_manifest"
        record_failure_detail "Baggage host capture is missing: $shot"
        session4_ok=0
    fi
done
for shot in m3-baggage-wrong-client.png m3-baggage-deliver-client.png; do
    if [ -f "$CLIENT_PROJECT/Evidence/$shot" ]; then
        cp "$CLIENT_PROJECT/Evidence/$shot" "$EVIDENCE_DIR/$shot"
    else
        printf 'MISSING client capture %s\n' "$shot" >> "$baggage_manifest"
        record_failure_detail "Baggage client capture is missing: $shot"
        session4_ok=0
    fi
done
if [ "$session4_ok" -ne 1 ]; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
for shot in m3-baggage-wrong-host.png m3-baggage-wrong-client.png m3-baggage-deliver-host.png m3-baggage-deliver-client.png; do
    if [ ! -s "$EVIDENCE_DIR/$shot" ]; then
        FAILURE_REASON="Baggage capture is missing or empty: $shot"
        write_summary FAIL
        echo "$FAILURE_REASON" >&2
        exit 1
    fi
    size=$(stat -f %z "$EVIDENCE_DIR/$shot")
    printf '%s  %s bytes  1920x1080\n' "$shot" "$size" >> "$baggage_manifest"
done
for report_path in "$EVIDENCE_DIR/net-host-session4.txt" "$EVIDENCE_DIR/net-client-session4.txt"; do
    if ! grep -q '^Runtime errors: 0$' "$report_path"; then
        FAILURE_REASON="Baggage session report has nonzero runtime errors: $report_path"
        write_summary FAIL
        echo "$FAILURE_REASON" >&2
        exit 1
    fi
done
printf 'Same-state host/client pairs for wrong delivery and correct delivery; %s; %s\n' \
    "host $(grep '^Runtime errors: ' "$EVIDENCE_DIR/net-host-session4.txt" | head -1)" \
    "client $(grep '^Runtime errors: ' "$EVIDENCE_DIR/net-client-session4.txt" | head -1)" >> "$baggage_manifest"

fi

if [ "$BOARDING_ONLY" = "0" ]; then
# Session 5（M3.3 燃油）：Remote UDP 徒步交互、满箱漫油清理、收纳上车、机位接管与双端截图。
set_project_identity "$HOST_PROJECT" host5
set_project_identity "$CLIENT_PROJECT" client5
if ! launch_editor host5 "$HOST_PROJECT" RunHostFuel "$EVIDENCE_DIR/net-host-session5.log" host5 1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! wait_for_signal host5 s5-host-ready; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! launch_editor client5 "$CLIENT_PROJECT" RunClientFuel "$EVIDENCE_DIR/net-client-session5.log" client5 1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
session5_ok=1
if ! wait_for_pair host5 client5; then session5_ok=0; fi
collect_report host 5 "$HOST_PROJECT"
collect_report client 5 "$CLIENT_PROJECT"
cleanup_identity host5
cleanup_identity client5
fuel_manifest="$EVIDENCE_DIR/m3-fuel-captures.txt"
: > "$fuel_manifest"
for shot in m3-fuel-fill-host.png m3-fuel-spill-host.png m3-fuel-transfer-host.png m3-fuel-deliver-host.png; do
    if [ -s "$HOST_PROJECT/Evidence/$shot" ]; then
        cp "$HOST_PROJECT/Evidence/$shot" "$EVIDENCE_DIR/$shot"
    else
        printf 'MISSING or empty host capture %s\n' "$shot" >> "$fuel_manifest"
        record_failure_detail "Fuel host capture is missing or empty: $shot"
        session5_ok=0
    fi
done
for shot in m3-fuel-fill-client.png m3-fuel-spill-client.png m3-fuel-transfer-client.png m3-fuel-deliver-client.png; do
    if [ -s "$CLIENT_PROJECT/Evidence/$shot" ]; then
        cp "$CLIENT_PROJECT/Evidence/$shot" "$EVIDENCE_DIR/$shot"
    else
        printf 'MISSING or empty client capture %s\n' "$shot" >> "$fuel_manifest"
        record_failure_detail "Fuel client capture is missing or empty: $shot"
        session5_ok=0
    fi
done
if [ "$session5_ok" -ne 1 ]; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
for shot in m3-fuel-fill-host.png m3-fuel-fill-client.png m3-fuel-spill-host.png m3-fuel-spill-client.png \
    m3-fuel-transfer-host.png m3-fuel-transfer-client.png m3-fuel-deliver-host.png m3-fuel-deliver-client.png; do
    if [ ! -s "$EVIDENCE_DIR/$shot" ]; then
        FAILURE_REASON="Fuel capture is missing or empty: $shot"
        write_summary FAIL
        echo "$FAILURE_REASON" >&2
        exit 1
    fi
    size=$(stat -f %z "$EVIDENCE_DIR/$shot")
    printf '%s  %s bytes  1920x1080\n' "$shot" "$size" >> "$fuel_manifest"
done
for report_path in "$EVIDENCE_DIR/net-host-session5.txt" "$EVIDENCE_DIR/net-client-session5.txt"; do
    if ! grep -q '^Runtime errors: 0$' "$report_path"; then
        FAILURE_REASON="Fuel session report has nonzero runtime errors: $report_path"
        write_summary FAIL
        echo "$FAILURE_REASON" >&2
        exit 1
    fi
done
printf 'Dual-end station fill, full-tank spill, paused/resumed aircraft transfer, and completed delivery; %s; %s\n' \
    "host $(grep '^Runtime errors: ' "$EVIDENCE_DIR/net-host-session5.txt" | head -1)" \
    "client $(grep '^Runtime errors: ' "$EVIDENCE_DIR/net-client-session5.txt" | head -1)" >> "$fuel_manifest"

fi

if [ "$FUEL_ONLY" = "0" ]; then
# Session 6（M3.4 登机）：UDP 放行/关闭/再开放、已放行继续、道路阻车与冻结同态截图。
set_project_identity "$HOST_PROJECT" host6
set_project_identity "$CLIENT_PROJECT" client6
if ! launch_editor host6 "$HOST_PROJECT" RunHostBoarding "$EVIDENCE_DIR/net-host-session6.log" host6 1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! wait_for_signal host6 s6-host-ready; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
if ! launch_editor client6 "$CLIENT_PROJECT" RunClientBoarding "$EVIDENCE_DIR/net-client-session6.log" client6 1; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
session6_ok=1
if ! wait_for_pair host6 client6; then session6_ok=0; fi
collect_report host 6 "$HOST_PROJECT"
collect_report client 6 "$CLIENT_PROJECT"
cleanup_identity host6
cleanup_identity client6
boarding_manifest="$EVIDENCE_DIR/m3-boarding-captures.txt"
: > "$boarding_manifest"
for role_name in host client; do
    case "$role_name" in host) capture_project="$HOST_PROJECT" ;; client) capture_project="$CLIENT_PROJECT" ;; esac
    for point in blocked closed complete; do
        shot="m3-boarding-$point-$role_name.png"
        if [ -s "$capture_project/Evidence/$shot" ]; then
            cp "$capture_project/Evidence/$shot" "$EVIDENCE_DIR/$shot"
            size=$(stat -f %z "$EVIDENCE_DIR/$shot")
            printf '%s  %s bytes  1920x1080\n' "$shot" "$size" >> "$boarding_manifest"
        else
            printf 'MISSING or empty capture %s\n' "$shot" >> "$boarding_manifest"
            record_failure_detail "Boarding capture is missing or empty: $shot"
            session6_ok=0
        fi
    done
    report_path="$EVIDENCE_DIR/net-$role_name-session6.txt"
    if ! grep -q '^Runtime errors: 0$' "$report_path"; then
        record_failure_detail "Boarding session report has nonzero or missing runtime errors: $report_path"
        session6_ok=0
    fi
done
for point in blocked closed complete; do
    expected_path="$CONTROL_DIR/s6-expected-$point"
    if [ -s "$expected_path" ]; then
        cp "$expected_path" "$EVIDENCE_DIR/m3-boarding-$point-snapshot.txt"
        printf 'Snapshot expectation: m3-boarding-%s-snapshot.txt (host X/Z, blocked, task progress, gate, passenger count and stable rows)\n' "$point" >> "$boarding_manifest"
    else
        record_failure_detail "Boarding snapshot expectation missing: $point"
        session6_ok=0
    fi
done
if [ "$session6_ok" -ne 1 ]; then
    write_summary FAIL
    echo "$FAILURE_REASON" >&2
    exit 1
fi
printf 'Same-state frozen UDP snapshot pairs: blocked host vehicle, closed gate after walkers finish with waiting IDs preserved, exact completed boarding. Fixtures place actors and satisfy only meal/fuel prerequisites.\n' >> "$boarding_manifest"
fi

for session in $SESSIONS; do
    for role_name in host client; do
        report_path="$EVIDENCE_DIR/net-$role_name-session$session.txt"
        if ! grep -F 'Final: PASS' "$report_path" >/dev/null; then
            FAILURE_REASON="A Unity evidence report did not contain Final: PASS: $report_path"
            write_summary FAIL
            echo "$FAILURE_REASON" >&2
            exit 1
        fi
    done
done

write_summary PASS
if [ "$FUEL_ONLY" = "1" ]; then
    echo "PASS: session 5 fuel-only subset completed the real UDP integration checks; sessions 1-4 and 6 were skipped."
elif [ "$BOARDING_ONLY" = "1" ]; then
    echo "PASS: session 6 boarding-only subset completed the real UDP integration checks; sessions 1-5 were skipped."
else
    echo "PASS: six isolated Unity process pairs completed the real UDP integration checks."
fi
echo "Evidence: $EVIDENCE_DIR/netplay-summary.txt"
