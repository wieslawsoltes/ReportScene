// Reproducible shader/backend microbenchmark. This is not an Uno application FPS
// benchmark: report traversal, shaping, rasterization and compositor latency are
// outside its measurements. Run --help for hardware and CI smoke examples.
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { arch, cpus, platform, release } from 'node:os';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { workloadDefaults } from './webgpu-benchmark-workloads.mjs';

const directory = fileURLToPath(new URL('.', import.meta.url));
const repository = fileURLToPath(new URL('../../', import.meta.url));
const shaderPath = 'src/ReportScene.Rendering.WebGpu/Shaders/Report2D.wgsl';
const help = `ReportScene production WebGPU shader benchmark

Hardware/default adapter (no software renderer flags are added):
  npm run benchmark:webgpu -- --require-hardware --timestamps auto --headed
  REPORTSCENE_BROWSER_EXECUTABLE=/path/to/chrome npm run benchmark:webgpu -- --timestamps required

Small software CI smoke (validates the runner; not hardware qualification):
  npm run benchmark:webgpu -- --software --workloads shapes,glyphs,images,mixed --instances 128 --width 640 --height 480 --warmup 2 --samples 3 --repeats 1 --timestamps auto --timeout-ms 120000

Options:
  --workloads LIST       Ordered subset of shapes,glyphs,images,mixed (default: all).
  --instances N          Override per-workload primitive counts (1..262144).
                        Defaults: shapes=4096, glyphs=16384, images=512, mixed=8192.
  --width N --height N   Logical viewport DIPs (64..8192; default: 1600 x 1000).
  --dpr N               Pixels per DIP (.5..4; default: 1). At most 16,777,216 pixels.
  --batch-limit N       Maximum adjacent same-texture instances per draw (default: 262144).
                        Splits batches without reordering them.
  --uploads MODE        instances | none (default: instances). Textures are resident.
  --target TARGET       offscreen | canvas (default: offscreen). Neither measures display latency.
  --warmup N            Unrecorded frames per workload per repeat (1..1000; default: 30).
  --samples N           Recorded frames per workload per repeat (3..1000; default: 60).
  --repeats N           Repeats with rotating workload order (1..10; default: 3).
  --seed N              Deterministic unsigned 32-bit fixture seed (default: 104729).
  --timestamps MODE     off | auto | required (default: off). Requested only on our own device.
  --software            Explicit SwiftShader/Vulkan flags for software smoke validation.
  --require-hardware    Fail unless the adapter reports non-fallback hardware.
  --headed              Show the Chromium window (headless by default).
  --executable PATH     Chromium executable (or REPORTSCENE_BROWSER_EXECUTABLE).
  --output PATH         Output directory (default: tests/browser/benchmark-evidence).
  --timeout-ms N        Device-run deadline (1000..600000; default: 180000).
                        Browser startup/navigation have separate 30-second limits.
  --help                Show this help.

results.json contains raw samples, per-repeat and pooled nearest-rank p50/p95,
adapter metadata, hashes and timing definitions. samples.csv retains every measured
frame. GPU timestamps are never inferred from CPU or queue-completion wall time.
Standard timestamp quantization is retained. No pass/fail speed threshold is imposed.
`;

