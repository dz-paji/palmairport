#!/usr/bin/env python3
"""Run only a dedicated macOS smoke player with fresh identity and isolated HOME.

Build separately with Unity -executeMethod M7SmokeBuilder.BuildMacSmoke. This
runner uses unattended -batchmode with graphics enabled for Camera.Render and
never starts the Editor, builds a player, copies credentials, or signs in.
"""
import argparse
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import time

GAME = Path(__file__).resolve().parents[1]
PROTOCOL = 'PALMBAY_M7_PLAYER_SMOKE_V1'
SHOTS = ('01-initial-lobby.png', '02-bot-ready.png', '03-shift-start.png',
         '04-results-final-replay.png', '05-retry.png', '06-returned-lobby.png',
         '07-second-entry.png')
# Keep only normal OS/session settings. Do not propagate auth/cloud environment
# variables to the player. macOS application registration may use these settings
# before Unity reaches any managed code; HOME and preference isolation stay fresh.
SESSION_ENV_KEYS = ('PATH', 'LANG', 'LC_ALL', 'LC_CTYPE', 'USER', 'LOGNAME',
                    'SHELL', 'COMMAND_MODE', '__CF_USER_TEXT_ENCODING',
                    'SECURITYSESSIONID', 'XPC_SERVICE_NAME', 'XPC_FLAGS')


def validate_identity(identity):
    nonce = identity.get('nonce', '')
    if not re.fullmatch(r'[0-9a-f]{32}', nonce):
        raise ValueError('Build manifest needs a fresh 32-digit identity nonce')
    expected = {'protocol': PROTOCOL, 'company': 'Palm Bay M7 Smoke ' + nonce,
                'product': 'M7Smoke-' + nonce, 'bundle': 'com.palmbay.m7smoke.' + nonce}
    for field, value in expected.items():
        if identity.get(field) != value:
            raise ValueError('Invalid isolated build identity: ' + field)


