# ReportScene

A modular reporting engine and Uno Platform report designer for desktop and WebAssembly, developed against the public FastReport .NET feature documentation.

## Implementation status

Initial development. This repository does **not** claim full FastReport .NET parity or drop-in API, FRX, FPX, script, export, or designer compatibility. Implemented and outstanding features will be recorded in `docs/compatibility.md`; build and browser test evidence will be recorded separately.

The architecture separates the report model, expression evaluation, data access, pagination, serialization, exports, designer state, rendering, and reusable Uno controls. The designer and browser deployment must use the same .NET reporting engine.

FastReport is a trademark of its respective owner. ReportScene is an independent project, not an official Fast Reports product. No proprietary FastReport source, artwork, or binaries are included.