export function parseOptions(args) {
  const strings = ['workloads', 'instances', 'width', 'height', 'dpr', 'batch-limit', 'uploads', 'target',
    'warmup', 'samples', 'repeats', 'seed', 'timestamps', 'executable', 'output', 'timeout-ms'];
  const booleans = ['software', 'require-hardware', 'headed', 'help'];
  const { values, tokens } = parseArgs({ args, strict: true, allowPositionals: false, tokens: true,
    options: Object.fromEntries([...strings.map(name => [name, { type: 'string' }]), ...booleans.map(name => [name, { type: 'boolean' }])]) });
  const supplied = new Set();
  for (const token of tokens) if (token.kind === 'option') {
    if (supplied.has(token.name)) throw new Error('Repeated option: --' + token.name);
    supplied.add(token.name);
  }
  if (values.help) return { help: true };
  const number = (key, fallback, min, max, integer = true) => {
    const result = values[key] === undefined ? fallback : Number(values[key]);
    if (!Number.isFinite(result) || result < min || result > max || (integer && !Number.isInteger(result)))
      throw new Error('--' + key + ' must be ' + (integer ? 'an integer' : 'a finite number') + ' in [' + min + ', ' + max + '].');
    return result;
  };
  const choice = (key, fallback, choices) => {
    const result = values[key] ?? fallback;
    if (!choices.includes(result)) throw new Error('--' + key + ' must be one of ' + choices.join(', ') + '.');
    return result;
  };
  const workloads = (values.workloads ?? Object.keys(workloadDefaults).join(',')).split(',');
  if (!workloads.length || workloads.some(name => !Object.hasOwn(workloadDefaults, name)) || new Set(workloads).size !== workloads.length)
    throw new Error('--workloads must contain distinct names from ' + Object.keys(workloadDefaults).join(',') + '.');
  const width = number('width', 1600, 64, 8192), height = number('height', 1000, 64, 8192), dpr = number('dpr', 1, .5, 4, false);
  const pixelWidth = Math.round(width * dpr), pixelHeight = Math.round(height * dpr);
  if (Math.max(pixelWidth, pixelHeight) > 8192 || pixelWidth * pixelHeight > 16777216)
    throw new Error('The physical viewport is limited to 8192 per axis and 16,777,216 pixels.');
  if (values.software && values['require-hardware']) throw new Error('--software and --require-hardware are mutually exclusive.');
  const config = {
    workloads, instances: values.instances === undefined ? null : number('instances', null, 1, 262144),
    width, height, dpr, batchLimit: number('batch-limit', 262144, 1, 262144),
    warmup: number('warmup', 30, 1, 1000), samples: number('samples', 60, 3, 1000), repeats: number('repeats', 3, 1, 10),
    seed: number('seed', 104729, 0, 4294967295), timestamps: choice('timestamps', 'off', ['off', 'auto', 'required']),
    uploads: choice('uploads', 'instances', ['instances', 'none']), target: choice('target', 'offscreen', ['offscreen', 'canvas']),
    software: values.software ?? false, requireHardware: values['require-hardware'] ?? false, headed: values.headed ?? false,
    executable: values.executable ?? process.env.REPORTSCENE_BROWSER_EXECUTABLE,
    output: resolve(values.output ?? fileURLToPath(new URL('./benchmark-evidence/', import.meta.url))),
    timeoutMs: number('timeout-ms', 180000, 1000, 600000)
  };
  if ((config.warmup + config.samples) * config.repeats * workloads.length > 20000)
    throw new Error('The run is limited to 20,000 total warmup plus measured frames.');
  return config;
}

