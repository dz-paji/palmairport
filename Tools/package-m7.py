#!/usr/bin/env python3
"""Package local players and record content hashes; defaults preserve M7 delivery. No upload."""
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import os
import re
import shutil
import subprocess
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='1.0.7')
    parser.add_argument('--android-code', type=int, default=7)
    parser.add_argument('--label', default='M7')
    parser.add_argument('--evidence-dir', type=Path, default=ROOT / 'Evidence')
    parser.add_argument('--log-prefix', default='m7')
    parser.add_argument('--runbook', type=Path, default=ROOT / 'Docs/M7-RUNBOOK.md')
    args = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9.-]+', args.label) or not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', args.version) or args.android_code < 1:
        parser.error('Use a plain release label, numeric semantic version, and positive Android code.')
    app = ROOT / 'Builds/Palm Bay.app'
    apk = ROOT / 'Builds/PalmBay.apk'
    info = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    if info.get('CFBundleShortVersionString') != args.version:
        raise SystemExit('Build the macOS player (' + args.version + ') before packaging.')
    if not apk.is_file() or apk.stat().st_size == 0:
        raise SystemExit('Android APK is missing.')
    candidates = [Path(os.environ['AAPT'])] if os.environ.get('AAPT') else []
    candidates += sorted((Path.home() / 'Library/Android/sdk/build-tools').glob('*/aapt'), reverse=True)
    aapt = next((path for path in candidates if path.is_file()), None)
    if aapt is None:
        raise SystemExit('Set AAPT to an Android SDK aapt executable to verify this APK.')
    metadata = subprocess.check_output([str(aapt), 'dump', 'badging', str(apk)], text=True)
    expected = "package: name='com.palmbay.islandairport' versionCode='" + str(args.android_code) + "' versionName='" + args.version + "'"
    if expected not in metadata:
        raise SystemExit('APK identity/version does not match this release; rebuild Android first.')
    for name, marker in [('macos', 'PALM_BAY_BUILD_READY'), ('android', 'PALM_BAY_ANDROID_READY')]:
        log = args.evidence_dir / (args.log_prefix + '-' + name + '-build.log')
        if not log.is_file() or marker not in log.read_text(errors='replace'):
            raise SystemExit('Missing successful build evidence: ' + str(log))
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    output = ROOT / 'Builds' / (args.label + '-' + args.version + '-' + stamp)
    output.mkdir(exist_ok=False)
    subprocess.run(['/usr/bin/ditto', '-c', '-k', '--sequesterRsrc', '--keepParent',
                    str(app), str(output / 'Palm-Bay-macOS.zip')], check=True)
    shutil.copy2(apk, output / 'PalmBay-Android.apk')
    readme = args.runbook.read_text()
    readme = re.sub(r'\[([^\]]+)\]\(\.\./(Evidence/[^)]+)\)',
                    lambda match: match[1] + '（源码工程：`' + match[2] + '`）', readme)
    (output / 'READ-ME.md').write_text(readme)
    entries = []
    for path in sorted(output.iterdir()):
        digest = hashlib.sha256()
        with path.open('rb') as source:
            for block in iter(lambda: source.read(1024 * 1024), b''):
                digest.update(block)
        entries.append(dict(file=path.name, bytes=path.stat().st_size, sha256=digest.hexdigest()))
    manifest = dict(version=args.version, android_version_code=args.android_code, generated_utc=stamp,
                    scope='Local prototype delivery; device/OAuth/production cloud acceptance pending',
                    files=entries)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (output / 'SHA256SUMS').write_text(''.join(e['sha256'] + '  ' + e['file'] + '\n' for e in entries))
    print(output)


if __name__ == '__main__':
    main()
