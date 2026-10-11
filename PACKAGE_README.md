# ReportScene reusable packages

ReportScene is an independent modular .NET reporting engine with an Uno Platform designer for desktop and WebAssembly. These are development packages, not a binary-compatible FastReport replacement or a claim of complete feature parity.

The model, expressions, data, matrix, engine, serialization and headless designer packages can be used without Uno. Rendering.Skia and Export share the prepared drawing representation. Controls.Uno supplies reusable report surfaces, definition editors and property controls. Designer.Uno provides the embeddable application workspace with host-supplied file, clipboard, print and recovery services.

Native report JSON preserves implemented report objects. FRX is a limited translator, and FPX is not supported. Direct database queries require an explicitly supplied desktop/server connection factory. Imported reports do not execute arbitrary scripts or open external connections.

Immutable page and vertical-interval indexes, prepared search sessions and designer activity/recovery coordination are available independently of the application shell. Rebuild indexes after changing their source content. Own Skia metric/rendering instances per rendering thread and dispose them with the host view or export operation.

[Source and examples](https://github.com/wieslawsoltes/ReportScene) · [Browser demo](https://wieslawsoltes.github.io/ReportScene/) · [Compatibility](https://github.com/wieslawsoltes/ReportScene/blob/main/docs/compatibility.md) · [Performance methodology](https://github.com/wieslawsoltes/ReportScene/blob/main/docs/performance-review.md) · [Validation](https://github.com/wieslawsoltes/ReportScene/blob/main/docs/validation.md)

Distributed under the MIT license. FastReport is a trademark of its respective owner; ReportScene is not an official Fast Reports product.