const timingDefinitions = {
  sampling: 'One submitted frame in flight; await queue.onSubmittedWorkDone() before the next frame. Serialized queue-completion latency, not pipelined throughput or display FPS.',
  cpuPrepareMs: 'performance.now() around optional uniform/instance writeBuffer calls, target acquisition, bind groups/draw encoding and encoder.finish(); excludes fixture generation, allocation and initial texture uploads.',
  cpuSubmitCallMs: 'CPU wall time spent inside queue.submit(); not GPU execution time.',
  queueCompletionWallMs: 'CPU wall time from frame start through queue.onSubmittedWorkDone(); includes CPU preparation/submission, driver scheduling, GPU work, callback delivery and optional query resolve/copy. Excludes query map/readback.',
  postSubmitWaitMs: 'CPU wall time between queue.submit() returning and queue.onSubmittedWorkDone() resolving; not a GPU timer.',
  gpuRenderPassMs: 'Optional timestamp-query end-minus-begin around the single render pass. Excludes writeBuffer transfers, query resolution/copy/readback, CPU encoding and presentation. Null when unavailable or invalid; quantization retained.',
  queryReadbackWallMs: 'Additional untimed-for-completion CPU wall time spent mapping and reading the timestamp result after queue completion.',
  statistics: 'Nearest-rank p50/p95; per-repeat and pooled samples are provided. Warmup, allocation, shader compilation, texture preparation and output sanity readback are excluded from recorded frames.',
  scope: 'Standalone production WGSL and matching primitive/bind/blend contract. Synthetic cached mask/image fixtures; no Uno scene traversal, .NET interop, font shaping/rasterization, image decode, atlas churn, swapchain presentation latency or end-to-end application FPS.',
  comparability: 'For controlled A/B comparisons, name the intended independent variable (for example shader revision or batch limit) and keep other fixture, viewport/DPR, workload, upload, timestamp, adapter, browser and host settings fixed. Source hashes and raw settings are retained for that comparison. Small CI samples establish executability only; no universal speed claim or hardware acceptance threshold.'
};

