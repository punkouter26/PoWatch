(() => {
  let activeStream = null;
  let activePreviewElement = null;

  // Cached load state received from the worker — keeps getModelLoadState() synchronous.
  let _cachedLoadState = 'idle';

  // Frame-diff state (DOM access stays on the main thread)
  let _lastFramePixels = null;

  // Model labels for getAvailableModels() — mirrors the worker's _MODELS list.
  const _MODEL_LABELS = {
    'smolvlm-256m': 'SmolVLM 256M',
    'smolvlm-500m': 'SmolVLM 500M',
    'fastvlm-0.5b': 'FastVLM 0.5B',
    'lfm2-vl-450m': 'LFM2-VL 450M',
  };

  // ---------------------------------------------------------------------------
  // Web Worker message bus
  // All heavy computation (model loading, token generation) runs in inference-worker.js
  // off the main thread so the browser UI stays responsive.
  // ---------------------------------------------------------------------------
  let _worker = null;
  let _pendingMessages = new Map(); // msgId -> { resolve, reject }
  let _msgId = 0;

  function getWorker() {
    if (!_worker) {
      _worker = new Worker('/js/inference-worker.js', { type: 'module' });
      _worker.onmessage = (e) => {
        const { id, type, ...rest } = e.data;
        // Unsolicited state broadcasts from the worker (e.g. during model loading)
        if (type === 'STATE_UPDATE') {
          _cachedLoadState = rest.loadState;
          return;
        }
        const pending = _pendingMessages.get(id);
        if (pending) {
          _pendingMessages.delete(id);
          pending.resolve({ type, ...rest });
        }
      };
      _worker.onerror = () => {
        for (const { reject } of _pendingMessages.values()) {
          reject(new Error('Inference worker crashed'));
        }
        _pendingMessages.clear();
        _worker = null; // allow transparent recreation on next request
      };
    }
    return _worker;
  }

  function postToWorker(type, payload) {
    return new Promise((resolve, reject) => {
      const id = _msgId++;
      _pendingMessages.set(id, { resolve, reject });
      getWorker().postMessage({ id, type, payload });
    });
  }

  // ---------------------------------------------------------------------------
  // DOM helpers — must stay on the main thread (Worker cannot access DOM)
  // ---------------------------------------------------------------------------

  async function attachStreamToElement(videoElement) {
    if (!videoElement || !activeStream) return;

    if (videoElement.srcObject !== activeStream) {
      videoElement.srcObject = activeStream;
    }

    videoElement.muted = true;
    videoElement.playsInline = true;
    activePreviewElement = videoElement;

    try {
      await videoElement.play();
    } catch {
      // Ignore autoplay timing issues; the stream remains attached.
    }
  }

  // Returns fraction of pixels that changed significantly vs last frame (0–1).
  // Samples at 160×90 to keep cost negligible.
  function computeFrameDiff(videoElement) {
    if (!videoElement || videoElement.videoWidth === 0 || videoElement.videoHeight === 0) return 1;
    const w = Math.min(160, videoElement.videoWidth);
    const h = Math.min(90,  videoElement.videoHeight);
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d');
    ctx?.drawImage(videoElement, 0, 0, w, h);
    const pixels = ctx.getImageData(0, 0, w, h).data;
    if (!_lastFramePixels || _lastFramePixels.length !== pixels.length) {
      _lastFramePixels = new Uint8ClampedArray(pixels);
      return 1;
    }
    let changed = 0;
    const total = pixels.length / 4;
    for (let i = 0; i < pixels.length; i += 4) {
      const dr = Math.abs(pixels[i]     - _lastFramePixels[i]);
      const dg = Math.abs(pixels[i + 1] - _lastFramePixels[i + 1]);
      const db = Math.abs(pixels[i + 2] - _lastFramePixels[i + 2]);
      if (dr + dg + db > 30) changed++;
    }
    _lastFramePixels = new Uint8ClampedArray(pixels);
    return changed / total;
  }

  async function captureFrame(videoElement) {
    if (!videoElement || videoElement.videoWidth === 0 || videoElement.videoHeight === 0) {
      return '';
    }
    const canvas = document.createElement('canvas');
    canvas.width = videoElement.videoWidth;
    canvas.height = videoElement.videoHeight;
    const context = canvas.getContext('2d');
    context?.drawImage(videoElement, 0, 0, canvas.width, canvas.height);
    return canvas.toDataURL('image/jpeg', 0.85);
  }

  function classifyMotion(diff) {
    if (diff >= 0.18) return 'High';
    if (diff >= 0.06) return 'Medium';
    if (diff >= 0.015) return 'Low';
    return 'Still';
  }

  // ---------------------------------------------------------------------------
  // Public API exposed to Blazor via window.powatchInference
  // ---------------------------------------------------------------------------

  window.powatchInference = {
    async isWebGpuAvailable() {
      const res = await postToWorker('GET_DIAGNOSTICS', {});
      return res.data?.webGpuPresent ?? false;
    },

    async ensureWebcamAccess() {
      if (!navigator?.mediaDevices?.getUserMedia) {
        return { available: false, errorState: 'Webcam unavailable in this browser. Fallback preview active.' };
      }
      try {
        if (!activeStream) {
          activeStream = await navigator.mediaDevices.getUserMedia({
            audio: false,
            video: {
              width: { ideal: 1280 },
              height: { ideal: 720 },
              facingMode: 'user',
            },
          });
        }
        return { available: true, errorState: '' };
      } catch {
        return { available: false, errorState: 'Webcam unavailable in this browser. Fallback preview active.' };
      }
    },

    async startPreview(videoElement) {
      const webcam = await window.powatchInference.ensureWebcamAccess();
      if (!webcam.available) return webcam.errorState;
      await attachStreamToElement(videoElement);
      return 'OK';
    },

    async captureAndInfer(prompt, videoElement, maxInferenceTokens = 96) {
      const webcam = await window.powatchInference.ensureWebcamAccess();
      if (!webcam.available) {
        return {
          isAvailable: false,
          status: webcam.errorState,
          subjectHint: null,
          activity: 'Unavailable',
          clinicalPayload: '',
          isSignificant: false,
          significantReason: null,
          confidenceScore: 0,
          confidenceLabel: 'Unavailable',
          motionScore: 0,
          motionLevel: 'Unavailable',
        };
      }

      await attachStreamToElement(videoElement);

      // Frame-diff: skip inference if the scene hasn't changed enough (saves CPU)
      const diff = computeFrameDiff(videoElement);
      const motionScore = Math.round(diff * 100);
      const motionLevel = classifyMotion(diff);
      if (diff < 0.015) {
        return {
          isAvailable: false,
          status: 'Frame unchanged: skipped',
          subjectHint: null,
          activity: 'No change',
          clinicalPayload: '',
          isSignificant: false,
          significantReason: null,
          confidenceScore: 0,
          confidenceLabel: 'Awaiting AI',
          motionScore,
          motionLevel,
        };
      }

      // Capture frame on main thread (DOM), then hand off to the worker
      const base64Frame = await captureFrame(videoElement);
      const res = await postToWorker('RUN_INFERENCE', {
        base64Frame,
        prompt,
        maxNewTokens: maxInferenceTokens,
      });
      return {
        ...res.result,
        motionScore,
        motionLevel,
        capturedImageDataUrl: base64Frame,
      };
    },

    // Synchronous — returns cached value broadcast by the worker; never blocks.
    getModelLoadState() {
      return _cachedLoadState;
    },

    // Async — queries the worker for the full diagnostics snapshot.
    async getInferenceDiagnostics() {
      const res = await postToWorker('GET_DIAGNOSTICS', {});
      // JS heap usage — Chrome-only (performance.memory is not in the spec).
      // Returns MB used / total, e.g. "42 / 128 MB". Returns null elsewhere.
      let jsHeapMb = null;
      try {
        const mem = performance?.memory;
        if (mem?.usedJSHeapSize) {
          const used  = Math.round(mem.usedJSHeapSize  / 1_048_576);
          const total = Math.round(mem.jsHeapSizeLimit  / 1_048_576);
          jsHeapMb = `${used} / ${total} MB`;
        }
      } catch { /* non-Chrome: silently ignore */ }
      return {
        ...res.data,
        jsHeapMb,
        streamActive: !!activeStream,
        previewWidth: activePreviewElement?.videoWidth ?? 0,
        previewHeight: activePreviewElement?.videoHeight ?? 0,
      };
    },

    setModel(modelKey) {
      _cachedLoadState = 'idle';
      postToWorker('SET_MODEL', { modelKey });
    },

    setPowerPreference(preference) {
      const valid = ['default', 'high-performance', 'low-power'];
      if (!valid.includes(preference)) return;
      _cachedLoadState = 'idle';
      postToWorker('SET_POWER_PREFERENCE', { preference });
    },

    getAvailableModels() {
      return Object.entries(_MODEL_LABELS).map(([key, label]) => ({ key, label }));
    },

    stopMonitor() {
      if (activePreviewElement) {
        activePreviewElement.pause();
        activePreviewElement.srcObject = null;
        activePreviewElement = null;
      }

      if (activeStream) {
        for (const track of activeStream.getTracks()) {
          track.stop();
        }
        activeStream = null;
      }
    },
  };
})();