def validate_evidence(evidence, identity):
    result = json.loads((evidence / 'm7-player-smoke.json').read_text())
    if result.get('status') != 'PASS' or result.get('runtimeErrors') != 0:
        raise ValueError('Player did not pass: ' + result.get('reason', 'no reason'))
    for field in ('company', 'product', 'bundle'):
        if result.get(field) != identity[field]:
            raise ValueError('Runtime identity mismatch: ' + field)
    if result.get('simulatedSeconds') != 300 or result.get('assertions', 0) < 25:
        raise ValueError('Full shift and required assertions not recorded')
    if Path(result['evidence']).resolve() != evidence:
        raise ValueError('Runtime evidence path mismatch')
    data = Path(result['persistentDataPath'])
    company_product = data.name == identity['product'] and data.parent.name == identity['company']
    bundle_path = data.name == identity['bundle'] and data.parent.name == 'Application Support'
    if not company_product and not bundle_path:
        raise ValueError('Runtime storage identity mismatch')
    if 'synthetic' not in result.get('scope', '') or 'OAuth' not in result.get('scope', ''):
        raise ValueError('Evidence lacks the local-only scope disclosure')
    for name in SHOTS:
        image = evidence / name
        header = image.read_bytes()[:24]
        if image.stat().st_size <= 10000 or len(header) != 24 or header[:8] != b'\x89PNG\r\n\x1a\n' or struct.unpack('>II', header[16:24]) != (1920, 1080):
            raise ValueError('Missing or invalid rendered screenshot: ' + name)
    if not (evidence / 'm7-player-smoke.txt').is_file():
        raise ValueError('Human-readable acceptance report missing')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player', type=Path, default=GAME / 'Builds/M7-Smoke.app')
    parser.add_argument('--manifest', type=Path, help='Defaults to m7-smoke-build.json beside the app')
    parser.add_argument('--evidence', type=Path, required=True, help='Explicit new, independent evidence directory')
    parser.add_argument('--timeout', type=float, default=240)
    parser.add_argument('--keep-temp', action='store_true')
    parser.add_argument('--windowed', action='store_true',
                        help='Opt into a desktop window instead of default unattended batchmode')
    args = parser.parse_args()
    if sys.platform != 'darwin':
        parser.error('This runner requires macOS')
    if args.timeout <= 0:
        parser.error('--timeout must be positive')
    player = args.player.resolve()
    manifest = (args.manifest or player.parent / 'm7-smoke-build.json').resolve()
    identity = json.loads(manifest.read_text())
    validate_identity(identity)
    if Path(identity['app']).resolve() != player:
        raise ValueError('Player does not match its build manifest')
    with (player / 'Contents/Info.plist').open('rb') as stream:
        plist = plistlib.load(stream)
    if plist.get('CFBundleIdentifier') != identity['bundle']:
        raise ValueError('App bundle does not have the isolated smoke identity')
    executable_name = plist.get('CFBundleExecutable', '')
    if not executable_name or Path(executable_name).name != executable_name:
        raise ValueError('Invalid app executable name')
    executable = player / 'Contents/MacOS' / executable_name
    if not executable.is_file() or not os.access(executable, os.X_OK):
        raise ValueError('Player executable is missing or not executable')
    evidence = args.evidence.resolve()
    if evidence.exists():
        raise ValueError('Evidence directory already exists; choose a fresh path')
    # Some Unity/macOS versions ignore HOME for NSHomeDirectory. A fresh company,
    # product and CFPreferences bundle domain still isolate all game persistence.
    for existing_data in (Path.home() / 'Library/Application Support' / identity['company'] / identity['product'],
                          Path.home() / 'Library/Application Support' / identity['bundle']):
        if existing_data.exists() and any(existing_data.iterdir()):
            raise ValueError('Dedicated identity was already used; rebuild the smoke app')
    evidence.mkdir(parents=True)
    root = Path(tempfile.mkdtemp(prefix='palmbay-m7-player.')).resolve()
    home = root / 'home'
    temporary = root / 'tmp'
    home.mkdir()
    temporary.mkdir()
    for relative in ('Library/Application Support', 'Library/Preferences',
                     'Library/Caches', 'Library/Logs'):
        (home / relative).mkdir(parents=True, exist_ok=True)
    marker = root / 'isolation.json'
    isolation = {key: identity[key] for key in ('protocol', 'nonce', 'company', 'product', 'bundle')}
    isolation.update(home=str(home), evidence=str(evidence))
    marker.write_text(json.dumps(isolation, indent=2))
    inherited_session_keys = [key for key in SESSION_ENV_KEYS if key in os.environ]
    env = {key: os.environ[key] for key in inherited_session_keys}
    env.setdefault('PATH', '/usr/bin:/bin:/usr/sbin:/sbin')
    env.setdefault('LANG', 'en_US.UTF-8')
    env.update(HOME=str(home), CFFIXED_USER_HOME=str(home), TMPDIR=str(temporary) + '/')
    command = [str(executable), '-palmbayM7Smoke', '-palmbayM7Isolation', str(marker),
               '-palmbayM7Evidence', str(evidence), '-logFile', str(evidence / 'player.log'),
               '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '0']
    if not args.windowed:
        # Do not use -nographics: the acceptance needs rendered PNG evidence.
        command.append('-batchmode')
    metadata = {'scope': 'Local offline macOS player; synthetic input + real bot; no desktop interaction/OAuth/cloud/device/multiplayer acceptance',
                'player': str(player), 'buildIdentity': identity, 'isolatedHome': str(home),
                'evidence': str(evidence), 'command': command, 'status': 'FAIL',
                'launchMode': 'windowed' if args.windowed else 'batchmode-with-graphics',
                'inheritedSessionEnvironmentKeys': inherited_session_keys}
    began = time.monotonic()
    child = None
    exit_code = 1
    try:
        with (evidence / 'launcher.log').open('w') as output:
            child = subprocess.Popen(command, cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
            try:
                code = child.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                child.terminate()
                try:
                    child.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait(timeout=10)
                raise RuntimeError('Player smoke timed out; inspect player.log and launcher.log')
        metadata['playerExitCode'] = code
        if code != 0:
            if code < 0 and not (evidence / 'm7-player-smoke.json').exists():
                metadata['failurePhase'] = 'native-startup-before-managed-report'
                metadata['signal'] = -code
            raise RuntimeError('Player exited with code ' + str(code) + '; inspect player.log and launcher.log')
        result = validate_evidence(evidence, identity)
        metadata['status'] = 'PASS'
        metadata['persistentDataPath'] = result['persistentDataPath']
        exit_code = 0
        print('M7 standalone player PASS: ' + str(evidence))
        print('Scope: local guest + bot, accelerated synthetic input; no real OAuth/cloud/multiplayer/device claim.')
    except Exception as error:
        metadata['reason'] = str(error)
        print('M7 standalone player FAIL: ' + str(error), file=sys.stderr)
    finally:
        metadata['wallSeconds'] = round(time.monotonic() - began, 3)
        metadata['temporaryStorageKept'] = args.keep_temp
        (evidence / 'runner.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2))
        if args.keep_temp:
            print('Isolated temporary directory: ' + str(root))
        else:
            shutil.rmtree(root)
    return exit_code


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError) as error:
        print('M7 smoke preflight rejected: ' + str(error), file=sys.stderr)
        sys.exit(2)
