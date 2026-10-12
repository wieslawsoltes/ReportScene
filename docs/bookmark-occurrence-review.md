# Bookmark occurrence regression review

The first native/browser build failed because single target-typed `new(...)` values passed to a `params PreparedPage[]` fixture were inferred as the array type. Explicit `new PreparedPage(...)` constructors corrected compilation without changing conformance assertions.

The subsequent run 38266796860 passed 662 native and 652 actual WebAssembly conformance checks, but the three new browser scenarios addressed guessed bookmark keys. Their fixture treated a design object's position in its band as its prepared drawing-list position. That assumption is invalid: the bookmark key deliberately encodes a prepared occurrence, not the source-design ordinal.

A read-only inspection of the retained failing trace (review run 38268955758) showed an actual visible row named `Far target` at `preview-outline-bookmark:1:3`, with valid native bounds, while the test waited for `preview-outline-bookmark:1:1`. The snapshot contained four bookmarks, two filtered entries and no diagnostics. It was not evidence of a missing native row or a slow lookup. Waiting longer on the guessed identifier cannot correct it.

The browser helper now locates the realized caption within its page ordinal, requires exactly one matching native row, then uses that row's exact occurrence key for real pointer input and selection assertions. Duplicate-caption tests still require distinct keys, both page transitions, highlighted target visibility and unchanged metadata/index ownership. High-zoom tests still require two-axis scrolling and actual highlighted pixels; replacement tests still require empty state, released targets and reset filters. No model command injection, automatic scenario retry, skipped assertion or larger timeout is introduced. Core exact-key tests remain to cover the standalone occurrence algorithm with explicitly constructed prepared lists.

The temporary trace-inspection workflow is absent from the final source tree. Current-head full browser acceptance is still required; previously passing arithmetic and compilation cannot substitute for actual interaction results.
