import { expect } from '@playwright/test';
import { state, fresh, bounds, click, fill } from './uno-driver.mjs';

export function designerFixture() {
  return { name: 'Designer fidelity contract',
    data: [{ name: 'Sales', columns: ['Name', 'RowNumber', 'Value'], rows: [{ Name: 'One', RowNumber: 17, Value: 42 }] }],
    pages: [
      { name: 'Main', bands: [{ id: 'main-band', name: 'MainTitle', kind: 'ReportTitle', height: 240,
        items: [
          { $type: 'text', id: 'a', name: 'Heading', text: 'Designer heading', bounds: { x: 40, y: 40, width: 200, height: 60 }, style: { fontFamily: 'Arial', fontSize: 14, bold: true } },
          { $type: 'text', id: 'b', name: 'Detail', text: 'Detail text', bounds: { x: 290, y: 120, width: 140, height: 60 }, style: { fontFamily: 'Courier New', fontSize: 9, italic: true } }
        ], children: [{ id: 'child-band', name: 'NestedBand', kind: 'Child', height: 40, items: [
          { $type: 'text', id: 'c', name: 'NestedNote', text: 'Nested note', bounds: { x: 20, y: 5, width: 220, height: 22 } }
        ] }] }] },
      { name: 'Second', bands: [{ id: 'other-band', name: 'OtherTitle', kind: 'ReportTitle', height: 80, items: [
        { $type: 'text', id: 'd', name: 'SecondNote', text: 'Second page', bounds: { x: 20, y: 20, width: 200, height: 30 } }
      ] }] }
    ] };
}
export async function loadDesignerFixture(page) {
  await click(page, 'tab-report'); const r = await bounds(page, 'open'); const chooser = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await chooser).setFiles({ name: 'designer.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(designerFixture())) });
  await expect.poll(async () => (await state(page)).Document).toBe('Designer fidelity contract');
  await click(page, 'tab-home'); await fresh(page);
}
export async function selectObject(page, id, additive = false) {
  const item = (await state(page)).Objects.find(o => o.Id === id); expect(item?.Screen).toBeTruthy();
  if (additive) await page.keyboard.down('Control');
  try { await page.mouse.click(item.Screen.X + item.Screen.Width / 2, item.Screen.Y + item.Screen.Height / 2); }
  finally { if (additive) await page.keyboard.up('Control'); }
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === id)?.Selected).toBe(true);
  await expect.poll(async () => (await state(page)).ActiveDrag).toBe(false); await fresh(page);
}
export async function propertyBounds(page, id) {
  await expect.poll(async () => Boolean((await state(page)).Properties[id]), { message: `Native property ${id}` }).toBe(true);
  return (await state(page)).Properties[id];
}
export async function typeProperty(page, id, text, key = 'Enter') {
  const r = await propertyBounds(page, id); await page.mouse.click(r.X + Math.min(20, r.Width / 2), r.Y + r.Height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.insertText(text);
  await expect.poll(async () => (await state(page)).Inputs[`property:${id}`]).toBe(text);
  if (key) await page.keyboard.press(key); await fresh(page);
}
export async function filterProperties(page, text) { await fill(page, 'property-filter', text); }
