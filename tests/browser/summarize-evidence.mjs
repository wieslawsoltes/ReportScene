import fs from 'node:fs/promises';
import path from 'node:path';

// Report results already produced by the real WebAssembly tests. No application commands,
// fallback results, retries or status overrides are introduced by this summary.
export default async function summarize(config) {
  for (const project of config.projects) {
    async function visit(directory) {
      let entries; try { entries = await fs.readdir(directory, { withFileTypes: true }); } catch (e) { if (e.code === 'ENOENT') return []; throw e; }
      const result = [];
      for (const entry of entries) {
        const file = path.join(directory, entry.name);
        if (entry.isDirectory()) result.push(...await visit(file));
        else if (entry.name === 'wasm-conformance.json') result.push(file);
      }
      return result;
    }
    for (const file of await visit(project.outputDir)) {
      const result = JSON.parse(await fs.readFile(file, 'utf8'));
      if (!Array.isArray(result.tests)) throw new Error(`Missing WebAssembly results in ${file}`);
      const failed = result.tests.filter(t => !t.Passed);
      const summary = { platform: 'actual .NET WebAssembly', total: result.tests.length, passed: result.tests.length - failed.length, failed: failed.map(t => t.Name), fatal: result.fatal ?? null };
      await fs.writeFile(path.join(project.outputDir, 'wasm-summary.json'), JSON.stringify(summary, null, 2));
      console.log(`WASM CONFORMANCE: ${summary.passed}/${summary.total} passed; fatal=${summary.fatal ?? 'none'}`);
      if (failed.length || result.fatal) throw new Error('WebAssembly conformance failed; see the original test evidence.');
    }
  }
}
