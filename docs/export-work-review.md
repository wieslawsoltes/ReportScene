# Avoiding unrelated export work

Source review found that both CSV entry points performed prepared-page work even though CSV exports bound data tables. The legacy Export method scanned and materialized a physical page-number filter that CSV never consumed. ExportSelection applied an ordinal selection and copied its page-reference list before also discarding that list. Data-only, text and native prepared-JSON output additionally constructed a report renderer/font-metrics object without drawing anything.

## Changes

CSV now keeps the original table-data semantics and does not enumerate or copy prepared pages. Ordinal export still requires All mode, the original page-count match and default physical filters; those guards are not bypassed. Legacy physical range options retain their validation and the old CSV behavior of exporting all bound rows. Empty prepared output does not prevent a valid table export. Report-error diagnostics, absent table errors and cancellation remain enforced.

Text, prepared JSON and CSV return before renderer construction. Their content selection, encoding and metadata paths are preserved. Graphics formats still use one disposed renderer for their complete output. Physical page-range scans now check cancellation before and between page visits, rather than only after materializing all matches. This does not make synchronous raster encoding, JSON serialization or CSV row encoding asynchronously cancellable midway through a single call.

PNG/SVG archive naming still preserves unique physical-number filenames and suffixes every repeated physical number with its selected ordinal. The duplicate-number prepass now retains only number sets rather than grouping page references into per-number lists, and checks cancellation during the prepass. Archives preserve source order, media types and the existing entry timestamps. The name-set pass remains O(n); the selected output still retains and processes its actual pages, and encoded outputs are still assembled in memory.

## Executable contracts

Eleven new shared native/WebAssembly cases use a page list that reports a count but throws and increments a counter on any item/enumerator access. Both CSV APIs must produce the same bytes as the existing table encoder with zero such accesses. Counts include zero, one, one million and int.MaxValue; this is a count-only contract fixture, not a physically materialized billion-page report or an end-to-end memory benchmark. Separate checks retain stale/mode/filter rejection, diagnostic errors, missing data, pre-cancellation and cancellation during a physical scan.

Other cases inspect actual CSV/PNG/SVG ZIP contents, names and ordering, plus text/JSON page selection and original physical labels. The existing native package consumer and all 65 real browser scenarios remain required, including the previously implemented export dialog's exact selections, CSV mode behavior, PNG resolution and oversized-output recovery. No new scenario count is inferred from headless tests.

These are measured work-exclusion contracts and source-level allocation reductions, not a claimed whole-UI speed ratio, storage throughput, peak-memory limit or hardware-GPU gain. Current-head CI must pass before integration. Synchronous export/UI responsiveness, bounded streaming encoders and the broader FastReport provider/format/visual-parity catalog remain outstanding.
