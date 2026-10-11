import fs from 'node:fs/promises';
const report = JSON.parse(await fs.readFile('merged-results.json', 'utf8'));
const tests = [];
function visit(suite) { for (const spec of suite.specs || []) tests.push(...spec.tests.map(test => ({ ...test, title: spec.title }))); for (const child of suite.suites || []) visit(child); }
for (const suite of report.suites || []) visit(suite);
const bad = tests.filter(t => t.expectedStatus !== 'passed' || t.results.length !== 1 || t.results[0].status !== 'passed');
if (tests.length === 0 || bad.length || report.errors?.length || report.stats.unexpected || report.stats.flaky || report.stats.skipped)
  throw new Error(`Merged browser validation failed: ${tests.length} tests, ${bad.length} rejected, stats=${JSON.stringify(report.stats)}`);
if (!tests.some(t => t.title.includes('actual .NET WebAssembly conformance suite')))
  throw new Error('The actual WebAssembly conformance scenario is missing from merged evidence.');
if (!tests.some(t => t.title.includes('delayed save preserves newer edits')))
  throw new Error('Delayed workspace-host integration coverage is missing.');
console.log(`MERGED BROWSER: ${tests.length}/${tests.length} passed; zero retries, skips or flaky results.`);
