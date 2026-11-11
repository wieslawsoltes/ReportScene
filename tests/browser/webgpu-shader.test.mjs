// Direct WGSL contract validation. This does not represent, replace or mock the Uno
// application: the browser scenarios separately exercise the real .NET UI and GPU host.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const evidence = fileURLToPath(new URL('./shader-evidence/', import.meta.url));
const shaderPath = new URL('../../src/ReportScene.Rendering.WebGpu/Shaders/Report2D.wgsl', import.meta.url);

test('Report2D WGSL compiles and preserves geometry, clips, premultiplied blending and grid pixels', { timeout: 180_000 }, async () => {
  await mkdir(evidence, { recursive: true });
  const code = await readFile(shaderPath, 'utf8');
  const server = createServer((_request, response) => {
    response.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    response.end('<!doctype html><title>ReportScene WGSL contract validation</title>');
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  let browser;
  const consoleMessages = [];
  try {
    browser = await chromium.launch({ headless: true, args: [
      '--enable-webgpu', '--enable-unsafe-webgpu', '--ignore-gpu-blocklist',
      '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--use-webgpu-adapter=swiftshader',
      '--enable-features=Vulkan', '--disable-vulkan-surface'
    ] });
    const page = await browser.newPage();
    page.on('console', message => consoleMessages.push({ type: message.type(), text: message.text() }));
    page.on('pageerror', error => consoleMessages.push({ type: 'pageerror', text: error.message }));
    await page.goto('http://127.0.0.1:' + server.address().port + '/');
    const result = await page.evaluate(async shader => {
      if (!navigator.gpu) throw new Error('WebGPU is unavailable: shader validation cannot be skipped or replaced by WebGL.');
      const adapter = await navigator.gpu.requestAdapter({ forceFallbackAdapter: true });
      if (!adapter) throw new Error('No SwiftShader fallback adapter was created.');
      const device = await adapter.requestDevice();
      const uncaptured = [];
      device.addEventListener('uncapturederror', event => uncaptured.push(event.error.message));
      const resources = [];
      const own = value => { resources.push(value); return value; };
      try {
        device.pushErrorScope('validation');
        const module = device.createShaderModule({ label: 'ReportScene production WGSL', code: shader });
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
        const pipeline = await device.createRenderPipelineAsync({
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
        });

        const width = 256, height = 256;
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
        primitive([80, 64, 48, 48], [1, .5, 0, 1], 5, { data: [0, Math.PI / 2, 0, 0] });
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
        const encoder = device.createCommandEncoder();
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
        device.queue.submit([encoder.finish()]);
        await readback.mapAsync(GPUMapMode.READ);
        const pixels = new Uint8Array(readback.getMappedRange()).slice();
        readback.unmap();
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
        return {
          adapter: {
            vendor: adapter.info.vendor, architecture: adapter.info.architecture,
            device: adapter.info.device, description: adapter.info.description,
            isFallbackAdapter: adapter.info.isFallbackAdapter
          },
          limits: { maxTextureDimension2D: device.limits.maxTextureDimension2D, maxStorageBufferBindingSize: device.limits.maxStorageBufferBindingSize },
          compilationMessages: messages, width, height, primitiveCount: commands.length, drawCalls: 2,
          samples, imageDataUrl: canvas.toDataURL('image/png')
        };
      } finally {
        for (const resource of resources.reverse()) resource.destroy();
        device.destroy();
      }
    }, code);
    const imageDataUrl = result.imageDataUrl;
    delete result.imageDataUrl;
    await writeFile(evidence + '/pixels.png', Buffer.from(imageDataUrl.split(',')[1], 'base64'));
    await writeFile(evidence + '/results.json', JSON.stringify(result, null, 2));
    const failed = result.samples.filter(sample => sample.actual.some((value, channel) =>
      Math.abs(value - sample.expected[channel]) > sample.tolerance));
    assert.deepEqual(failed, [], 'Production WGSL pixel mismatches; inspect shader-evidence/results.json and pixels.png.');
    assert.equal(result.compilationMessages.filter(message => message.type === 'error').length, 0);
    assert.equal(result.primitiveCount, 15);
    assert.equal(result.drawCalls, 2);
    console.log('WGSL PIXELS: ' + result.samples.length + '/' + result.samples.length + ' passed; explicit native WebGPU pipeline, adapter=' + JSON.stringify(result.adapter));
  } catch (error) {
    await writeFile(evidence + '/failure.json', JSON.stringify({ message: error.message, stack: error.stack }, null, 2));
    throw error;
  } finally {
    await writeFile(evidence + '/console.json', JSON.stringify(consoleMessages, null, 2));
    if (browser) await browser.close();
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});
