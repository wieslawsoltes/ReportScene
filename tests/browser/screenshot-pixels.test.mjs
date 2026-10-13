import test from 'node:test';
import assert from 'node:assert/strict';
import { PNG } from 'pngjs';
import { screenshotPixels } from './screenshot-pixels.mjs';

function image(width, height, pixel) {
  const png = new PNG({ width, height });
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) png.data.set(pixel(x, y), (y * width + x) * 4);
  return PNG.sync.write(png);
}
test('reads exact RGBA coordinates and reuses only the same captured buffer', () => {
  const bytes = image(4, 3, (x, y) => [x * 30, y * 50, x + y, 128]);
  const pixels = screenshotPixels(bytes);
  assert.equal(pixels, screenshotPixels(bytes)); assert.notEqual(pixels, screenshotPixels(Buffer.from(bytes)));
  assert.deepEqual(pixels.pixel(2.9, 1.8), [60, 50, 3, 128]);
  const changed = pixels.pixel(2, 1); changed[0] = 255;
  assert.deepEqual(pixels.pixel(2, 1), [60, 50, 3, 128]);
});
test('blank and sparse two-color splash images fail the retained diversity threshold', () => {
  assert.equal(screenshotPixels(image(1600, 1000, () => [195, 195, 195, 255])).colorCount(), 1);
  const splash = screenshotPixels(image(1600, 1000, (x, y) => x > 700 && x < 900 && y > 480 && y < 520 ? [40, 40, 40, 255] : [255, 255, 255, 255]));
  assert.ok(splash.colorCount() <= 30);
});
test('dense rendered color fixture passes diversity and exact edge checks', () => {
  const pixels = screenshotPixels(image(160, 100, (x, y) => [x, y * 2, (x * 7 + y) % 256, 255]));
  assert.ok(pixels.colorCount() > 30); assert.deepEqual(pixels.pixel(159, 99), [159, 198, 188, 255]);
});
test('rejects out-of-bounds coordinates rather than treating them as transparent', () => {
  const pixels = screenshotPixels(image(2, 2, () => [1, 2, 3, 255]));
  for (const point of [[-1, 0], [2, 0], [0, 2]]) assert.throws(() => pixels.pixel(...point), RangeError);
  assert.throws(() => pixels.pixel(NaN, 0), TypeError);
});
test('validates header dimensions and CRC before trusting captured bytes', () => {
  assert.throws(() => screenshotPixels(Buffer.alloc(33)));
  const bytes = image(2, 2, () => [1, 2, 3, 255]);
  const oversized = Buffer.from(bytes); oversized.writeUInt32BE(20000, 16);
  assert.throws(() => screenshotPixels(oversized), RangeError);
  const corrupt = Buffer.from(bytes); corrupt[29] ^= 1;
  assert.throws(() => screenshotPixels(corrupt));
});
