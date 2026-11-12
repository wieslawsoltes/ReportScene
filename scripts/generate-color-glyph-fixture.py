"""Regenerate ReportScene's original, minimal COLR/CPAL test font.

Requires fontTools. Normal builds use the checked-in fixture and do not run this
script. The shapes and font metadata below are original test data under the
repository license; no external font is copied or subsetted.
"""

from pathlib import Path
from fontTools.colorLib.builder import buildCOLR, buildCPAL
from fontTools.fontBuilder import FontBuilder
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.pens.ttGlyphPen import TTGlyphPen


def rectangle(left, bottom, right, top):
    pen = TTGlyphPen(None)
    pen.moveTo((left, bottom))
    pen.lineTo((right, bottom))
    pen.lineTo((right, top))
    pen.lineTo((left, top))
    pen.closePath()
    return pen.glyph()


builder = FontBuilder(1000, isTTF=True)
order = [".notdef", "color", "red", "foreground"]
builder.setupGlyphOrder(order)
builder.setupCharacterMap({0xE000: "color", 0xE001: "red"})
builder.setupGlyf({
    ".notdef": TTGlyphPen(None).glyph(),
    "color": rectangle(0, 0, 1000, 800),
    "red": rectangle(0, 0, 500, 800),
    "foreground": rectangle(500, 0, 1000, 800),
})
# Preserve each layer's horizontal bearing so native font engines place the
# foreground rectangle on the right half rather than translating it to x=0.
builder.setupHorizontalMetrics({name: (1000, 500 if name == "foreground" else 0) for name in order})
builder.setupHorizontalHeader(ascent=800, descent=-200)
builder.setupNameTable({
    "familyName": "ReportScene Color Glyph Fixture",
    "styleName": "Regular",
    "uniqueFontIdentifier": "ReportScene.ColorGlyphFixture.1",
    "fullName": "ReportScene Color Glyph Fixture Regular",
    "psName": "ReportSceneColorGlyphFixture-Regular",
    "version": "Version 1.0",
})
builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
builder.setupPost()
builder.setupMaxp()
# Positioned overlap distinguishes per-glyph opacity from a single opacity
# layer around the entire shaped run. At 100pt these glyphs start at x=0,60.
addOpenTypeFeaturesFromString(builder.font, "feature kern { pos color color -400; } kern;")
builder.font["COLR"] = buildCOLR({"color": [("red", 0), ("foreground", 0xFFFF)]}, version=0)
builder.font["CPAL"] = buildCPAL([[(1.0, 0.0, 0.0, 1.0)]])
builder.font.recalcTimestamp = False
builder.font["head"].created = builder.font["head"].modified = 2082844800
target = Path(__file__).resolve().parents[1] / "src/ReportScene.Validation/Assets/ColorGlyphFixture.ttf"
target.parent.mkdir(parents=True, exist_ok=True)
builder.font.save(target)
print(target)

# The same original outlines without color tables exercise the ordinary text
# path deterministically, including in browser hosts without a system font manager.
del builder.font["COLR"]
del builder.font["CPAL"]
builder.setupNameTable({
    "familyName": "ReportScene Outline Glyph Fixture",
    "styleName": "Regular",
    "uniqueFontIdentifier": "ReportScene.OutlineGlyphFixture.1",
    "fullName": "ReportScene Outline Glyph Fixture Regular",
    "psName": "ReportSceneOutlineGlyphFixture-Regular",
    "version": "Version 1.0",
})
target = target.with_name("OutlineGlyphFixture.ttf")
builder.font.save(target)
print(target)
