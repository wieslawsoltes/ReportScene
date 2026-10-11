#!/usr/bin/env bash
set -euo pipefail
projects=(Model Expressions Data Data.AdoNet Matrix Engine Serialization Designer Rendering.Skia Export Samples)
mkdir -p artifacts/packages
for suffix in "${projects[@]}"; do
  dotnet pack "src/ReportScene.$suffix" -c Release -o artifacts/packages
  compgen -G "artifacts/packages/ReportScene.$suffix.*.nupkg" >/dev/null || { echo "Missing package: $suffix" >&2; exit 1; }
done
# This consumer has PackageReference only and a fresh cache. No source ProjectReference or
# public feed can mask a missing ReportScene dependency in a generated nupkg.
dotnet run --project tests/ReportScene.PackageTests -c Release \
  -p:ReportSceneLocalFeed="$(pwd)/artifacts/packages" \
  -p:RestorePackagesPath="${RUNNER_TEMP:-/tmp}/reportscene-package-smoke"
