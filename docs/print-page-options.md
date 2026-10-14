# Print page options

The native Print Report dialog now precedes the host print service for the ribbon, File command and Ctrl+P. It presents document information, All/Current/custom page ranges and reverse ordering. The page-range control is the same independently reusable ordinal selector used by export options. The print editor stores only name/count/current-position metadata and a draft; it does not retain a report or own a printer.

`ReportExporter.CreatePrintDocument` produces the actual HTML/SVG print document through the existing rendering/export path. It snapshots selected page references once, then uses a read-only reverse view when requested. It does not reverse the source report, clone item data or allocate a second page array for reversal. Physical page labels, page sizes and content remain unchanged; ranges refer to displayed positions, not those labels. Empty/stale selections, invalid ranges, report errors and cancellation fail before a host receives output.

The designer captures the report revision and prepared identity before opening the dialog. Only the native Print button validates, generates output and invokes the host. The asynchronous button deferral keeps the draft open when validation, host refusal or cancellation fails; an explicit retry is possible. Closing with Cancel sends no print document. After a successful host acknowledgement, the status says the selected pages were opened for printing, not that a physical print job completed. A delayed acknowledgement does not mark a new document or newer revision saved.

## Host-owned settings

The destination is explicitly the system print dialog. Printer enumeration, copies, collation, paper trays, duplex, output scaling and physical printer completion are not fabricated in this browser UI. The host or operating-system dialog owns those settings. Because the application has already selected the pages, its instructions tell the user to leave the system page range on All. Browser popup refusal is shown inline and leaves the draft intact. HTML printing still depends on the browser's page-size and scaling behavior; this increment does not establish exact physical printer output or full FastReport PrintSettings compatibility.

## Validation

Eight native/WebAssembly tests cover zero/repeated/non-contiguous physical labels, displayed Current selection, normalized custom ranges, reversal, unchanged source objects, escaped print HTML and page-break CSS, cancellation, stale counts and report errors. The independent Uno package consumer compiles both the reusable control and exporter API.

Three real native-input browser scenarios preserve all preceding 61 and raise the required aggregate to 64 across all eight shards, with zero automatic scenario retries, skips or flaky outcomes. They test invalid/cancel behavior, custom/reverse and Ctrl+P Current selection, actual popup HTML/SVG page order and geometry, screenshot output, opener isolation, unchanged report/preview/index state, and a refused popup followed by explicit retry. The real browser host and real exported document are used. Only the final `window.print` OS-device call is recorded by CI, because there is no interactive physical printer. One scenario makes the first actual window.open return null to exercise platform refusal; no report command or model state is injected.

No current-head pass is implied until all CI gates finish. This is a scoped reporting-workflow improvement, not complete original UI identity, full printer API parity, a GPU/latency benchmark or an exhaustive audit.

Primary references: [FastReport PrintSettings](https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.PrintSettings.html), [browser print() contract](https://developer.mozilla.org/en-US/docs/Web/API/Window/print).
