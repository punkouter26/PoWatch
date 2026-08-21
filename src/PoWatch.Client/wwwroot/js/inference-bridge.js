(() => {
  let activeStream = null;
  let activePreviewElement = null;

  // Cached load state received from the worker — keeps getModelLoadState() synchronous.
  let _cachedLoadState = 'idle';

  // Frame-diff state (DOM access stays on the main thread)
  let _lastFramePixels = null;

  // The model list is owned by the shared /model-registry.json (rule 1.5): the worker reads it for
  // inference config and the C# UI reads it for the picker. The bridge no longer keeps its own copy.

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
      _worker = new Worker('/js/inference-worker.js?v=20260820-model-selftest', { type: 'module' });
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

  // timeoutMs = 0 means wait forever (RUN_INFERENCE/model loads can be slow). Quick status
  // queries MUST pass a timeout: a worker that never responds otherwise hangs the awaiting
  // Blazor OnInitializedAsync and the page never renders (System page bug, 2026-07-22).
  function postToWorker(type, payload, timeoutMs = 0) {
    return new Promise((resolve, reject) => {
      const id = _msgId++;
      let timer = null;
      if (timeoutMs > 0) {
        timer = setTimeout(() => {
          if (_pendingMessages.delete(id)) {
            reject(new Error(`Inference worker did not answer ${type} within ${timeoutMs}ms`));
          }
        }, timeoutMs);
      }
      _pendingMessages.set(id, {
        resolve: (v) => { if (timer) clearTimeout(timer); resolve(v); },
        reject: (e) => { if (timer) clearTimeout(timer); reject(e); },
      });
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

  // Longest edge sent to the model. The frame used to be captured at full webcam resolution
  // (often 1280x720+), and SmolVLM splits a large image into many patches — a single frame was
  // costing ~900 image tokens, which dominates inference time on the CPU/wasm backend. The model's
  // processor downsamples anyway, so the extra pixels bought nothing but latency.
  const _MAX_CAPTURE_EDGE = 512;

  async function captureFrame(videoElement) {
    if (!videoElement || videoElement.videoWidth === 0 || videoElement.videoHeight === 0) {
      return '';
    }

    const srcW = videoElement.videoWidth;
    const srcH = videoElement.videoHeight;
    const scale = Math.min(1, _MAX_CAPTURE_EDGE / Math.max(srcW, srcH));

    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(srcW * scale));
    canvas.height = Math.max(1, Math.round(srcH * scale));
    const context = canvas.getContext('2d');
    context?.drawImage(videoElement, 0, 0, canvas.width, canvas.height);
    return canvas.toDataURL('image/jpeg', 0.85);
  }

  // ---------------------------------------------------------------------------
  // Model self-test fixture (System page)
  // ---------------------------------------------------------------------------

  // A fixed, synthetic room scene drawn on the main thread, where canvas lives. It is used instead
  // of the webcam on purpose: every model then sees byte-identical input, so two runs are
  // comparable, and the System page never has to raise a camera-permission prompt on a page that
  // shows no preview. The literal colours here are a test fixture, not themed UI — they must stay
  // constant across light and dark so the comparison holds.
  const _TEST_FRAME_EDGE = 384;

  function buildTestFrame() {
    const canvas = document.createElement('canvas');
    canvas.width = _TEST_FRAME_EDGE;
    canvas.height = _TEST_FRAME_EDGE;
    const ctx = canvas.getContext('2d');
    if (!ctx) return '';

    ctx.fillStyle = '#d8d4cc';                       // wall
    ctx.fillRect(0, 0, 384, 384);
    ctx.fillStyle = '#8d7f6d';                       // floor
    ctx.fillRect(0, 250, 384, 134);
    ctx.fillStyle = '#bcd6e8';                       // window
    ctx.fillRect(250, 40, 100, 90);
    ctx.strokeStyle = '#5b5348';
    ctx.lineWidth = 4;
    ctx.strokeRect(250, 40, 100, 90);
    ctx.fillStyle = '#e9e6e0';                       // bed
    ctx.fillRect(30, 200, 210, 90);
    ctx.fillStyle = '#7a8ea0';
    ctx.fillRect(30, 235, 210, 55);
    ctx.fillStyle = '#c98b6a';                       // person: head
    ctx.beginPath();
    ctx.arc(75, 185, 26, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = '#3f5c78';                       // person: torso on the bed
    ctx.fillRect(95, 205, 120, 34);

    return canvas.toDataURL('image/jpeg', 0.9);
  }

  // Kept close to the real observation prompt so the reply the test shows is the kind of reply the
  // observation loop would get. It is not the same constant — the real one lives in C# with the
  // reasoning for its wording, and a shared copy would drift silently.
  const _TEST_PROMPT =
    'What is the person in this image doing? Answer with one short sentence describing only what you can see.';

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
      const res = await postToWorker('GET_DIAGNOSTICS', {}, 5000);
      return res.data?.webGpuPresent ?? false;
    },

    // Fix: C# used to pass its CancellationToken across the JSInterop boundary, which made
    // System.Text.Json walk into CancellationToken.WaitHandle.Handle (IntPtr) and throw
    // SerializeTypeInstanceNotSupported. Now C# calls cancelInFlight() and the bridge keeps
    // an AbortController scoped to the in-flight captureAndInfer call.
    _abortController: null,
    cancelInFlight() {
      try { window.powatchInference._abortController?.abort(); }
      catch { /* nothing to cancel */ }
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
      // Abort any in-flight capture (defensive — C# also calls cancelInFlight before issuing a
      // new one when stopping monitoring). One controller per call, not shared, so a new
      // inference cycle isn't poisoned by a stale abort from a previous run.
      if (window.powatchInference._abortController) {
        try { window.powatchInference._abortController.abort(); } catch { /* */ }
      }
      const ctrl = new AbortController();
      window.powatchInference._abortController = ctrl;

      const webcam = await window.powatchInference.ensureWebcamAccess();
      if (ctrl.signal.aborted) {
        return { isAvailable: false, status: 'Cancelled', activity: 'Cancelled', confidenceLabel: 'Cancelled' };
      }
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
      if (ctrl.signal.aborted) {
        return { isAvailable: false, status: 'Cancelled', activity: 'Cancelled', confidenceLabel: 'Cancelled' };
      }
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
      const res = await postToWorker('GET_DIAGNOSTICS', {}, 5000);
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

    // Per-model self-test for the System page. No timeout is passed: a first-time load of the 2.2B
    // model downloads well over a gigabyte, and a timeout here would report a slow-but-working
    // laptop as a failure. The card disables its buttons for the duration instead.
    async runModelTest(modelKey) {
      const testFrameDataUrl = buildTestFrame();
      if (!testFrameDataUrl) {
        return { modelKey, ok: false, stage: 'fixture', error: 'Could not draw the test image in this browser' };
      }
      const res = await postToWorker('MODEL_TEST', {
        modelKey,
        base64Frame: testFrameDataUrl,
        prompt: _TEST_PROMPT,
        maxNewTokens: 48,
      });
      // The worker owns the model state and has just unloaded whatever it tested, so the cached
      // state must follow it back to 'idle' — otherwise the Live Room reads a stale 'ready'.
      _cachedLoadState = 'idle';
      return { ...res.result, testFrameDataUrl };
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
