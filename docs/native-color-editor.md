# Native color editing

The reusable `ReportColorEditor` combines the actual Uno ColorPicker spectrum, brightness and opacity controls with 40 standard swatches, explicit transparent selection, byte RGBA fields, RGB/ARGB hexadecimal input, and original/new checkerboard previews. Palette arrows/Home/End navigate; native Space activates a swatch. `DesignerColorValue` provides bounded culture-independent parsing, exact byte equality, and alpha-first serialization. This does not add a new report file color representation.

The initial color branch failed compilation because `ColorSpectrum` is in `Microsoft.UI.Xaml.Controls.Primitives`. The missing import is corrected; the native control is retained rather than replaced by an imitation spectrum.

## Draft and commit ownership

Fill, Text color and Border color now open the native editor through the designer ribbon. A captured `DesignerTarget` guards the original document revision, page, band and selected objects before Apply. Locked objects are excluded by the session. Color changes use one existing batch/undo transaction. Validation failures keep the dialog open; Cancel changes no report content. An untouched mixed selection remains mixed on Apply. Explicitly choosing a color, including the initial palette color, applies it to the editable selection. Semantically equal colors preserve their existing stored spelling and create no content mutation. Border color does not silently enable a border or change its width.

Existing renderer-accepted aliases can initialize the editor through the renderer parser; user-entered values use bounded RGB/ARGB hex and decimal channels. Alpha-zero RGB bytes are preserved by exact entry; the explicit No color command uses transparent black. Native spectrum selection retains the native ColorPicker's own HSV behavior.

Live editing preserves active text instead of normalizing it after each valid prefix. This matters when typing eight ARGB digits: the intermediate six-digit RGB value must not insert a prefix or move the caret. Invalid/partial text preserves the last valid preview but cannot be applied. Escape restores the last valid value; Restore original restores the opening value. Delayed identical TextChanged notifications are ignored to avoid overwriting a fresh draft. Report data is never used as the draft store.

## Work and lifetime review

The palette is parsed once. Per-control brushes for selected/unselected borders are reused, and only the previous/new selected swatches have their border properties updated. The previous implementation parsed all 40 palette colors and created 40 border brushes on each color update. Public counters expose actual value transitions and swatch property updates for consumers. No frame-rate, input-latency, native-memory or hardware-GPU measurement is inferred from this source-level work reduction.

Dialog diagnostics enumerate real native controls and the actual ColorSpectrum child. The host detaches StateChanged and removes only its own registered controls when the dialog closes. No callback writes to the report until Apply. The editor remains reusable outside the report designer, and the generated package-only consumer compiles its public API.

## Validation and remaining limits

Eight headless color contracts cover malformed/oversized input, culture, channels, all alpha bytes and roundtrips. Four native-input browser scenarios preserve the 57 prior scenarios and require a 61-scenario aggregate over all eight mandatory shards with no retries, skips or flaky outcomes. They test incremental ARGB typing, invalid Apply, cancellation, real checkerboard pixels, native palette keyboard interaction, atomic/mixed selection, actual spectrum input, RGBA serialization, text/border targets and locking. A new build must pass all gates; no pending result is treated as acceptance.

This improves the reporting color workflow, not complete FastReport visual identity. Gradient/hatch fills, report styles/themes, complete docking/toolbox behavior, arbitrary scripts, inheritance, rich text and the complete export/provider catalog remain outside this increment. Property-grid scalar color entry remains available. The old private compact-palette helper is not used by the ribbon after this integration.

Primary references: [WinUI ColorPicker](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/color-picker), [ColorSpectrum namespace](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.colorspectrum), [Uno ColorPicker support](https://platform.uno/docs/articles/implemented/microsoft-ui-xaml-controls-colorpicker.html).
