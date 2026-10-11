# Recovery and export edge cases

This supplements the [performance and correctness review](performance-review.md) with the final lifecycle/export findings.

## Deferring recovery during transient edits

Not scheduling a save on every pointer move is insufficient: a recovery request from an earlier committed edit can already be queued when the next drag begins. A long-running save can also finish after another drag has started.

`RecoverySaveCoordinator.FlushWhenReadyAsync` checks a host-supplied synchronous readiness predicate immediately before every snapshot, including each iteration after an awaited write. When the host is not ready it leaves the request pending without calling the snapshot or persistence callbacks. The original `FlushAsync` remains available for hosts that always provide a stable snapshot.

The Uno workspace uses `!disposed && !Session.HasActiveDrag` as its predicate. Deferred work is retried by the existing timer, so either committing or cancelling the drag eventually permits a stable save. Storage failures remain pending without an automatic failure loop. An already-started host write is not retroactively cancelled by disposal; the guard prevents subsequent snapshots after disposal. Hosts must coordinate concurrent model mutations with snapshot construction, and the coordinator does not replace the storage provider's durability guarantees.

Conformance tests cover an initially unready host, readiness changing between drained requests, a save queued before a cancelled drag, and cancellation while waiting for readiness. These tests exercise the coordination contract; interactive browser tests also cover ordinary recovery completion after actual property editing.

## Matching exported interactions to visible geometry

Previously HTML and PDF annotations used an object's un-clipped bounds, so a visually hidden part of a subreport could remain clickable. The exporters now use the same item/page/clip intersection as prepared search and outline geometry. Fully clipped or off-paper objects produce no hyperlink annotation. Partially visible objects receive an annotation restricted to their visible intersection. Existing allowed-scheme and HTML-escaping rules remain in place.

Plain-text export now omits fully clipped and off-paper text items while retaining the existing top-to-bottom, left-to-right ordering of visible items. A partially visible text item still exports its complete prepared string; no glyph-level text truncation is inferred from its rectangular clip.

**Clipping is not redaction.** Native report/prepared JSON and CSV have their own data-preserving semantics, and visual PDF/SVG clipping must not be used as a guarantee that underlying text/data is removed from a file. Remove sensitive data from the source model before producing a redacted document. This change corrects invisible text-list entries and annotation hit regions; it is not a secure-redaction implementation.

The direct public PNG and SVG methods now validate their own inputs rather than relying on callers to have entered through the general exporter. NaN resolution, nonpositive/unrepresentable paper dimensions and excessive raster allocation fail before the relevant native backend operation. Existing page-range and raster-budget behavior is retained. New tests cover these guards, text visibility, clipped HTML annotation geometry and omission of invisible PDF URL annotations.
