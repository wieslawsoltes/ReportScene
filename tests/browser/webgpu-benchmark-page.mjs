import { buildWorkload, comparePixelBytes, compareTrialTimings, decodeTimestampPair, fixtureVersion,
  primitiveStrideBytes, shaderVariantOrder, summarizeFrames, textureFixture } from './webgpu-benchmark-workloads.mjs';

// This module creates and owns a standalone device. It never touches Uno's device,
// queue, callbacks, feature negotiation or resource lifetime.
export async function runBenchmark(config) {
  const progress = window.__reportSceneBenchmarkProgress = {
    status: 'initializing', workload: null, completedTrials: 0, uncapturedErrors: [], deviceLoss: null
  };
  const started = performance.now();
  const deadline = started + config.timeoutMs;
  if (!navigator.gpu) throw new Error('WebGPU is unavailable; this benchmark has no alternate renderer.');
  const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
  if (!adapter) throw new Error('No WebGPU adapter is available for the selected browser configuration.');
  const info = adapter.info;
  const adapterInfo = {
    vendor: info.vendor, architecture: info.architecture, device: info.device, description: info.description,
    isFallbackAdapter: info.isFallbackAdapter ?? adapter.isFallbackAdapter ?? null
  };
  progress.adapter = adapterInfo;
  const knownSoftware = adapterInfo.isFallbackAdapter === true || /swiftshader|llvmpipe|software/i.test(
    [info.vendor, info.architecture, info.device, info.description].join(' '));
  if (config.software && !knownSoftware) throw new Error('--software did not select a reported software adapter.');
  if (config.requireHardware && (knownSoftware || adapterInfo.isFallbackAdapter !== false))
    throw new Error('--require-hardware requires a reported, non-fallback hardware adapter.');
  const timestampSupported = adapter.features.has('timestamp-query');
  progress.timestampSupported = timestampSupported;
  if (config.timestamps === 'required' && !timestampSupported)
    throw new Error('The selected adapter does not expose the required timestamp-query feature.');
  const timestampEnabled = config.timestamps !== 'off' && timestampSupported;
  // Optional features must be requested here, on this benchmark's own device.
  const device = await adapter.requestDevice({ requiredFeatures: timestampEnabled ? ['timestamp-query'] : [] });
  const resources = [];
  const own = resource => { resources.push(resource); return resource; };
  let destroying = false, canvasContext = null;
  device.lost.then(loss => {
    if (!destroying) progress.deviceLoss = { reason: loss.reason, message: loss.message };
  });
  device.addEventListener('uncapturederror', event => progress.uncapturedErrors.push(event.error.message));
  const health = () => {
    if (progress.deviceLoss) throw new Error('WebGPU device lost: ' + JSON.stringify(progress.deviceLoss));
    if (progress.uncapturedErrors.length) throw new Error('Uncaptured GPU errors: ' + JSON.stringify(progress.uncapturedErrors));
    if (performance.now() > deadline) throw new Error('Benchmark exceeded the configured wall-clock budget.');
  };
  async function checked(label, action) {
    device.pushErrorScope('validation'); device.pushErrorScope('internal'); device.pushErrorScope('out-of-memory');
    let value, failure;
    try { value = await action(); } catch (error) { failure = error; }
    const errors = [];
    for (let index = 0; index < 3; index++) {
      const error = await device.popErrorScope();
      if (error) errors.push(error.message);
    }
    if (errors.length) throw new Error(label + ': ' + errors.join('; '), { cause: failure });
    if (failure) throw failure;
    health(); return value;
  }
  const digest = async bytes => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)))
    .map(value => value.toString(16).padStart(2, '0')).join('');
  try {
    const width = Math.round(config.width * config.dpr), height = Math.round(config.height * config.dpr);
    if (Math.max(width, height) > device.limits.maxTextureDimension2D)
      throw new Error('The requested viewport exceeds the selected device texture limit.');
    const { frameLayout, imageLayout } = await checked('Bind group layout initialization', async () => {
      const frameLayout = device.createBindGroupLayout({ entries: [
        { binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT, buffer: { type: 'uniform', minBindingSize: 16 } },
        { binding: 1, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT, buffer: { type: 'read-only-storage', minBindingSize: primitiveStrideBytes } }
      ] });
      const imageLayout = device.createBindGroupLayout({ entries: [
        { binding: 0, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
        { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float', viewDimension: '2d' } }
      ] });
      return { frameLayout, imageLayout };
    });
    const variants = new Map();
    for (const [name, path] of [['candidate', '/Report2D.wgsl'], ...(config.baselineShader ? [['baseline', '/Baseline.wgsl']] : [])]) {
      const response = await fetch(path);
      if (!response.ok) throw new Error(name + ' WGSL source could not be loaded.');
      const code = await response.text(), pipelineStarted = performance.now();
      const result = await checked(name + ' pipeline initialization', async () => {
        const module = device.createShaderModule({ label: 'ReportScene ' + name + ' WGSL benchmark', code });
        const compilation = await module.getCompilationInfo();
        const compilationMessages = compilation.messages.map(message => ({ type: message.type, text: message.message, line: message.lineNum }));
        if (compilationMessages.some(message => message.type === 'error'))
          throw new Error(name + ' WGSL compilation failed: ' + JSON.stringify(compilationMessages));
        const pipeline = await device.createRenderPipelineAsync({
          label: 'Production 2D ordered instancing, premultiplied source-over, 1x sampling',
          layout: device.createPipelineLayout({ bindGroupLayouts: [frameLayout, imageLayout] }),
          vertex: { module, entryPoint: 'vertex_main' },
          fragment: { module, entryPoint: 'fragment_main', targets: [{
            format: 'rgba8unorm', blend: {
              color: { operation: 'add', srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
              alpha: { operation: 'add', srcFactor: 'one', dstFactor: 'one-minus-src-alpha' }
            }, writeMask: GPUColorWrite.ALL
          }] }, primitive: { topology: 'triangle-list', cullMode: 'none' }
        });
        return { pipeline, compilationMessages };
      });
      variants.set(name, { ...result, pipelineInitializationWallMs: performance.now() - pipelineStarted });
    }
    const { compilationMessages, pipelineInitializationWallMs } = variants.get('candidate');
    let textureUploadBytes = 0;
    const textureData = Array.from({ length: 5 }, (_, index) => textureFixture(index));
    const textureFixtures = await Promise.all(textureData.map(async (levels, index) => ({ index,
      levels: await Promise.all(levels.map(async ({ bytes, width, height }) => ({ width, height,
        byteLength: bytes.byteLength, sha256: await digest(bytes) }))) })));
    const imageGroups = await checked('Texture fixture initialization', async () => {
      const sampler = device.createSampler({ minFilter: 'linear', magFilter: 'linear', mipmapFilter: 'linear' });
      return textureData.map((levels, index) => {
        const texture = own(device.createTexture({
          label: 'Resident benchmark fixture ' + index, size: [levels[0].width, levels[0].height],
          mipLevelCount: levels.length, format: 'rgba8unorm', usage: GPUTextureUsage.COPY_DST | GPUTextureUsage.TEXTURE_BINDING
        }));
        for (let level = 0; level < levels.length; level++) {
          const { bytes, width, height } = levels[level]; textureUploadBytes += bytes.byteLength;
          device.queue.writeTexture({ texture, mipLevel: level }, bytes, { bytesPerRow: width * 4, rowsPerImage: height }, [width, height]);
        }
        return device.createBindGroup({ layout: imageLayout, entries: [
          { binding: 0, resource: sampler }, { binding: 1, resource: texture.createView() }
        ] });
      });
    });
    const target = config.target === 'offscreen' ? own(device.createTexture({
      size: [width, height], format: 'rgba8unorm', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC
    })) : null;
    if (!target) {
      const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height;
      canvas.style.width = config.width + 'px'; canvas.style.height = config.height + 'px';
      document.body.style.margin = '0'; document.body.appendChild(canvas);
      canvasContext = canvas.getContext('webgpu');
      if (!canvasContext) throw new Error('GPUCanvasContext is unavailable.');
      canvasContext.configure({ device, format: 'rgba8unorm', alphaMode: 'premultiplied',
        usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
    }
    const querySet = timestampEnabled ? own(device.createQuerySet({ type: 'timestamp', count: 2 })) : null;
    const queryResolve = timestampEnabled ? own(device.createBuffer({ size: 16, usage: GPUBufferUsage.QUERY_RESOLVE | GPUBufferUsage.COPY_SRC })) : null;
    const queryReadback = timestampEnabled ? own(device.createBuffer({ size: 16, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ })) : null;
    const timestampWrites = timestampEnabled ? { querySet, beginningOfPassWriteIndex: 0, endOfPassWriteIndex: 1 } : undefined;
    const frameData = new Float32Array([config.width, config.height, width / config.width, height / config.height]);
    const frameUniformSha256 = await digest(frameData);
    const workloadResults = new Map(), baselineResults = new Map(), comparisonResults = new Map();
    // A deadline may interrupt a later workload after earlier trials completed.
    // Retain only existing bounded result records, never readback pixel arrays.
    // The controller writes these under failed progress with complete=false;
    // a partial run receives no pooled summary or successful benchmark status.
    const completedEvidence = progress.completedEvidence = {
      complete: false, fixtureVersion, frameUniformSha256, textureFixtures,
      timestampEnabled, workloads: [], baselineWorkloads: [], comparisons: []
    };
    await device.queue.onSubmittedWorkDone();
    progress.status = 'measuring';
    for (let repeat = 0; repeat < config.repeats; repeat++) {
      // Rotate a fixed order between repeats so one workload is not always first.
      const order = config.workloads.map((_, index) => config.workloads[(index + repeat) % config.workloads.length]);
      for (const name of order) {
        progress.workload = name; health();
        const generationStarted = performance.now();
        const workload = buildWorkload(name, config);
        const generationWallMs = performance.now() - generationStarted;
        if (workload.primitives.byteLength > device.limits.maxStorageBufferBindingSize || workload.primitives.byteLength > device.limits.maxBufferSize)
          throw new Error('The instance buffer exceeds the selected device limits.');
        const primitiveSha256 = await digest(workload.primitives);
        const textureOrderSha256 = await digest(workload.textures);
        const uniform = device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
        const storage = device.createBuffer({ size: workload.primitives.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
        try {
          const group = device.createBindGroup({ layout: frameLayout, entries: [
            { binding: 0, resource: { buffer: uniform } }, { binding: 1, resource: { buffer: storage } }
          ] });
          device.queue.writeBuffer(uniform, 0, frameData); device.queue.writeBuffer(storage, 0, workload.primitives);
          await device.queue.onSubmittedWorkDone();
          function encode(texture, timing, variant, readback) {
            const encoder = device.createCommandEncoder();
            const pass = encoder.beginRenderPass({
              colorAttachments: [{ view: texture.createView(), clearValue: [1, 1, 1, 1], loadOp: 'clear', storeOp: 'store' }],
              ...(timing ? { timestampWrites } : {})
            });
            pass.setPipeline(variants.get(variant).pipeline); pass.setBindGroup(0, group);
            let textureIndex = -1;
            for (const batch of workload.batches) {
              if (textureIndex !== batch.texture) { pass.setBindGroup(1, imageGroups[batch.texture]); textureIndex = batch.texture; }
              pass.draw(6, batch.count, 0, batch.firstInstance);
            }
            pass.end();
            if (timing) {
              encoder.resolveQuerySet(querySet, 0, 2, queryResolve, 0);
              encoder.copyBufferToBuffer(queryResolve, 0, queryReadback, 0, 16);
            }
            if (readback) encoder.copyTextureToBuffer({ texture },
              { buffer: readback.buffer, bytesPerRow: readback.bytesPerRow, rowsPerImage: height }, [width, height]);
            return encoder.finish();
          }
          async function frame(variant) {
            health();
            const start = performance.now();
            if (config.uploads === 'instances') {
              device.queue.writeBuffer(uniform, 0, frameData);
              device.queue.writeBuffer(storage, 0, workload.primitives);
            }
            const command = encode(target ?? canvasContext.getCurrentTexture(), timestampEnabled, variant);
            const beforeSubmit = performance.now();
            device.queue.submit([command]);
            const submitted = performance.now();
            await device.queue.onSubmittedWorkDone();
            const completed = performance.now();
            let gpuRenderPassMs = null, timestampRejection = null, timestampDeltaNs = null;
            if (timestampEnabled) {
              await queryReadback.mapAsync(GPUMapMode.READ);
              const values = new BigUint64Array(queryReadback.getMappedRange());
              // Counter resets are permitted by the WebGPU specification. Retain
              // rejected data explicitly; zero deltas remain valid quantized samples.
              ({ gpuRenderPassMs, timestampDeltaNs, timestampRejection } = decodeTimestampPair(values[0], values[1]));
              queryReadback.unmap();
            }
            const queryRead = performance.now();
            health();
            return {
              cpuPrepareMs: beforeSubmit - start, cpuSubmitCallMs: submitted - beforeSubmit,
              queueCompletionWallMs: completed - start, postSubmitWaitMs: completed - submitted,
              queryReadbackWallMs: timestampEnabled ? queryRead - completed : 0,
              gpuRenderPassMs, timestampDeltaNs, timestampRejection
            };
          }
          const pairedOutputs = new Map(), variantOrder = shaderVariantOrder(repeat, Boolean(config.baselineShader));
          for (const variant of variantOrder) {
            progress.variant = variant;
            const trial = await checked(name + ' ' + variant + ' repeat ' + repeat, async () => {
              for (let index = 0; index < config.warmup; index++) await frame(variant);
              const samples = [];
              for (let index = 0; index < config.samples; index++) samples.push(await frame(variant));
              return { repeat, variant, executionIndex: progress.completedTrials, generationWallMs, samples, statistics: summarizeFrames(samples) };
            });
            // Readback is outside the timed frames. In comparison mode retain
            // this pair's two bounded images and compare every RGBA byte; hashes
            // alone do not replace the equality check or permit an AA tolerance.
            const bytesPerRow = Math.ceil(width * 4 / 256) * 256;
            const readback = device.createBuffer({ size: bytesPerRow * height, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
            try {
              const output = await checked('Output readback ' + variant, async () => {
                device.queue.submit([encode(target ?? canvasContext.getCurrentTexture(), false, variant, { buffer: readback, bytesPerRow })]);
                await readback.mapAsync(GPUMapMode.READ);
                const bytes = new Uint8Array(readback.getMappedRange());
                const packed = new Uint8Array(width * height * 4);
                let changedPixels = 0;
                for (let y = 0; y < height; y++) {
                  packed.set(bytes.subarray(y * bytesPerRow, y * bytesPerRow + width * 4), y * width * 4);
                  for (let x = 0; x < width; x++) {
                    const offset = y * bytesPerRow + x * 4;
                    if (bytes[offset] !== 255 || bytes[offset + 1] !== 255 || bytes[offset + 2] !== 255) changedPixels++;
                  }
                }
                readback.unmap();
                if (!changedPixels) throw new Error('The rendered viewport is entirely the clear color.');
                return { pixels: packed, metadata: { changedPixels, pixelSha256: await digest(packed), measured: false } };
              });
              trial.output = output.metadata;
              pairedOutputs.set(variant, { pixels: output.pixels, trial });
            } finally { readback.destroy(); }
            const results = variant === 'candidate' ? workloadResults : baselineResults;
            if (!results.has(name)) {
              const result = {
                name, primitiveCount: workload.count, primitiveSha256, textureOrderSha256, primitiveBytes: workload.primitives.byteLength,
                kindCounts: workload.kindCounts, glyphMasks: workload.glyphCount, images: workload.imageCount,
                drawCalls: workload.batches.length,
                textureBindChanges: workload.batches.filter((batch, index) => !index || batch.texture !== workload.batches[index - 1].texture).length,
                uploadBytesPerFrame: config.uploads === 'instances' ? workload.primitives.byteLength + 16 : 0,
                trials: []
              };
              results.set(name, result);
              (variant === 'candidate' ? completedEvidence.workloads : completedEvidence.baselineWorkloads).push(result);
            }
            const result = results.get(name);
            if (result.primitiveSha256 !== primitiveSha256 || result.textureOrderSha256 !== textureOrderSha256
                || result.trials.some(previous => previous.output.pixelSha256 !== trial.output.pixelSha256))
              throw new Error(name + ' ' + variant + ' fixture or output changed between repeats.');
            result.trials.push(trial);
            progress.completedTrials++;
          }
          if (config.baselineShader) {
            const baseline = pairedOutputs.get('baseline'), candidate = pairedOutputs.get('candidate');
            const pixelComparison = comparePixelBytes(baseline.pixels, candidate.pixels);
            pixelComparison.examples = pixelComparison.examples.map(example => ({ ...example,
              x: example.pixel % width, y: Math.floor(example.pixel / width) }));
            if (pixelComparison.differingBytes) {
              progress.comparisonFailure = { name, repeat, primitiveSha256, pixelComparison,
                baselineOutput: baseline.trial.output, candidateOutput: candidate.trial.output };
              throw new Error('Shader comparison changed ' + pixelComparison.differingPixels + ' pixels (' +
                pixelComparison.differingBytes + ' bytes) for ' + name + ' repeat ' + repeat + '. Exact RGBA equality is required.');
            }
            if (!comparisonResults.has(name)) {
              const result = { name, primitiveSha256, textureOrderSha256, trials: [] };
              comparisonResults.set(name, result);
              completedEvidence.comparisons.push({ name, primitiveSha256, textureOrderSha256, trials: [] });
            }
            // The checkpoint deliberately has no performance ratios. Its pair
            // records attest only to the strict equality check; raw separate
            // baseline/candidate times remain on their completed trials.
            completedEvidence.comparisons.find(result => result.name === name).trials.push({ repeat, variantOrder, pixelComparison });
            comparisonResults.get(name).trials.push({ repeat, variantOrder, pixelComparison,
              timings: compareTrialTimings(baseline.trial, candidate.trial) });
          }
        } finally { storage.destroy(); uniform.destroy(); }
      }
    }
    health(); progress.status = 'complete';
    const aggregate = results => config.workloads.map(name => {
      const result = results.get(name);
      return { ...result, statistics: summarizeFrames(result.trials.flatMap(trial => trial.samples)) };
    });
    const workloads = aggregate(workloadResults), baselineWorkloads = config.baselineShader ? aggregate(baselineResults) : [];
    return {
      adapter: adapterInfo,
      adapterClassification: knownSoftware ? 'reported-software' : adapterInfo.isFallbackAdapter === false ? 'reported-nonfallback' : 'unclassified',
      adapterClassificationBasis: 'Browser-reported fallback status and software driver names; not independent verification of physical hardware.',
      browser: { userAgent: navigator.userAgent, hardwareConcurrency: navigator.hardwareConcurrency, crossOriginIsolated },
      limits: { maxTextureDimension2D: device.limits.maxTextureDimension2D,
        maxStorageBufferBindingSize: device.limits.maxStorageBufferBindingSize, maxBufferSize: device.limits.maxBufferSize },
      features: { adapter: [...adapter.features].sort(), device: [...device.features].sort() },
      timestamps: { requested: config.timestamps, supported: timestampSupported, enabled: timestampEnabled,
        reason: timestampEnabled ? 'enabled on standalone benchmark device' : config.timestamps === 'off' ? 'not requested' : 'unsupported by adapter',
        precision: 'Implementation-defined, privacy-coarsened nanoseconds; zero deltas are retained, invalid deltas are counted.' },
      viewport: { logicalWidth: config.width, logicalHeight: config.height, pixelWidth: width, pixelHeight: height, dpr: config.dpr, format: 'rgba8unorm', target: config.target },
      fixtureVersion, textureFixtures, frameUniformSha256, initialTextureUploadBytes: textureUploadBytes, residentTextureCount: 5,
      pipelineInitializationWallMs, compilationMessages, wallDurationMs: performance.now() - started,
      workloads,
      comparison: config.baselineShader ? {
        independentVariable: 'Shader source only; shared device, target, textures, buffers, primitive stream, draw order and upload/timestamp settings.',
        order: 'Baseline then candidate on even repeats; candidate then baseline on odd repeats. Each variant receives its own warmup; workload order rotates independently.',
        outputRequirement: 'Exact full RGBA8 byte equality after every paired trial; no pixel tolerance.',
        ratios: 'Baseline divided by candidate for the named percentile. Values above one mean a shorter candidate interval for this fixture and adapter. Zero or unavailable percentiles produce null, never an infinite speedup.',
        baselinePipelineInitializationWallMs: variants.get('baseline').pipelineInitializationWallMs,
        baselineCompilationMessages: variants.get('baseline').compilationMessages,
        baselineWorkloads,
        workloads: config.workloads.map((name, index) => ({ ...comparisonResults.get(name),
          pooledTimings: compareTrialTimings(baselineWorkloads[index], workloads[index]) }))
      } : null,
      uncapturedErrors: progress.uncapturedErrors, deviceLoss: progress.deviceLoss
    };
  } finally {
    destroying = true;
    canvasContext?.unconfigure();
    for (const resource of resources.reverse()) resource.destroy();
    device.destroy();
  }
}
