import { PNG } from 'pngjs';

// Decode captured PNG bytes in the test process, never in the application's
// renderer. Cache only while the caller retains that exact immutable Buffer.
const decoded = new WeakMap();
const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
export function screenshotPixels(bytes) {
  if (!Buffer.isBuffer(bytes)) throw new TypeError('Expected the original screenshot PNG Buffer.');
  const cached = decoded.get(bytes); if (cached) return cached;
  if (bytes.length < 33 || bytes.length > 100_000_000 || !bytes.subarray(0, 8).equals(signature)
      || bytes.toString('ascii', 12, 16) !== 'IHDR') throw new Error('Invalid screenshot PNG header.');
  const width = bytes.readUInt32BE(16), height = bytes.readUInt32BE(20);
  if (!width || !height || width > 16384 || height > 16384 || width * height > 64_000_000)
    throw new RangeError(`Screenshot dimensions exceed the inspection budget: ${width}x${height}.`);
  const image = PNG.sync.read(bytes, { checkCRC: true });
  if (image.width !== width || image.height !== height || image.data.length !== width * height * 4)
    throw new Error('Unexpected screenshot pixel layout.');
  const result = Object.freeze({ width, height,
    pixel(x, y) {
      if (!Number.isFinite(x) || !Number.isFinite(y)) throw new TypeError('Pixel coordinates must be finite.');
      x = Math.floor(x); y = Math.floor(y);
      if (x < 0 || y < 0 || x >= width || y >= height) throw new RangeError(`Pixel (${x},${y}) is outside ${width}x${height}.`);
      const n = (y * width + x) * 4;
      return Array.from(image.data.subarray(n, n + 4));
    },
    // Same 160x100 / five-bit RGB diversity guard and >30 threshold as before.
    // Bilinear sample centers are deterministic, without a browser Image/Canvas
    // roundtrip. Exact-color regressions use pixel(), without interpolation.
    colorCount() {
      const colors = new Set();
      for (let y = 0; y < 100; y++) {
        const sy = Math.max(0, Math.min(height - 1, (y + .5) * height / 100 - .5));
        const y0 = Math.floor(sy), y1 = Math.min(height - 1, y0 + 1), fy = sy - y0;
        for (let x = 0; x < 160; x++) {
          const sx = Math.max(0, Math.min(width - 1, (x + .5) * width / 160 - .5));
          const x0 = Math.floor(sx), x1 = Math.min(width - 1, x0 + 1), fx = sx - x0;
          const a = (y0 * width + x0) * 4, b = (y0 * width + x1) * 4;
          const c = (y1 * width + x0) * 4, d = (y1 * width + x1) * 4;
          let key = 0;
          for (let channel = 0; channel < 3; channel++) {
            const upper = image.data[a + channel] * (1 - fx) + image.data[b + channel] * fx;
            const lower = image.data[c + channel] * (1 - fx) + image.data[d + channel] * fx;
            key = (key << 5) | (Math.round(upper * (1 - fy) + lower * fy) >> 3);
          }
          colors.add(key);
        }
      }
      return colors.size;
    }
  });
  decoded.set(bytes, result); return result;
}
