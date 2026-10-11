#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/packages
cp artifacts/core-input/packages/*.nupkg artifacts/packages/
for suffix in Controls.Uno Designer.Uno; do
  dotnet pack "src/ReportScene.$suffix" -c Release -o "$(pwd)/artifacts/packages"
  compgen -G "artifacts/packages/ReportScene.$suffix.*.nupkg" >/dev/null || { echo "Missing Uno package: $suffix" >&2; exit 1; }
done
python3 - <<'PY'
from pathlib import Path
import zipfile, xml.etree.ElementTree as ET
packages = list(Path('artifacts/packages').glob('*.nupkg'))
if len(packages) != 13:
    raise SystemExit(f'Expected 13 reusable packages, found {len(packages)}')
for name in ('ReportScene.Controls.Uno', 'ReportScene.Designer.Uno'):
    package = next(p for p in packages if p.name.startswith(name + '.'))
    with zipfile.ZipFile(package) as archive:
        if not any(n.startswith('lib/') and n.endswith('/' + name + '.dll') for n in archive.namelist()):
            raise SystemExit(f'{name}: missing reusable library assembly')
        print('PASS package payload:', name)
root = ET.Element('configuration')
sources = ET.SubElement(root, 'packageSources'); ET.SubElement(sources, 'clear')
ET.SubElement(sources, 'add', key='ReportSceneLocal', value=str(Path('artifacts/packages').resolve()))
ET.SubElement(sources, 'add', key='NuGet', value='https://api.nuget.org/v3/index.json')
mapping = ET.SubElement(root, 'packageSourceMapping'); ET.SubElement(mapping, 'clear')
local = ET.SubElement(mapping, 'packageSource', key='ReportSceneLocal'); ET.SubElement(local, 'package', pattern='ReportScene.*')
external = ET.SubElement(mapping, 'packageSource', key='NuGet'); ET.SubElement(external, 'package', pattern='*')
ET.ElementTree(root).write('artifacts/uno-consumer.nuget.config', encoding='unicode')
PY
cache="${RUNNER_TEMP:-/tmp}/reportscene-uno-package-smoke"
# ReportScene.* is mapped to the generated feed, never public NuGet. The external source
# supplies only Uno/Skia framework dependencies. No source ProjectReference is available.
dotnet restore tests/ReportScene.UnoPackageTests \
  --configfile "$(pwd)/artifacts/uno-consumer.nuget.config" \
  -p:RestorePackagesPath="$cache"
dotnet build tests/ReportScene.UnoPackageTests -c Release --no-restore \
  -p:RestorePackagesPath="$cache"
echo 'PASS isolated Uno package-consumer compilation'
