// Page side of the L1 detector: owns the detector worker, grabs frames from the <video> on a
// worker-driven clock (about 1 Hz), and hands normalised boxes to .NET. A frame is skipped while
// the previous one is still being detected, so a slow device lowers the rate instead of queueing.
(function () {
  'use strict';

  const WORKER_URL = new URL('./detector-worker.js', document.currentScript?.src ?? location.href);
  const CLOCK_URL = new URL('./sample-clock.js', document.currentScript?.src ?? location.href);
  const CAPTURE_WIDTH = 640;

  let _worker = null;
  let _clock = null;
  let _busy = false;
  let _nextId = 1;
  const _pending = new Map();

  function worker() {
    if (!_worker) {
      _worker = new Worker(WORKER_URL, { type: 'module' });
      _worker.onmessage = (event) => {
        const { id } = event.data;
        const entry = _pending.get(id);
        if (!entry) return;
        _pending.delete(id);
        if (event.data.type === 'ERROR') entry.reject(new Error(event.data.message));
        else entry.resolve(event.data);
      };
    }
    return _worker;
  }

  function call(message, transfer) {
    const id = _nextId++;
    return new Promise((resolve, reject) => {
      _pending.set(id, { resolve, reject });
      worker().postMessage({ ...message, id }, transfer ?? []);
    });
  }

  /** Loads the detector; resolves to { modelId, device, loadMs }. `device` forces 'webgpu' or 'wasm'. */
  function load(device) {
    return call({ type: 'LOAD', device });
  }

  /** Detects on the current frame; resolves to { latencyMs, detections: [{ label, score, x0, y0, x1, y1 }] }. */
  async function detect(video, threshold) {
    if (!video || video.readyState < 2 || !video.videoWidth) return null;
    const height = Math.round(CAPTURE_WIDTH * video.videoHeight / video.videoWidth);
    const bitmap = await createImageBitmap(video, { resizeWidth: CAPTURE_WIDTH, resizeHeight: height });
    return call({ type: 'DETECT', bitmap, threshold }, [bitmap]);
  }

  async function start(video, dotnetRef, intervalMs, threshold) {
    stop();
    _clock = new Worker(CLOCK_URL, { type: 'module' });
    _clock.onmessage = async () => {
      if (_busy) return;
      _busy = true;
      try {
        const result = await detect(video, threshold ?? 0.5);
        if (result) {
          await dotnetRef.invokeMethodAsync('OnDetections', {
            atUtc: new Date().toISOString(),
            latencyMs: result.latencyMs,
            detections: result.detections,
          });
        }
      } catch (err) {
        dotnetRef.invokeMethodAsync('OnDetectorError', String(err?.message ?? err)).catch(() => { });
      } finally {
        _busy = false;
      }
    };
    _clock.postMessage({ intervalMs: intervalMs || 1000 });
  }

  function stop() {
    if (_clock) { _clock.terminate(); _clock = null; }
    _busy = false;
  }

  window.powatchDetector = { load, detect, start, stop };
})();