export async function benchmark(config) {
  const { chromium } = await import('@playwright/test');
  await mkdir(config.output, { recursive: true });
  // These four files belong to the runner. A failed rerun must not retain a stale
  // successful report or samples that might be mistaken for the current result.
  for (const name of ['results.json', 'samples.csv', 'failure.json', 'console.json'])
    await rm(resolve(config.output, name), { force: true });
  const sources = await Promise.all(['webgpu-benchmark.mjs', 'webgpu-benchmark-page.mjs', 'webgpu-benchmark-workloads.mjs', 'ellipse-reference.mjs']
    .map(async name => [name, await readFile(resolve(directory, name), 'utf8')]));
  const shader = await readFile(resolve(repository, shaderPath), 'utf8');
  const hash = content => createHash('sha256').update(content).digest('hex');
  let commit = null, dirty = null;
  try {
    commit = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repository, encoding: 'utf8' }).trim();
    dirty = execFileSync('git', ['status', '--porcelain'], { cwd: repository, encoding: 'utf8' }).trim().length > 0;
  } catch { /* Hashes remain authoritative outside a git checkout. */ }
  const routes = new Map([
    ['/', ['text/html; charset=utf-8', '<!doctype html><title>ReportScene WebGPU benchmark</title>']],
    ['/Report2D.wgsl', ['text/plain; charset=utf-8', shader]],
    ...sources.filter(([name]) => name !== 'webgpu-benchmark.mjs').map(([name, content]) => ['/' + name, ['text/javascript; charset=utf-8', content]])
  ]);
  const server = createServer((request, response) => {
    const route = routes.get(new URL(request.url ?? '/', 'http://localhost').pathname);
    response.writeHead(route ? 200 : 404, { 'content-type': route?.[0] ?? 'text/plain', 'cache-control': 'no-store' });
    response.end(route?.[1] ?? 'Not found');
  });
  const flags = config.software ? [
    '--enable-webgpu', '--enable-unsafe-webgpu', '--ignore-gpu-blocklist',
    '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--use-vulkan=swiftshader', '--enable-features=Vulkan'
  ] : ['--enable-webgpu'];
  const metadata = {
    schemaVersion: 1, startedAt: new Date().toISOString(), configuration: config,
    source: { commit, dirty, shaderPath, shaderSha256: hash(shader), harnessSha256: Object.fromEntries(sources.map(([name, source]) => [name, hash(source)])) },
    host: { platform: platform(), release: release(), architecture: arch(), logicalCpus: cpus().length, cpuModel: cpus()[0]?.model, node: process.version },
    launch: { flags, headless: !config.headed, executable: config.executable ?? 'Playwright Chromium' },
    timingDefinitions
  };
  const messages = [];
  let browser, page, timeout;
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    browser = await chromium.launch({ headless: !config.headed, executablePath: config.executable, args: flags, timeout: 30000 });
    metadata.launch.version = browser.version();
    page = await browser.newPage({ viewport: { width: config.width, height: config.height }, deviceScaleFactor: 1 });
    page.on('console', message => messages.push({ type: message.type(), text: message.text() }));
    page.on('pageerror', error => messages.push({ type: 'pageerror', text: error.message }));
    await page.goto('http://127.0.0.1:' + server.address().port + '/');
    const result = await Promise.race([
      page.evaluate(async options => (await import('/webgpu-benchmark-page.mjs')).runBenchmark(options), config),
      new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('Benchmark deadline exceeded.')), config.timeoutMs); })
    ]);
    if (messages.some(message => message.type === 'pageerror')) throw new Error('The benchmark page emitted a JavaScript error.');
    const report = { ...metadata, status: 'passed', ...result };
    await writeFile(resolve(config.output, 'results.json'), JSON.stringify(report, null, 2) + '\n');
    const csv = ['workload,repeat,sample,cpuPrepareMs,cpuSubmitCallMs,queueCompletionWallMs,postSubmitWaitMs,queryReadbackWallMs,gpuRenderPassMs,timestampDeltaNs,timestampRejected'];
    for (const workload of report.workloads) for (const trial of workload.trials) for (const [index, sample] of trial.samples.entries())
      csv.push([workload.name, trial.repeat, index, sample.cpuPrepareMs, sample.cpuSubmitCallMs,
        sample.queueCompletionWallMs, sample.postSubmitWaitMs, sample.queryReadbackWallMs,
        sample.gpuRenderPassMs ?? '', sample.timestampDeltaNs ?? '', sample.timestampRejection ? 'true' : 'false'].join(','));
    await writeFile(resolve(config.output, 'samples.csv'), csv.join('\n') + '\n');
    console.log('Adapter: ' + report.adapterClassification + ' ' + JSON.stringify(report.adapter));
    console.log('Timings are serialized latency in ms; CPU submit and queue wall time are not GPU execution time.');
    for (const workload of report.workloads) {
      const metrics = workload.statistics, gpu = metrics.gpuRenderPassMs;
      console.log(workload.name + ': instances=' + workload.primitiveCount + ', draws=' + workload.drawCalls +
        ', samples=' + metrics.queueCompletionWallMs.count + ', queue-wall p50/p95=' +
        metrics.queueCompletionWallMs.p50.toFixed(3) + '/' + metrics.queueCompletionWallMs.p95.toFixed(3) +
        ', GPU-pass p50/p95=' + (gpu ? gpu.p50.toFixed(3) + '/' + gpu.p95.toFixed(3) : 'unavailable'));
    }
    console.log('Results: ' + resolve(config.output, 'results.json'));
    return report;
  } catch (error) {
    let progressTimeout;
    const progress = page && !page.isClosed() ? await Promise.race([
      page.evaluate(() => window.__reportSceneBenchmarkProgress ?? null).catch(() => null),
      new Promise(resolve => { progressTimeout = setTimeout(() => resolve(null), 2000); })
    ]) : null;
    clearTimeout(progressTimeout);
    await writeFile(resolve(config.output, 'failure.json'), JSON.stringify({ ...metadata, status: 'failed', error: error.message, stack: error.stack, progress }, null, 2) + '\n');
    throw error;
  } finally {
    clearTimeout(timeout);
    try { await writeFile(resolve(config.output, 'console.json'), JSON.stringify(messages, null, 2) + '\n'); }
    finally {
      try { if (browser) await browser.close(); }
      finally {
        server.closeAllConnections();
        if (server.listening) await new Promise(resolve => server.close(resolve));
      }
    }
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const options = parseOptions(process.argv.slice(2));
    if (options.help) console.log(help);
    else await benchmark(options);
  } catch (error) { console.error(error.stack ?? error.message); process.exitCode = 1; }
}
