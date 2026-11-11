# Uno Platform 7 and WebGPU migration

## Goal

Move the desktop and browser application, reusable Uno controls, and all interactive report drawing to Uno Platform 7 with WebGPU. Preserve the existing report model, authoring behavior, pagination, text metrics and export contracts.

## Baseline

- Source: `425c01db1b44f19418a8af408da01ab0dff3010d` (`main`).
- Uno SDK 6.7.30 and .NET 10; desktop and browser heads.
- Report design, preview and vector icons currently use `SKCanvasElement`.
- The shared Skia renderer also powers PDF, SVG and PNG export; these offline document encoders must remain available.
- Existing validation includes core conformance, package consumers, benchmarks and actual Uno browser interaction tests.

## Implementation plan

1. Pin Uno SDK 7 and its matching runtime dependencies, migrate host startup, and rebuild every Uno-dependent library.
2. Separate report drawing commands from their display backend while keeping the existing export APIs compatible.
3. Implement a native WebGPU renderer with WGSL shaders, ordered batching, bounded resources, clipping and high quality text/image sampling.
4. Move interactive surfaces to viewport-sized GPU targets with demand-driven invalidation, preserving scroll, zoom, selection and preview indexes.
5. Validate geometry, text, image, chart and barcode behavior; run the application and migration gates; record measured evidence and remaining limits here.

## Evidence policy

Feature implementation, CPU submission measurements, software GPU validation and hardware GPU performance are distinct. No universal speedup or visual-quality certification is claimed without representative hardware measurements. This document will record completed work and actual validation as implementation proceeds.

## Upstream references

- [Uno Platform 7 migration guide](https://platform.uno/docs/articles/migrating-to-uno-7.html)
- [WebGPU rendering backend](https://platform.uno/docs/articles/features/webgpu.html)
- [WebGpuCanvasElement](https://platform.uno/docs/articles/controls/WebGpuCanvasElement.html)
