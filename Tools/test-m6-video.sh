#!/bin/sh
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
GAME_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
EDITOR_ROOT=/Applications/Tuanjie/Hub/Editor/2022.3.61t14/Tuanjie.app/Contents
MCS_COMPILER=${MCS_BIN:-"$EDITOR_ROOT/MonoBleedingEdge/bin/mcs"}
MONO_RUNTIME=${MONO_BIN:-"$EDITOR_ROOT/MonoBleedingEdge/bin/mono"}
TMP_VIDEO=$(mktemp -d "${TMPDIR:-/tmp}/palmbay-video.XXXXXX")
trap 'rm -rf "$TMP_VIDEO"' EXIT HUP INT TERM
"$MCS_COMPILER" -nologo -define:PALMBAY_VIDEO_TEST -out:"$TMP_VIDEO/VideoTests.exe" \
  "$GAME_DIR/Assets/Scripts/Sharing/SilentVideoEncoder.cs" "$GAME_DIR/Tests/SilentVideoEncoderTests.cs"
"$MONO_RUNTIME" "$TMP_VIDEO/VideoTests.exe" "$TMP_VIDEO"
python3 - "$TMP_VIDEO" "$GAME_DIR" <<'PY'
import json, pathlib, shutil, subprocess, sys, xml.etree.ElementTree as ET
directory, game = map(pathlib.Path, sys.argv[1:])
video = directory / 'silent 回放 with spaces.mp4'
ffprobe = shutil.which('ffprobe') or '/opt/homebrew/bin/ffprobe'
ffmpeg = shutil.which('ffmpeg') or '/opt/homebrew/bin/ffmpeg'
info = json.loads(subprocess.check_output([ffprobe, '-v', 'error', '-show_streams', '-of', 'json', str(video)]))
streams = info['streams']
assert len(streams) == 1 and streams[0]['codec_type'] == 'video', 'video must be silent'
stream = streams[0]
assert stream['codec_name'] == 'h264' and stream['pix_fmt'] == 'yuv420p'
assert stream['width'] == 64 and stream['height'] == 48 and int(stream['nb_frames']) == 30
assert abs(float(stream['duration']) - 1.25) < 0.02
rgb = subprocess.check_output([ffmpeg, '-v', 'error', '-i', str(video), '-frames:v', '1', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'])
top, bottom = rgb[(8*64+32)*3:(8*64+32)*3+3], rgb[(40*64+32)*3:(40*64+32)*3+3]
assert top[0] > 220 and top[2] < 25 and bottom[2] > 220 and bottom[0] < 25, (top, bottom)
manifest = ET.parse(game / 'Assets/Plugins/Android/AndroidManifest.xml').getroot()
ns = '{http://schemas.android.com/apk/res/android}'
provider = manifest.find('application/provider')
assert provider is not None and provider.get(ns+'name') == 'com.palmbay.sharing.ReplayVideoProvider'
assert provider.get(ns+'exported') == 'false' and provider.get(ns+'grantUriPermissions') == 'true'
assert provider.get(ns+'authorities') == '${applicationId}.replayvideo'
assert not any(p.get(ns+'name') == 'android.permission.RECORD_AUDIO' for p in manifest.findall('uses-permission'))
print('ffprobe/decode: real H.264 MP4, 30 frames, correct duration/orientation, no audio; read-only provider manifest passed')
PY
JDK=${JDK_ROOT:-/Applications/Tuanjie/Hub/Editor/2022.3.61t14/PlaybackEngines/AndroidPlayer/OpenJDK}
ANDROID_JAR=${ANDROID_JAR:-/Users/qinziqian/Library/Android/sdk/platforms/android-36.1/android.jar}
if [ -x "$JDK/bin/javac" ] && [ -f "$ANDROID_JAR" ]; then
  "$JDK/bin/javac" -source 8 -target 8 -classpath "$ANDROID_JAR" -d "$TMP_VIDEO/java" \
    "$GAME_DIR/Assets/Plugins/Android/RgbYuvConverter.java" \
    "$GAME_DIR/Assets/Plugins/Android/SilentVideoEncoder.java" \
    "$GAME_DIR/Assets/Plugins/Android/ReplayVideoProvider.java" \
    "$GAME_DIR/Assets/Plugins/Android/ReplayVideoShare.java" \
    "$GAME_DIR/Tests/RgbYuvConverterTests.java"
  "$JDK/bin/java" -cp "$TMP_VIDEO/java" RgbYuvConverterTests
else
  echo 'Android SDK/JDK unavailable: Java compile skipped (set JDK_ROOT and ANDROID_JAR).'
fi
