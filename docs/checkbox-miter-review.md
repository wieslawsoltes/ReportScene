# Tiny checkbox check-mark extents

Stroke width alone is not always the complete painted extent. The built-in checkbox draws a rectangle and, when checked, the fixed angled path (.2,.5) → (.43,.75) → (.82,.23) times its side. At tiny sizes its one-point minimum stroke creates a miter join extending beyond the rectangle's half-stroke envelope.

`PreparedPaintBounds.StrokeOutset` retains its half-width contract. `PaintOutset` also conservatively bounds this built-in path: every vertex is at least one eighth of the side inside the nominal box, and the single fixed-angle miter extends less than one full stroke width from its vertex. The maximum of the rectangular half-width and this inset-path bound covers either checked state. Normal large boxes retain the same bounds; small unchecked boxes may admit extra candidates rather than risk removing a checked mark. This depends on the fixed renderer path, not arbitrary custom paths or effects.

Prepared-page indexing, the linear viewport drawing path and authoring culling now all use the paint envelope. No renderer pixel output or report data is changed. The previous minimum-stroke assertions remain intact. Two additional shared native/WebAssembly regressions render 1-point and quarter-point checked boxes at 64x diagnostic magnification and compare full, indexed and linear pixels in a strip beyond the rectangle's stroke bounds. The reference strip must contain actual nonwhite ink. These are model/raster regressions, not a change to the UI's allowed zoom range or a new browser-performance claim.

Final current-head execution is required before claiming these new regressions pass. The existing 52-scenario native-input browser gate remains unchanged.
