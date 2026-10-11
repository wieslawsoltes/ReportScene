# ReportScene

A modular .NET reporting engine and Uno Platform designer for desktop and WebAssembly. ReportScene is an independent implementation developed against publicly documented reporting concepts, not an official Fast Reports product or a drop-in FastReport replacement.

[Browser application](https://wieslawsoltes.github.io/ReportScene/) · [Compatibility](docs/compatibility.md) · [Data providers](docs/data-providers.md) · [Validation](docs/validation.md)

## Reporting and authoring

Band pagination, grouping and aggregates, master/detail relations, parameters, multi-column pages, growing text, prepared-page search, charts, tables, pictures, shapes and six barcode symbologies share the same .NET model and preparation engine. Matrix reports add typed hierarchical axes, subtotals and grand totals, nine aggregation operations, multiple measures, percentages, horizontal page tiling and repeated column headers.

The designer offers report/data trees, property editors, drag/resize/snap, alignment, clipboard operations, transactional undo/redo, local recovery, a visual matrix definition editor, and CSV/JSON/XML import with a bounded typed preview. PDF, SVG, PNG, printable HTML, text, CSV and native prepared JSON use the common prepared-page representation. Native report JSON is the lossless interchange format for implemented objects. FRX is an explicitly diagnosed subset; FPX is not supported.

Uno controls and report drawing use Skia; text metrics/shaping use HarfBuzz. Browser validation runs the actual .NET WebAssembly application rather than a separate JavaScript mock. CI uses software WebGL, not evidence of hardware GPU performance.

## Reusable packages

| Package | Responsibility |
| --- | --- |
| `ReportScene.Model` | Report objects, typed scalar data and validation |
| `ReportScene.Expressions` | Bounded expression parsing and aggregate evaluation |
| `ReportScene.Data` | CSV/JSON/XML readers, file dispatch and provider contract |
| `ReportScene.Data.AdoNet` | CLR/DataTable/DataSet/DbDataReader adapters and explicit parameterized query providers |
| `ReportScene.Matrix` | Independent typed cross-tab aggregation |
| `ReportScene.Engine` | Band, table and matrix preparation and pagination |
| `ReportScene.Serialization` | Source-generated native JSON and diagnosed FRX subset |
| `ReportScene.Designer` | Headless authoring state and transactional history |
| `ReportScene.Rendering.Skia` | Common report drawing and font-backed metrics |
| `ReportScene.Export` | Prepared-report exporters |
| `ReportScene.Controls.Uno` | Reusable design/preview surfaces, property sheets, matrix editor and data preview |
| `ReportScene.Designer.Uno` | Embeddable designer with host-supplied file/clipboard/print services |
| `ReportScene.Samples` | Executable example reports |

`ReportScene.Validation` is a non-packable test library shared by console and browser hosts. Core packages do not depend on Uno. Database queries require an explicitly supplied desktop/server connection factory; imported report files cannot create connections or execute scripts.

## Build and validate

Install the .NET SDK specified by `global.json`. From the repository root:

```sh
dotnet run --project tests/ReportScene.Tests -c Release
bash scripts/pack-core.sh
dotnet build apps/ReportScene.App -c Release -f net10.0-desktop -p:ReportSceneTargetFramework=net10.0-desktop

dotnet workload install wasm-tools
dotnet publish apps/ReportScene.App -c Release -f net10.0-browserwasm \
  -p:ReportSceneTargetFramework=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ReportScene/ -o artifacts/wasm
python3 scripts/stage-browser.py artifacts/wasm artifacts/site artifacts/testsite/ReportScene
python3 -m http.server 8000 --directory artifacts/testsite
# In another terminal:
cd tests/browser
npm install
npx playwright install chromium
npx playwright test
```

Open **View → Samples → Matrix sales** for the cross-tab example, or **Report → Import data** to bind CSV, JSON or scalar XML data. Select a matrix and double-click it or choose **Home → Edit matrix** to edit dimensions, measures, totals and layout.

## Scope

This is active development, not complete FastReport .NET parity or production certification. General subreports, rich-text/HTML objects, report inheritance, arbitrary C#/VB.NET scripts/events, dialog pages, native FPX, the full provider/export catalog, and complete original-designer visual/behavioral matching remain outstanding. See the compatibility document for narrower boundaries within implemented areas.

FastReport is a trademark of its respective owner. No proprietary FastReport source, artwork or binaries are included. See [LICENSE](LICENSE).
