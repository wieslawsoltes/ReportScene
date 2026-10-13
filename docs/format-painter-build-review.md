# Native repeat-button build correction

Validate run 38461499008 failed both Uno application builds with CS0115:
ReportRepeatToggleButton.OnClick() has no overridable method in the referenced
Uno API. The passing 786 core checks did not compile this native control;
browser scenarios were skipped because publication failed. The preceding
status of "pending" is superseded by this recorded failure, not a success.

Use the public ToggleButton.OnToggle extensibility point to suppress only the
trailing pointer toggle of a recognized double tap. The reusable control now
exposes ActivationRequested alongside RepeatRequested, and the native designer
subscribes to that paired-action contract. Its inherited Click and Command
retain their normal framework semantics; consumers must not use them for the
paired single/repeat action. Ordinary pointer press/release, capture, keyboard,
focus and automation remain on the native control path. Unloading clears the
transient gesture flags. No framework internals or reflection are used.

The independent Uno package consumer references both events, so the public
contract must compile outside the repository project references. All three
native Format Painter scenarios, all prior browser cases, zero retries/skips,
and both actual Uno builds remain required. Passing core conformance alone is
not native control acceptance.

API reference:
https://platform.uno/docs/articles/implemented/microsoft-ui-xaml-controls-primitives-togglebutton.html
