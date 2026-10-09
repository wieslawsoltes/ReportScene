#!/usr/bin/env python3
"""Stage Uno's real publish output without replacing it with a static mockup."""
from pathlib import Path
import json
import shutil
import sys

source, output, test_output = map(Path, sys.argv[1:])
candidates = list(source.rglob('index.html'))
if not candidates:
    print('\n'.join(str(p) for p in source.rglob('*')))
    raise SystemExit('Uno publish output contains no index.html')
# Uno's wwwroot/distribution layout can differ between SDK releases.
candidates.sort(key=lambda p: (0 if 'wwwroot' in p.parts else 1, len(p.parts)))
root = candidates[0].parent
if not list(root.rglob('*.wasm')):
    raise SystemExit(f'{root} contains no WebAssembly binaries; refusing to publish a non-Uno substitute')
for target in (output, test_output):
    if target.exists():
        shutil.rmtree(target)
    shutil.copytree(root, target)
    (target / '.nojekyll').touch()
    (target / 'build-info.json').write_text(json.dumps({'application': 'ReportScene', 'runtime': '.NET 10 / Uno Platform Skia', 'source': str(root)}, indent=2))
print(f'Staged {root} -> {output} and {test_output}')
