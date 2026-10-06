#!/usr/bin/env python3
"""Isolated real-UDP Unity pairs for M4. No user credentials/prefs are copied."""
import argparse
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile
import time
import uuid

GAME = Path(__file__).resolve().parents[1]
EDITOR = Path(os.environ.get('TUANJIE_BIN', os.environ.get('UNITY_BIN',
    '/Applications/Tuanjie/Hub/Editor/2022.3.61t14/Tuanjie.app/Contents/MacOS/Tuanjie')))
CASES = {'shift': 7, 'clientdrop': 8, 'hostleave': 9, 'hostloss': 10}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', choices=list(CASES))
    parser.add_argument('--keep-temp', action='store_true')
    args = parser.parse_args()
    cases = [args.case] if args.case else list(CASES)
    evidence = GAME / 'Evidence' / ('m4-netplay-' + args.case if args.case else 'm4-netplay')
    evidence.mkdir(parents=True, exist_ok=True)
    if not EDITOR.is_file():
        raise SystemExit('Editor not found: ' + str(EDITOR))
    root = Path(tempfile.mkdtemp(prefix='palmbay-netplay.')).resolve()
    tag = uuid.uuid4().hex
    control = root / 'control'
    control.mkdir()
    marker = root / '.netplay-test-marker'
    marker.write_text('PALMBAY_NETPLAY_TEST:' + tag)
    children = []
    handles = []
    identities = []
    results = []
    failure = None

    def prepare(role):
        project = root / role
        (project / 'Assets').mkdir(parents=True)
        subprocess.run(['rsync', '-a', '--exclude=/Resources/palmbay-auth.json',
            '--exclude=/Resources/palmbay-auth.json.meta', '--exclude=/google-services.json',
            '--exclude=/google-services.json.meta', str(GAME / 'Assets') + '/',
            str(project / 'Assets') + '/'], check=True)
        for name in ('Packages', 'ProjectSettings', 'UIElementsSchema', '.codely.packages'):
            shutil.copytree(GAME / name, project / name)
        for name in ('Resources/palmbay-auth.json', 'google-services.json'):
            assert not (project / 'Assets' / name).exists(), 'Credential filtering failed'
        (project / 'Evidence').mkdir()
        return project

    def launch(role, case, project):
        identity = role + str(CASES[case])
        company = 'Palm Bay Netplaytest ' + tag + ' ' + identity
        product = 'Netplay-' + tag + '-' + identity
        settings = project / 'ProjectSettings/ProjectSettings.asset'
        value = settings.read_text()
        value = re.sub(r'^(\s*)companyName:.*$', lambda m: m[1] + 'companyName: ' + company, value, flags=re.M)
        value = re.sub(r'^(\s*)productName:.*$', lambda m: m[1] + 'productName: ' + product, value, flags=re.M)
        settings.write_text(value)
        identities.append((company, product))
        report = project / ('Evidence/net-' + role + '.txt')
        report.unlink(missing_ok=True)
        for old in (project / 'Evidence').glob('m4-*.png'):
            old.unlink()
        env = dict(os.environ)
        for secret in ('PALMBAY_FIREBASE_KEY', 'GOOGLE_APPLICATION_CREDENTIALS', 'FIREBASE_CONFIG'):
            env.pop(secret, None)
        env.update(PALMBAY_NETPLAY_ROOT=str(root), PALMBAY_NETPLAY_MARKER=str(marker),
            PALMBAY_NETPLAY_CONTROL_DIR=str(control), PALMBAY_NETPLAY_RUN_TAG=tag,
            PALMBAY_NETPLAY_ROLE_TAG=identity, PALMBAY_JOIN='127.0.0.1')
        method = 'NetPlaytest.Run' + role.capitalize() + 'M4' + case.capitalize()
        log = evidence / ('net-' + role + '-' + case + '.log')
        handle = (evidence / ('process-' + role + '-' + case + '.txt')).open('w')
        handles.append(handle)
        proc = subprocess.Popen([str(EDITOR), '-batchmode', '-projectPath', str(project),
            '-executeMethod', method, '-logFile', str(log)], cwd=project,
            env=env, stdout=handle, stderr=subprocess.STDOUT)
        children.append(proc)
        return proc

    def wait_ready(proc, case):
        deadline = time.monotonic() + 180
        signal = control / ('m4-' + case + '-host-ready')
        while not signal.exists():
            if proc.poll() is not None:
                raise RuntimeError('Host exited before ready: ' + case)
            if time.monotonic() >= deadline:
                raise RuntimeError('Host ready timeout: ' + case)
            time.sleep(.2)

    def collect(role, case, project, proc):
        source = project / ('Evidence/net-' + role + '.txt')
        dest = evidence / ('net-' + role + '-' + case + '.txt')
        if source.exists():
            shutil.copy2(source, dest)
            contents = dest.read_text()
        else:
            contents = 'Final: FAIL\nMissing Unity report\n'
            dest.write_text(contents)
        images = []
        for image in (project / 'Evidence').glob('m4-*.png'):
            data = image.read_bytes()
            if data[:8] != b'\x89PNG\r\n\x1a\n' or struct.unpack('>II', data[16:24]) != (1920, 1080):
                raise RuntimeError('Invalid capture: ' + image.name)
            shutil.copy2(image, evidence / image.name)
            images.append(image.name)
        good = proc.returncode == 0 and 'Final: PASS' in contents and 'Runtime errors: 0' in contents
        results.append((case, role, proc.returncode, good, images))
        return good

    try:
        host_project, client_project = prepare('host'), prepare('client')
        for case in cases:
            host = launch('host', case, host_project)
            wait_ready(host, case)
            client = launch('client', case, client_project)
            deadline = time.monotonic() + 360
            while host.poll() is None or client.poll() is None:
                if time.monotonic() >= deadline:
                    raise RuntimeError('Pair timeout: ' + case)
                time.sleep(.25)
            host_ok = collect('host', case, host_project, host)
            client_ok = collect('client', case, client_project, client)
            if not (host_ok and client_ok):
                raise RuntimeError('Unity pair failed: ' + case)
            print('PASS M4 ' + case + ': both exits 0, runtime errors 0', flush=True)
    except Exception as error:
        failure = str(error)
    finally:
        for child in children:
            if child.poll() is None:
                child.terminate()
        for child in children:
            try:
                child.wait(timeout=8)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait(timeout=8)
        for handle in handles:
            handle.close()
        # Only identities generated by this invocation can be removed.
        home = Path.home()
        for company, product in identities:
            assert tag in company and tag in product
            shutil.rmtree(home / 'Library/Application Support' / company / product, ignore_errors=True)
            (home / 'Library/Preferences' / ('unity.' + company + '.' + product + '.plist')).unlink(missing_ok=True)
        lines = ['M4 real UDP integration: ' + ('FAIL' if failure else 'PASS'),
            'Scenarios: ' + ', '.join(cases), 'Isolated Unity processes / FakeAuth / explicit test fixtures.']
        for case, role, code, good, images in results:
            lines.append(f'{case} {role}: exit={code} report={"PASS" if good else "FAIL"}')
            lines.extend('Capture: ' + name + ' 1920x1080' for name in images)
        if failure:
            lines.append('Failure: ' + failure)
        if args.keep_temp:
            lines.append('Temporary projects: ' + str(root))
        else:
            shutil.rmtree(root)
        (evidence / 'summary.txt').write_text('\n'.join(lines) + '\n')
    if failure:
        raise SystemExit(failure)
    print('Evidence: ' + str(evidence / 'summary.txt'))


if __name__ == '__main__':
    main()
