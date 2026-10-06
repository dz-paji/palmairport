#!/usr/bin/env python3
"""Compile against installed Unity assemblies without starting the licensed editor."""
import os, pathlib, subprocess, tempfile
root = pathlib.Path(__file__).resolve().parents[1]
default = '/Applications/Tuanjie/Hub/Editor/2022.3.61t14/Tuanjie.app/Contents'
editor = pathlib.Path(os.environ.get('UNITY_CONTENTS', default))
compiler = editor / 'MonoBleedingEdge/bin/mcs'
managed = editor / 'Managed'
refs = sorted((managed/'UnityEngine').glob('*.dll'))
refs = [p for p in refs if p.name.startswith(('UnityEngine', 'UnityEditor'))]
refs += list((editor/'MonoBleedingEdge/lib/mono/4.5/Facades').glob('netstandard.dll'))
# uGUI is a source-only package; Unity compiles it into Library/ScriptAssemblies on import.
ui_dll = root/'Library/ScriptAssemblies/UnityEngine.UI.dll'
if not ui_dll.exists():
    raise SystemExit('FAIL: %s not found; open the project in the editor once to compile the uGUI package.' % ui_dll)
refs.append(ui_dll)
with tempfile.TemporaryDirectory(prefix='palmbay-compile-') as tmp:
    cmd = [str(compiler), '-target:library', '-out:'+tmp+'/PalmBay.dll']
    cmd += ['-r:'+str(p) for p in refs]
    cmd += [str(p) for p in sorted((root/'Assets').rglob('*.cs'))]
    result = subprocess.run(cmd)
    if result.returncode: raise SystemExit(result.returncode)
    print('PASS: all runtime and editor C# sources compile against installed Unity assemblies.')
