// L1 detector worker: runs an object detector on frames posted from the page.
//
// Model: RF-DETR Nano (Apache-2.0, COCO classes), falling back to RT-DETRv2 R18 (Apache-2.0).
// Both architectures ship in the vendored transformers.js 3.8.1, loaded from our own origin like
// the VLM worker. WebGPU is tried first, then the WASM backend. Boxes come back normalised to
// [0, 1] so the page and the tracker never care about the capture resolution.

const TRANSFORMERS_BASE = new URL('../../lib/transformers-3.8.1/', import.meta.url);
const TRANSFORMERS_URL = new URL('transformers.min.js', TRANSFORMERS_BASE).href;

const MODELS = [
  { id: 'onnx-community/rfdetr_nano-ONNX', dtype: 'fp32' },
  { id: 'onnx-community/rtdetr_v2_r18vd-ONNX', dtype: 'fp32' },
];

let detector = null;
let RawImage = null;
let loaded = null;       // { modelId, device, loadMs }
let loading = null;
let canvas = null;

async function load(forceDevice) {
  if (loaded) return loaded;
  if (loading) return loading;

  loading = (async () => {
    const started = performance.now();
    const transformers = await import(TRANSFORMERS_URL);
    RawImage = transformers.RawImage;
    transformers.env.useFSCache = false;
    transformers.env.backends.onnx.wasm.wasmPaths = TRANSFORMERS_BASE.href;

    const devices = forceDevice ? [forceDevice]
      : typeof navigator !== 'undefined' && navigator.gpu ? ['webgpu', 'wasm'] : ['wasm'];
    const errors = [];
    for (const model of MODELS) {
      for (const device of devices) {
        try {
          const candidate = await transformers.pipeline('object-detection', model.id, { device, dtype: model.dtype });
          if (!(await producesSaneBoxes(candidate))) {
            errors.push(`${model.id}@${device}: calibration frame produced out-of-frame boxes`);
            await candidate.dispose?.();
            continue;
          }
          detector = candidate;
          loaded = { modelId: model.id, device, loadMs: Math.round(performance.now() - started), rejected: errors };
          return loaded;
        } catch (err) {
          errors.push(`${model.id}@${device}: ${err?.message ?? err}`);
        }
      }
    }
    throw new Error(`No detector could be loaded. ${errors.join(' | ')}`);
  })();

  try {
    return await loading;
  } finally {
    loading = null;
  }
}

// Some WebGPU stacks (seen on Chromium's software GPU) run the model but return garbage: boxes many
// frame-widths wide with near-random labels. A plain grey frame must never yield a box outside the
// frame, so one calibration pass catches that and the loader moves on to the next backend.
async function producesSaneBoxes(candidate) {
  const width = 320, height = 240;
  const grey = new RawImage(new Uint8ClampedArray(width * height * 3).fill(128), width, height, 3);
  const output = await candidate(grey, { threshold: 0.05, percentage: true });
  const inside = (v) => v >= -0.1 && v <= 1.1;
  return output.every((d) => inside(d.box.xmin) && inside(d.box.ymin) && inside(d.box.xmax) && inside(d.box.ymax));
}

async function detect(bitmap, threshold) {
  await load();
  if (!canvas || canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
    canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
  }
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  ctx.drawImage(bitmap, 0, 0);
  bitmap.close();
  const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height);
  // The processors expect three channels; canvas pixels are RGBA.
  const image = new RawImage(pixels.data, canvas.width, canvas.height, 4).rgb();

  const started = performance.now();
  const output = await detector(image, { threshold, percentage: true });
  return {
    latencyMs: Math.round(performance.now() - started),
    detections: output.map((d) => ({
      label: d.label,
      score: d.score,
      x0: d.box.xmin, y0: d.box.ymin, x1: d.box.xmax, y1: d.box.ymax,
    })),
  };
}

self.onmessage = async (event) => {
  const { id, type } = event.data ?? {};
  try {
    if (type === 'LOAD') {
      self.postMessage({ id, type: 'LOADED', ...(await load(event.data.device)) });
    } else if (type === 'DETECT') {
      self.postMessage({ id, type: 'DETECTED', ...(await detect(event.data.bitmap, event.data.threshold ?? 0.5)) });
    }
  } catch (err) {
    self.postMessage({ id, type: 'ERROR', message: String(err?.message ?? err) });
  }
};
