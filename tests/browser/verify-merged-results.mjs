import fs from 'node:fs/promises';
const report = JSON.parse(await fs.readFile('merged-results.json', 'utf8'));
const tests = [];
function visit(suite) { for (const spec of suite.specs || []) tests.push(...spec.tests.map(test => ({ ...test, title: spec.title }))); for (const child of suite.suites || []) visit(child); }
for (const suite of report.suites || []) visit(suite);
const bad = tests.filter(t => t.expectedStatus !== 'passed' || t.results.length !== 1 || t.results[0].status !== 'passed');
if (tests.length < 57 || bad.length || report.errors?.length || report.stats.unexpected || report.stats.flaky || report.stats.skipped)
  throw new Error(`Merged browser validation failed: ${tests.length} tests, ${bad.length} rejected, stats=${JSON.stringify(report.stats)}`);
for (const required of [
  'native export dialog validates range drafts', 'native export custom and current page selections',
  'native PNG resolution controls', 'data-only CSV disables page controls', 'oversized PNG export keeps the native draft',
  'actual .NET WebAssembly conformance suite', 'delayed save preserves newer edits',
  'compact native tool windows resize', 'report explorer filters nested paths',
  'formatting controls track single and mixed selections', 'property filtering, categories, validation',
  'all eight visible resize handles', 'selection saving and zoom reuse authoring layout',
  'page property selection edits paper', 'ordinary multi-selection clicks select one object',
  'authoring text wrapping is cached', 'independent explorer tabs retain search',
  'native report-tree keyboard navigation', 'first selected object resize handles',
  'save and page navigation reuse outline metadata', 'designer options draft cancel defaults',
  'designer preferences apply atomically', 'viewport constraints and cancelled pane drags',
  'invalid stored preferences keep the designer usable', 'preference storage failure stays pending',
  'direct view snap sorting and pane commands persist', 'preview first last and strict page entry',
  'preview keyboard page navigation', 'whole-page fitting uses both viewport axes',
  'preview viewport culling follows real scroll', 'preview outline preserves duplicate bookmark occurrences',
  'bookmark and search navigation reveal offscreen targets', 'replacing a prepared report clears old bookmark targets',
  'authoring viewport paints distant content', 'authoring culling retains a thick border',
  'prepared checkbox stroke stays painted'
]) if (!tests.some(t => t.title.includes(required))) throw new Error(`Required browser scenario missing: ${required}`);
console.log(`MERGED BROWSER: ${tests.length}/${tests.length} passed; zero retries, skips or flaky results.`);
