import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { ready, state, fresh, click, artifact } from './uno-driver.mjs';

function fixture(name = 'Host operation fixture') {
  return { name, pages: [{ name: 'Main', bands: [{ kind: 'ReportTitle', height: 140, items: [
    { $type: 'text', id: 'a', name: 'First', text: 'Alpha', bounds: { x: 0, y: 0, width: 200, height: 30 } },
    { $type: 'text', id: 'b', name: 'Second', text: 'Beta', bounds: { x: 0, y: 60, width: 200, height: 30 } }
  ] }] }, { name: 'Other', bands: [{ kind: 'ReportTitle', height: 100 }] }] };
}
async function shortcut(page, key) { await click(page, 'tool-select'); await page.keyboard.press(key); await fresh(page); }
async function openFile(page, document) {
  const chooser = page.waitForEvent('filechooser'); await shortcut(page, 'Control+o');
  await (await chooser).setFiles({ name: 'fixture.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(document)) });
}
async function start(page) {
  await ready(page); await openFile(page, fixture());
  await expect.poll(async () => (await state(page)).Document).toBe('Host operation fixture'); await fresh(page);
}
async function select(page, id) {
  const o = (await state(page)).Objects.find(o => o.Id === id); expect(o?.Screen).toBeTruthy();
  const r = o.Screen; await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === id)?.Selected).toBe(true); await fresh(page);
}
async function changeText(page, value) {
  await expect.poll(async () => Boolean((await state(page)).Properties.text)).toBe(true);
  const r = (await state(page)).Properties.text; await page.mouse.click(r.X + 25, r.Y + r.Height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.insertText(value); await page.keyboard.press('Tab');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a')?.Text).toBe(value);
}
// Delay ONLY a host I/O acknowledgement, after actual file/clipboard/download work starts.
// No application commands, model edits, diagnostic values or rendered DOM are synthesized.
async function holdHost(page, method, outcome = null) {
  await page.evaluate(({ method, outcome }) => {
    const host = window.reportSceneHost;
    if (window.__heldHost) throw new Error('One host gate per fresh browser context.');
    const gate = window.__heldHost = { id: null, release: false, delivered: false, outcome };
    const original = host[method]; const take = host.take;
    host[method] = function (...args) { if (gate.id === null) gate.id = args[0]; return original.apply(this, args); };
    host.take = function (id) {
      if (id !== gate.id || gate.delivered) return take.call(this, id);
      if (!gate.release) return '';
      const real = take.call(this, id); if (!real) return '';
      gate.delivered = true; return gate.outcome ? JSON.stringify(gate.outcome) : real;
    };
  }, { method, outcome });
}
async function release(page) {
  await expect.poll(() => page.evaluate(() => Boolean(window.__heldHost?.id))).toBe(true);
  await page.evaluate(() => { window.__heldHost.release = true; });
  await expect.poll(() => page.evaluate(() => window.__heldHost.delivered)).toBe(true); await fresh(page);
}

test('a delayed save preserves newer edits, and undo returns to the written revision', async ({ page }, info) => {
  await start(page); await select(page, 'a'); await changeText(page, 'Written snapshot');
  await holdHost(page, 'saveFile');
  const download = page.waitForEvent('download'); await click(page, 'quick-save'); const file = await download;
  const written = JSON.parse(await fs.readFile(await file.path(), 'utf8'));
  expect(written.pages[0].bands[0].items[0].text).toBe('Written snapshot');
  await changeText(page, 'Newer edit'); await release(page);
  await expect.poll(async () => (await state(page)).Status).toContain('remain unsaved'); expect((await state(page)).Dirty).toBe(true);
  await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).Dirty).toBe(false);
  expect((await state(page)).Objects.find(o => o.Id === 'a').Text).toBe('Written snapshot');
  await artifact(info, 'delayed-save.json', await state(page));
});

test('selection changes during real clipboard cut cannot delete the replacement selection', async ({ page }, info) => {
  await start(page); await select(page, 'a'); await holdHost(page, 'copy'); await click(page, 'cut');
  await select(page, 'b'); await release(page);
  await expect.poll(async () => (await state(page)).Status).toContain('selection changed');
  expect((await state(page)).ObjectCount).toBe(2); expect((await state(page)).Dirty).toBe(false);
  await artifact(info, 'delayed-cut.json', await state(page));
});

test('paste rejects a new target instead of inserting into a newly selected page', async ({ page }, info) => {
  await start(page); await select(page, 'a'); await click(page, 'copy');
  await expect.poll(async () => (await state(page)).Status).toContain('Copied 1');
  await holdHost(page, 'paste'); await click(page, 'paste'); await click(page, 'page-tab-1'); await release(page);
  await expect.poll(async () => (await state(page)).Status).toContain('selection changed');
  expect((await state(page)).Objects).toHaveLength(0); expect((await state(page)).ObjectCount).toBe(2);
  await artifact(info, 'delayed-paste.json', await state(page));
});

test('file-picker completion cannot replace changes made while its result is pending', async ({ page }, info) => {
  await start(page); await select(page, 'a'); await holdHost(page, 'pick');
  await openFile(page, fixture('Late replacement')); await changeText(page, 'Keep this edit'); await release(page);
  await expect.poll(async () => (await state(page)).Status).toContain('report changed');
  expect((await state(page)).Document).toBe('Host operation fixture');
  expect((await state(page)).Objects.find(o => o.Id === 'a').Text).toBe('Keep this edit');
  await artifact(info, 'delayed-open.json', await state(page));
});

test('save-before-new refuses to discard newer edits made during the save acknowledgement', async ({ page }, info) => {
  await start(page); await select(page, 'a'); await changeText(page, 'Before confirmation');
  await holdHost(page, 'saveFile'); await shortcut(page, 'Control+n');
  await click(page, 'save-confirm-save'); await changeText(page, 'After confirmation'); await release(page);
  await expect.poll(async () => (await state(page)).Status).toContain('remain unsaved');
  expect((await state(page)).Document).toBe('Host operation fixture'); expect((await state(page)).Dirty).toBe(true);
  await artifact(info, 'save-before-new.json', await state(page));
});

test('a host cancellation during save-before-new retains the current dirty document', async ({ page }, info) => {
  await start(page); await select(page, 'a'); await changeText(page, 'Unsaved content');
  await holdHost(page, 'saveFile', { cancelled: true }); await shortcut(page, 'Control+n'); await click(page, 'save-confirm-save');
  await release(page); await expect.poll(async () => (await state(page)).Status).toContain('Save cancelled');
  expect((await state(page)).Document).toBe('Host operation fixture'); expect((await state(page)).Dirty).toBe(true);
  await artifact(info, 'save-cancellation.json', await state(page));
});
