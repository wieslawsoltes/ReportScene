// Direct WGSL contract validation. This does not represent, replace or mock the Uno
// application: the browser scenarios separately exercise the real .NET UI and GPU host.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';
import { screenshotPixels } from './screenshot-pixels.mjs';
import { ellipseDistanceCases, ellipseIterationBudget } from './ellipse-reference.mjs';
import { lineReferenceFrames } from './line-reference.mjs';

const evidence = fileURLToPath(new URL('./shader-evidence/', import.meta.url));
const shaderPath = new URL('../../src/ReportScene.Rendering.WebGpu/Shaders/Report2D.wgsl', import.meta.url);

test('Report2D WGSL compiles and preserves geometry, clips, premultiplied blending and grid pixels', { timeout: 180_000 }, async () => {
  await mkdir(evidence, { recursive: true });
  const code = await readFile(shaderPath, 'utf8');
  const ellipseReference = ellipseDistanceCases();
  const oval = (cx, cy, rx, ry, stroke, color, sx = 1, sy = 1) => {
    const rect = [cx - rx * sx, cy - ry * sy, rx * sx * 2, ry * sy * 2];
    const padX = Math.max(0, stroke) * sx + 1.5, padY = Math.max(0, stroke) * sy + 1.5;
    return { rect, color, options: {
      stroke, data: [1 / sx, 1 / sy, ellipseIterationBudget(Math.max(rx * sx, ry * sy)), 0],
      quad: [rect[0] - padX, rect[1] - padY, rect[2] + padX * 2, rect[3] + padY * 2]
    } };
  };
  const ellipses = [
    oval(48.5, 304.5, 10, 10, 20, [0, .5, 1, 1]),
    oval(112.5, 304.5, 10, 10, 10, [0, 1, 0, 1]),
    oval(176.5, 304.5, 20, 20, 2, [1, .5, 0, 1]),
    oval(320.5, 300.5, 60, 10, 8, [1, 0, .5, 1]),
    oval(320.5, 390.5, 60, 10, 20, [.5, 0, 1, 1]),
    oval(448.5, 390.5, 10, 10, 4, [0, 1, 1, 1], 3, 1),
    oval(112.5, 430.5, 80, 2, 6, [1, 1, 0, 1]),
    oval(448.5, 48.5, 40, 20, -1, [0, 1, .5, 1]),
    oval(448.5, 150.5, 40e-25, 20e-25, 8e-25, [1, .5, .5, 1], 1e25, 1e25),
    oval(448.5, 230.5, 40e25, 20e25, 8e25, [.5, 1, .5, 1], 1e-25, 1e-25)
  ];
  const server = createServer((_request, response) => {
    response.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    response.end('<!doctype html><title>ReportScene WGSL contract validation</title>');
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  let browser, page;
  const consoleMessages = [];
  try {
    browser = await chromium.launch({ headless: true, executablePath: process.env.REPORTSCENE_BROWSER_EXECUTABLE, args: [
      '--enable-webgpu', '--enable-unsafe-webgpu', '--ignore-gpu-blocklist',
      '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--use-vulkan=swiftshader',
      '--enable-features=Vulkan'
    ] });
    page = await browser.newPage();
    page.on('console', message => consoleMessages.push({ type: message.type(), text: message.text() }));
    page.on('pageerror', error => consoleMessages.push({ type: 'pageerror', text: error.message }));
    await page.goto('http://127.0.0.1:' + server.address().port + '/');
    const result = await page.evaluate(async ({ shader, ellipses, distanceCases, lineFrames }) => {
      if (!navigator.gpu) throw new Error('WebGPU is unavailable: shader validation cannot be skipped or replaced by WebGL.');
      const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
      if (!adapter) throw new Error('No WebGPU adapter was created with the configured software Vulkan driver.');
      const device = await adapter.requestDevice();
      device.lost.then(info => { window.__reportSceneShaderDeviceLoss = { reason: info.reason, message: info.message }; });
      const uncaptured = [];
      device.addEventListener('uncapturederror', event => uncaptured.push(event.error.message));
      const resources = [];
      const own = value => { resources.push(value); return value; };
      let retainedForPresentation = false;
      try {
        device.pushErrorScope('validation');
        // The compute entry invokes the same production stroke function used by
        // the fragment stage. It contains no copied distance implementation.
        const distanceProbe = `
@group(0) @binding(2) var<storage, read_write> ellipse_probe_distances: array<f32>;
@compute @workgroup_size(64)
fn ellipse_distance_probe(@builtin(global_invocation_id) id: vec3u) {
    if (id.x >= arrayLength(&ellipse_probe_distances)) { return; }
    let p = primitives[id.x];
    ellipse_probe_distances[id.x] = ellipse_stroke_distance(p.data.xy, p.rect.zw, p.shape.y, u32(p.data.z));
}
// Freeze the original AABB vertex expression independently of vertex_main.
// This gate checks geometry/interpolation with the production fragment path;
// separate pixel probes and pinned full-WGSL A/B comparisons cover its math.
@vertex fn axis_aligned_reference(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> VertexOutput {
    let corners = array<vec2f, 6>(vec2f(0, 0), vec2f(1, 0), vec2f(0, 1), vec2f(0, 1), vec2f(1, 0), vec2f(1, 1));
    let p = primitives[instance];
    let point = p.quad.xy + corners[vertex] * p.quad.zw;
    var output: VertexOutput;
    output.position = vec4f(point.x / frame.size.x * 2 - 1, 1 - point.y / frame.size.y * 2, 0, 1);
    output.point = point;
    output.index = instance;
    return output;
}`;
        const module = device.createShaderModule({ label: 'ReportScene production WGSL', code: shader + distanceProbe });
        const compilation = await module.getCompilationInfo();
        const messages = compilation.messages.map(message => ({
          type: message.type, text: message.message, line: message.lineNum, column: message.linePos
        }));
        if (messages.some(message => message.type === 'error'))
          throw new Error('WGSL compilation failed: ' + JSON.stringify(messages));

        const frameLayout = device.createBindGroupLayout({ entries: [
          { binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT, buffer: { type: 'uniform', minBindingSize: 16 } },
          { binding: 1, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT, buffer: { type: 'read-only-storage', minBindingSize: 128 } }
        ] });
        const imageLayout = device.createBindGroupLayout({ entries: [
          { binding: 0, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
          { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float', viewDimension: '2d' } }
        ] });
        const pipelineDescriptor = {
          label: 'Report2D explicit layout and premultiplied blend contract',
          layout: device.createPipelineLayout({ bindGroupLayouts: [frameLayout, imageLayout] }),
          vertex: { module, entryPoint: 'vertex_main' },
          fragment: { module, entryPoint: 'fragment_main', targets: [{
            format: 'rgba8unorm',
            blend: {
              color: { operation: 'add', srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
              alpha: { operation: 'add', srcFactor: 'one', dstFactor: 'one-minus-src-alpha' }
            }, writeMask: GPUColorWrite.ALL
          }] },
          primitive: { topology: 'triangle-list', cullMode: 'none' }
        };
        const pipeline = await device.createRenderPipelineAsync(pipelineDescriptor);
        const axisAlignedPipeline = await device.createRenderPipelineAsync({
          ...pipelineDescriptor, label: 'Frozen AABB line vertex reference',
          vertex: { module, entryPoint: 'axis_aligned_reference' }
        });

        const width = 512, height = 512;
        const commands = [];
        // Same 128-byte ABI as WebGpuPrimitive: eight ordered vec4 fields.
        function primitive(rect, color, kind = 0, options = {}) {
          const padding = options.padding ?? 1;
          commands.push([
            ...rect, ...color, ...(options.clip ?? [0, 0, width, height]),
            kind, options.stroke ?? -1, options.aa ?? 1, options.cap ?? 0,
            ...(options.data ?? [0, 0, 0, 0]), ...(options.uv ?? [0, 0, 1, 1]),
            ...(options.quad ?? [rect[0] - padding, rect[1] - padding, rect[2] + padding * 2, rect[3] + padding * 2]),
            ...(options.extra ?? [0, 0, 0, 0])
          ]);
        }
        primitive([8, 8, 40, 40], [1, 0, 0, 1]);
        primitive([24, 24, 40, 40], [0, 0, .5, .5]);
        primitive([80, 8, 48, 40], [0, 1, 0, 1], 0, { clip: [92, 16, 112, 36] });
        primitive([144, 8, 40, 40], [1, 1, 0, 1], 1, { data: [10, 0, 0, 0] });
        primitive([200, 8, 40, 40], [1, 0, 1, 1], 2);
        primitive([8, 77, 48, 6], [0, 1, 1, 1], 3, {
          stroke: 3, cap: 2, data: [8, 80, 56, 80], quad: [4, 76, 56, 8]
        });
        primitive([80.25, 64.25, 48, 48], [1, .5, 0, 1], 5, { data: [0, Math.PI / 2, 0, 0] });
        primitive([144, 64, 80, 64], [1, 1, 1, 1], 6, {
          aa: 0, clip: [144, 64, 216, 128], data: [148.5, 68.5, 8, 8], extra: [2, 2, 0, 0], padding: 0
        });
        primitive([8, 144, 32, 32], [1, 1, 1, 1], 4, { aa: 0 });
        primitive([64, 144, 32, 32], [.5, .25, 0, .5], 4, { aa: 0, extra: [0, 0, 0, 1] });
        primitive([112, 144, 32, 32], [1, 1, 0, 1], 0, { stroke: 2, padding: 3 });
        primitive([160.25, 144.25, 32, 32], [1, 1, 1, 1]);
        for (let cap = 0; cap < 3; cap++) {
          const x = 32 + cap * 32;
          primitive([x, 212, 0, 0], [1, 1, 1, 1], 3, {
            stroke: 4, cap, aa: 0, data: [x, 212, x, 212], quad: [x - 5, 207, 10, 10]
          });
        }
        for (const ellipse of ellipses) primitive(ellipse.rect, ellipse.color, 2, ellipse.options);

        const probeInput = new Float32Array(distanceCases.length * 32);
        for (let index = 0; index < distanceCases.length; index++) {
          const c = distanceCases[index], offset = index * 32;
          probeInput.set(c.radius, offset + 2);
          probeInput[offset + 13] = c.halfStroke;
          probeInput.set(c.point, offset + 16);
          probeInput[offset + 18] = c.iterations;
        }
        const probeStorage = own(device.createBuffer({ size: probeInput.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST }));
        const probeOutput = own(device.createBuffer({ size: distanceCases.length * 4, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC }));
        const probeReadback = own(device.createBuffer({ size: distanceCases.length * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ }));
        device.queue.writeBuffer(probeStorage, 0, probeInput);
        const probeLayout = device.createBindGroupLayout({ entries: [
          { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage', minBindingSize: 128 } },
          { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage', minBindingSize: 4 } }
        ] });
        const probePipeline = await device.createComputePipelineAsync({
          label: 'Production ellipse distance against independent double-precision normal-equation oracle',
          layout: device.createPipelineLayout({ bindGroupLayouts: [probeLayout] }),
          compute: { module, entryPoint: 'ellipse_distance_probe' }
        });
        const probeGroup = device.createBindGroup({ layout: probeLayout, entries: [
          { binding: 1, resource: { buffer: probeStorage } }, { binding: 2, resource: { buffer: probeOutput } }
        ] });

        const primitives = new Float32Array(commands.flat());
        if (primitives.byteLength !== commands.length * 128) throw new Error('Primitive ABI must be exactly 128 bytes.');
        const uniform = own(device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST }));
        const storage = own(device.createBuffer({ size: primitives.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST }));
        device.queue.writeBuffer(uniform, 0, new Float32Array([width, height, 1, 1]));
        device.queue.writeBuffer(storage, 0, primitives);
        const frameGroup = device.createBindGroup({ layout: frameLayout, entries: [
          { binding: 0, resource: { buffer: uniform } }, { binding: 1, resource: { buffer: storage } }
        ] });
        const image = own(device.createTexture({
          size: [2, 2], format: 'rgba8unorm', usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST
        }));
        // Source bytes are premultiplied. The lower-left pixel has half alpha.
        device.queue.writeTexture({ texture: image }, new Uint8Array([
          255, 0, 0, 255, 0, 255, 0, 255,
          0, 0, 128, 128, 255, 255, 255, 255
        ]), { bytesPerRow: 8, rowsPerImage: 2 }, [2, 2]);
        const imageGroup = device.createBindGroup({ layout: imageLayout, entries: [
          { binding: 0, resource: device.createSampler({ minFilter: 'nearest', magFilter: 'nearest' }) },
          { binding: 1, resource: image.createView() }
        ] });
        const target = own(device.createTexture({
          size: [width, height], format: 'rgba8unorm', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC
        }));
        const readback = own(device.createBuffer({
          size: width * height * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ
        }));
        const presentedCanvas = document.createElement('canvas');
        presentedCanvas.id = 'production-wgsl-output';
        presentedCanvas.width = width; presentedCanvas.height = height;
        document.body.style.margin = '0'; document.body.style.background = '#FFFFFF';
        document.body.appendChild(presentedCanvas);
        const canvasContext = presentedCanvas.getContext('webgpu');
        if (!canvasContext) throw new Error('The production shader must also render through a real GPUCanvasContext.');
        // Match Uno's browser surface format and premultiplied-alpha composition.
        // CopySrc additionally lets this test compare the real swapchain texels.
        canvasContext.configure({
          device, format: 'rgba8unorm', alphaMode: 'premultiplied',
          usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC
        });
        const canvasTexture = canvasContext.getCurrentTexture();
        const canvasReadback = own(device.createBuffer({
          size: width * height * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ
        }));
        const encoder = device.createCommandEncoder();
        const computePass = encoder.beginComputePass();
        computePass.setPipeline(probePipeline); computePass.setBindGroup(0, probeGroup);
        computePass.dispatchWorkgroups(Math.ceil(distanceCases.length / 64));
        computePass.end();
        encoder.copyBufferToBuffer(probeOutput, 0, probeReadback, 0, distanceCases.length * 4);
        const pass = encoder.beginRenderPass({ colorAttachments: [{
          view: target.createView(), clearValue: [0, 0, 0, 0], loadOp: 'clear', storeOp: 'store'
        }] });
        pass.setPipeline(pipeline);
        pass.setBindGroup(0, frameGroup); pass.setBindGroup(1, imageGroup);
        // Two draws exercise both ordered instancing and nonzero firstInstance.
        pass.draw(6, 8, 0, 0);
        pass.draw(6, commands.length - 8, 0, 8);
        pass.end();
        encoder.copyTextureToBuffer(
          { texture: target }, { buffer: readback, bytesPerRow: width * 4, rowsPerImage: height }, [width, height]
        );
        const presentationPass = encoder.beginRenderPass({ colorAttachments: [{
          view: canvasTexture.createView(), clearValue: [0, 0, 0, 0], loadOp: 'clear', storeOp: 'store'
        }] });
        presentationPass.setPipeline(pipeline);
        presentationPass.setBindGroup(0, frameGroup); presentationPass.setBindGroup(1, imageGroup);
        presentationPass.draw(6, 8, 0, 0);
        presentationPass.draw(6, commands.length - 8, 0, 8);
        presentationPass.end();
        encoder.copyTextureToBuffer(
          { texture: canvasTexture }, { buffer: canvasReadback, bytesPerRow: width * 4, rowsPerImage: height }, [width, height]
        );
        device.queue.submit([encoder.finish()]);
        await probeReadback.mapAsync(GPUMapMode.READ);
        const ellipseDistances = Array.from(new Float32Array(probeReadback.getMappedRange()));
        probeReadback.unmap();
        await readback.mapAsync(GPUMapMode.READ);
        const pixels = new Uint8Array(readback.getMappedRange()).slice();
        readback.unmap();
        await canvasReadback.mapAsync(GPUMapMode.READ);
        const presentedPixels = new Uint8Array(canvasReadback.getMappedRange()).slice();
        canvasReadback.unmap();
        let canvasPixelMismatches = 0;
        for (let n = 0; n < pixels.length; n++)
          if (pixels[n] !== presentedPixels[n]) canvasPixelMismatches++;
        const lineComparisons = [];
        for (const frame of lineFrames) {
          const lineUniform = own(device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST }));
          const lineData = new Float32Array(frame.commands.flat());
          const lineStorage = own(device.createBuffer({ size: lineData.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST }));
          device.queue.writeBuffer(lineUniform, 0, new Float32Array([...frame.size, ...frame.pixelsPerDip]));
          device.queue.writeBuffer(lineStorage, 0, lineData);
          const lineGroup = device.createBindGroup({ layout: frameLayout, entries: [
            { binding: 0, resource: { buffer: lineUniform } }, { binding: 1, resource: { buffer: lineStorage } }
          ] });
          const outputs = [pipeline, axisAlignedPipeline].map(selectedPipeline => ({
            selectedPipeline,
            target: own(device.createTexture({ size: [frame.width, frame.height], format: 'rgba8unorm', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC })),
            readback: own(device.createBuffer({ size: frame.width * frame.height * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ }))
          }));
          const lineEncoder = device.createCommandEncoder();
          for (const output of outputs) {
            const linePass = lineEncoder.beginRenderPass({ colorAttachments: [{
              view: output.target.createView(), clearValue: [0, 0, 0, 0], loadOp: 'clear', storeOp: 'store'
            }] });
            linePass.setPipeline(output.selectedPipeline); linePass.setBindGroup(0, lineGroup); linePass.setBindGroup(1, imageGroup);
            linePass.draw(6, frame.commands.length - frame.firstInstance, 0, frame.firstInstance);
            linePass.end();
            lineEncoder.copyTextureToBuffer({ texture: output.target },
              { buffer: output.readback, bytesPerRow: frame.width * 4, rowsPerImage: frame.height }, [frame.width, frame.height]);
          }
          device.queue.submit([lineEncoder.finish()]);
          await Promise.all(outputs.map(output => output.readback.mapAsync(GPUMapMode.READ)));
          const [candidate, reference] = outputs.map(output => new Uint8Array(output.readback.getMappedRange()).slice());
          for (const output of outputs) output.readback.unmap();
          let byteMismatches = 0, maximumChannelDifference = 0, nontransparentPixels = 0;
          const mismatches = [];
          for (let index = 0; index < candidate.length; index++) {
            if (index % 4 === 3 && candidate[index] !== 0) nontransparentPixels++;
            const difference = Math.abs(candidate[index] - reference[index]);
            maximumChannelDifference = Math.max(maximumChannelDifference, difference);
            if (!difference) continue;
            byteMismatches++;
            if (mismatches.length < 32) mismatches.push({
              x: Math.floor(index / 4) % frame.width, y: Math.floor(index / 4 / frame.width),
              channel: index % 4, candidate: candidate[index], reference: reference[index]
            });
          }
          lineComparisons.push({ pixelsPerDip: frame.pixelsPerDip, firstInstance: frame.firstInstance,
            primitiveCount: frame.commands.length - frame.firstInstance, byteMismatches, maximumChannelDifference, nontransparentPixels, mismatches });
        }
        await device.queue.onSubmittedWorkDone();
        const validationError = await device.popErrorScope();
        if (validationError) throw new Error('WebGPU validation failed: ' + validationError.message);
        if (uncaptured.length) throw new Error('Uncaptured WebGPU errors: ' + JSON.stringify(uncaptured));

        const samples = [];
        function sample(name, x, y, expected, tolerance = 1) {
          const offset = (y * width + x) * 4;
          samples.push({ name, x, y, expected, actual: Array.from(pixels.subarray(offset, offset + 4)), tolerance });
        }
        sample('opaque rectangle interior', 16, 16, [255, 0, 0, 255]);
        sample('premultiplied source over destination', 32, 32, [128, 0, 128, 255]);
        sample('premultiplied color over transparent', 56, 56, [0, 0, 128, 128]);
        sample('clip interior', 100, 24, [0, 255, 0, 255]);
        sample('clip left exterior', 84, 24, [0, 0, 0, 0]);
        sample('clip top exterior', 100, 12, [0, 0, 0, 0]);
        sample('clip excludes right edge', 112, 24, [0, 0, 0, 0]);
        sample('rounded rectangle interior', 164, 28, [255, 255, 0, 255]);
        sample('rounded rectangle corner', 144, 8, [0, 0, 0, 0]);
        sample('ellipse interior', 220, 28, [255, 0, 255, 255]);
        sample('ellipse corner exterior', 200, 8, [0, 0, 0, 0]);
        sample('line body', 32, 80, [0, 255, 255, 255]);
        sample('round line cap', 6, 80, [0, 255, 255, 255]);
        sample('pie included quadrant', 112, 96, [255, 128, 0, 255]);
        sample('pie radial edge fractional coverage', 112, 88, [191, 96, 0, 191], 2);
        sample('pie excludes upper quadrant', 112, 78, [0, 0, 0, 0]);
        sample('pie excludes lower-left quadrant', 94, 96, [0, 0, 0, 0]);
        sample('procedural grid dot', 148, 68, [255, 255, 255, 255]);
        sample('procedural grid gap', 152, 68, [0, 0, 0, 0]);
        sample('procedural grid respects clip', 220, 124, [0, 0, 0, 0]);
        sample('texture upper-left', 16, 152, [255, 0, 0, 255]);
        sample('texture upper-right', 32, 152, [0, 255, 0, 255]);
        sample('texture premultiplied texel', 16, 168, [0, 0, 128, 128]);
        sample('texture lower-right', 32, 168, [255, 255, 255, 255]);
        sample('glyph mask uses alpha, not source RGB', 72, 152, [128, 64, 0, 128]);
        sample('glyph mask multiplies premultiplied tint', 72, 168, [64, 32, 0, 64]);
        sample('rectangle stroke interior edge', 112, 160, [255, 255, 0, 255]);
        sample('rectangle stroke exterior extension', 110, 160, [255, 255, 0, 255]);
        sample('rectangle miter outer corner', 110, 142, [255, 255, 0, 255]);
        sample('rectangle stroke leaves center empty', 128, 160, [0, 0, 0, 0]);
        sample('analytic fractional-pixel coverage', 160, 160, [191, 191, 191, 191], 2);
        sample('analytic edge outside', 159, 160, [0, 0, 0, 0]);
        sample('analytic edge interior', 161, 160, [255, 255, 255, 255]);
        sample('zero-length butt line is empty', 32, 212, [0, 0, 0, 0]);
        sample('zero-length square line center', 64, 212, [255, 255, 255, 255]);
        sample('zero-length square line corner', 60, 208, [255, 255, 255, 255]);
        sample('zero-length round line center', 96, 212, [255, 255, 255, 255]);
        sample('zero-length round line excludes square corner', 92, 208, [0, 0, 0, 0]);
        sample('untouched target remains transparent', 240, 240, [0, 0, 0, 0]);
        sample('thick circle center is solid', 48, 304, [0, 128, 255, 255]);
        sample('thick circle near center is solid', 49, 304, [0, 128, 255, 255]);
        sample('thick circle outer stroke interior', 77, 304, [0, 128, 255, 255]);
        sample('thick circle outer boundary coverage', 78, 304, [0, 64, 128, 128], 2);
        sample('thick circle outer geometry is not a clipped oversized ellipse', 79, 304, [0, 0, 0, 0]);
        sample('collapsed circle inner contour has no half-alpha center', 112, 304, [0, 255, 0, 255]);
        sample('collapsed circle outer boundary coverage', 132, 304, [0, 128, 0, 128], 2);
        sample('thin circle preserves inner hole', 176, 304, [0, 0, 0, 0]);
        sample('thin circle exact inner boundary', 194, 304, [128, 64, 0, 128], 2);
        sample('thin circle stroke body', 197, 304, [255, 128, 0, 255]);
        sample('thin circle exact outer boundary', 198, 304, [128, 64, 0, 128], 2);
        sample('eccentric ellipse medial stroke body', 360, 300, [255, 0, 128, 255]);
        sample('eccentric ellipse preserves remaining inner hole', 320, 300, [0, 0, 0, 0]);
        sample('eccentric ellipse outer stroke body', 360, 314, [255, 0, 128, 255]);
        sample('eccentric ellipse corner outside true stroke', 380, 315, [0, 0, 0, 0]);
        sample('eccentric ellipse major-axis boundary', 388, 300, [128, 0, 64, 128], 2);
        sample('eccentric ellipse minor-axis boundary', 320, 318, [128, 0, 64, 128], 2);
        sample('thick eccentric ellipse has solid center', 320, 390, [128, 0, 255, 255]);
        sample('thick eccentric ellipse outer minor-axis boundary', 320, 420, [64, 0, 128, 128], 2);
        sample('nonuniform transform preserves horizontal stroke extent', 489, 390, [0, 255, 255, 255]);
        sample('nonuniform transform horizontal boundary', 490, 390, [0, 128, 128, 128], 2);
        sample('nonuniform transform vertical boundary', 448, 404, [0, 128, 128, 128], 2);
        sample('very eccentric ellipse has solid center', 112, 430, [255, 255, 0, 255]);
        sample('very eccentric ellipse minor-axis boundary', 112, 438, [128, 128, 0, 128], 2);
        sample('very eccentric ellipse major-axis boundary', 198, 430, [128, 128, 0, 128], 2);
        sample('filled ellipse exact center', 448, 48, [0, 255, 128, 255]);
        sample('filled ellipse center neighbor has no gradient pole', 449, 48, [0, 255, 128, 255]);
        sample('filled ellipse diagonal center neighbor', 449, 49, [0, 255, 128, 255]);
        sample('tiny local ellipse units preserve inner hole', 448, 150, [0, 0, 0, 0]);
        sample('tiny local ellipse units preserve opaque stroke', 495, 150, [255, 128, 128, 255]);
        sample('tiny local ellipse units preserve boundary coverage', 496, 150, [128, 64, 64, 128], 2);
        sample('huge local ellipse units preserve inner hole', 448, 230, [0, 0, 0, 0]);
        sample('huge local ellipse units preserve opaque stroke', 495, 230, [128, 255, 128, 255]);
        sample('huge local ellipse units preserve boundary coverage', 496, 230, [64, 128, 64, 128], 2);

        // PNG is only a visual aid. Assertions use raw premultiplied readback bytes.
        const display = new Uint8ClampedArray(pixels.length);
        for (let offset = 0; offset < pixels.length; offset += 4) {
          const alpha = pixels[offset + 3];
          for (let channel = 0; channel < 3; channel++)
            display[offset + channel] = alpha ? Math.round(pixels[offset + channel] * 255 / alpha) : 0;
          display[offset + 3] = alpha;
        }
        const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height;
        canvas.getContext('2d').putImageData(new ImageData(display, width, height), 0, 0);
        // Keep the actual WebGPU device and its objects alive across Node's
        // screenshot. Destroying them here would clear the canvas before inspection.
        window.__reportSceneShaderValidation = { device, resources, canvasContext };
        retainedForPresentation = true;
        return {
          canvasPixelMismatches, presentationDrawCalls: 2,
          adapter: {
            vendor: adapter.info.vendor, architecture: adapter.info.architecture,
            device: adapter.info.device, description: adapter.info.description,
            isFallbackAdapter: adapter.info.isFallbackAdapter
          },
          limits: { maxTextureDimension2D: device.limits.maxTextureDimension2D, maxStorageBufferBindingSize: device.limits.maxStorageBufferBindingSize },
          compilationMessages: messages, width, height, primitiveCount: commands.length, drawCalls: 2,
          samples, ellipseDistances, lineComparisons, imageDataUrl: canvas.toDataURL('image/png')
        };
      } finally {
        if (!retainedForPresentation) {
          for (const resource of resources.reverse()) resource.destroy();
          device.destroy();
        }
      }
    }, { shader: code, ellipses, distanceCases: ellipseReference.cases, lineFrames: lineReferenceFrames() });
    const distanceErrors = result.ellipseDistances.map((actual, index) => {
      const c = ellipseReference.cases[index];
      const error = Math.abs(actual - c.expected);
      return { index, actual, error, projectedErrorPixels: error * (c.localToPixel ?? 1), ...c };
    });
    const distanceFailures = distanceErrors.filter(c => !Number.isFinite(c.actual) || c.error > c.tolerance);
    result.ellipseDistanceValidation = {
      seed: ellipseReference.seed, cases: distanceErrors.length,
      maximumLocalAbsoluteError: Math.max(...distanceErrors.map(c => c.error)),
      maximumProjectedErrorPixels: Math.max(...distanceErrors.map(c => c.projectedErrorPixels)),
      maximumToleranceRatio: Math.max(...distanceErrors.map(c => c.error / c.tolerance)),
      worstCase: distanceErrors.reduce((a, b) => a.projectedErrorPixels > b.projectedErrorPixels ? a : b),
      failureCount: distanceFailures.length, failures: distanceFailures.slice(0, 20)
    };
    delete result.ellipseDistances;
    const imageDataUrl = result.imageDataUrl;
    delete result.imageDataUrl;
    await writeFile(evidence + '/pixels.png', Buffer.from(imageDataUrl.split(',')[1], 'base64'));
    // Canvas readback alone can pass while the browser's compositor loses its
    // WebGPU instance. Inspect the real presented canvas too; never substitute the
    // offscreen PNG for this screenshot (Chromium issue 575926500).
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    const presented = await page.screenshot({ clip: { x: 0, y: 0, width: result.width, height: result.height } });
    await writeFile(evidence + '/presented.png', presented);
    const screen = screenshotPixels(presented);
    result.presentedColorCount = screen.colorCount();
    result.deviceLoss = await page.evaluate(() => window.__reportSceneShaderDeviceLoss ?? null);
    result.presentationSamples = result.samples.map(sample => ({
      name: sample.name, x: sample.x, y: sample.y,
      expected: sample.expected.slice(0, 3).map(channel => Math.min(255, channel + 255 - sample.expected[3])),
      actual: screen.pixel(sample.x, sample.y).slice(0, 3),
      tolerance: Math.max(2, sample.tolerance)
    }));
    await writeFile(evidence + '/results.json', JSON.stringify(result, null, 2));
    assert.equal(distanceErrors.length, ellipseReference.cases.length, 'Every seeded ellipse query must be executed by the production WGSL function.');
    assert.equal(result.ellipseDistanceValidation.failureCount, 0, 'Production ellipse distance exceeds its geometric plus float32 error bound; inspect shader-evidence/results.json.');
    assert.equal(result.lineComparisons.length, 3);
    assert.ok(result.lineComparisons.every(frame => frame.primitiveCount === 17 && frame.firstInstance === 3 && frame.nontransparentPixels > 1000));
    assert.deepEqual(result.lineComparisons.filter(frame => frame.byteMismatches !== 0), [],
      'Line vertex geometry and interpolation must match the frozen AABB vertex path at fractional/nonuniform DPR.');
    assert.equal(result.canvasPixelMismatches, 0, 'GPUCanvasContext texels must equal the offscreen production shader result.');
    assert.equal(result.deviceLoss, null, 'The real presented canvas must retain a valid WebGPU device.');
    assert.ok(result.presentedColorCount > 30, 'The browser must present shader colors, not a blank canvas.');
    const presentationFailures = result.presentationSamples.filter(sample => sample.actual.some((value, channel) =>
      Math.abs(value - sample.expected[channel]) > sample.tolerance));
    assert.deepEqual(presentationFailures, [], 'Presented canvas differs from premultiplied shader output over the white page.');
    const failed = result.samples.filter(sample => sample.actual.some((value, channel) =>
      Math.abs(value - sample.expected[channel]) > sample.tolerance));
    assert.deepEqual(failed, [], 'Production WGSL pixel mismatches; inspect shader-evidence/results.json and pixels.png.');
    assert.equal(result.compilationMessages.filter(message => message.type === 'error').length, 0);
    assert.equal(result.primitiveCount, 25);
    assert.equal(result.drawCalls, 2);
    console.log('WGSL PIXELS: ' + result.samples.length + '/' + result.samples.length + ' passed; explicit native WebGPU pipeline, adapter=' + JSON.stringify(result.adapter));
  } catch (error) {
    await writeFile(evidence + '/failure.json', JSON.stringify({ message: error.message, stack: error.stack }, null, 2));
    throw error;
  } finally {
    await writeFile(evidence + '/console.json', JSON.stringify(consoleMessages, null, 2));
    if (page && !page.isClosed()) await page.evaluate(() => {
      const owned = window.__reportSceneShaderValidation;
      if (!owned) return;
      owned.canvasContext.unconfigure();
      for (const resource of owned.resources.reverse()) resource.destroy();
      owned.device.destroy();
      delete window.__reportSceneShaderValidation;
    }).catch(() => {});
    if (browser) await browser.close();
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});
