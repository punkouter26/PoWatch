// inference-worker.js
// Runs AI model loading and token generation in a dedicated Web Worker,
// keeping the browser's main thread free so the UI stays responsive.
// The main thread (inference-bridge.js) handles all DOM access (video/canvas).

const _CDN_URL = 'https://cdn.jsdelivr.net/npm/@huggingface/transformers@3/dist/transformers.min.js';

const _MODELS = {
  'smolvlm-256m': {
    id: 'HuggingFaceTB/SmolVLM-256M-Instruct',
    label: 'SmolVLM 256M',
    webgpuDtype: 'fp16',
    webgpuDtypeFallback: 'fp32',
    wasmDtype: 'q8',
  },
  'smolvlm-500m': {
    id: 'HuggingFaceTB/SmolVLM-500M-Instruct',
    label: 'SmolVLM 500M',
    webgpuDtype: 'fp16',
    webgpuDtypeFallback: 'fp32',
    wasmDtype: 'q8',
  },

};

// Model state
let _processor = null;
let _model = null;
let _RawImage = null;
let _loadState = 'idle'; // 'idle' | 'loading' | 'ready' | 'error'
let _loadError = null;
let _loadPromise = null;
let _activeModelKey = 'smolvlm-256m';

// Inference diagnostics
let _device = null;
let _dtype = null;
let _fp16FallbackUsed = false;
let _loadStartMs = null;
let _loadEndMs = null;
let _inferenceCount = 0;
let _lastInferenceMs = null;
let _lastInferenceTimestamp = null;
let _lastInferenceOutput = null;

// Cached single WebGPU adapter probe — avoids multiple requestAdapter() calls.
let _webGpuProbePromise = null;
let _gpuAdapterVendor = null;
let _gpuAdapterName = null;
let _gpuPowerPreference = 'default'; // 'default' | 'high-performance' | 'low-power'
function probeWebGpu() {
  if (!_webGpuProbePromise) {
    _webGpuProbePromise = (async () => {
      if (typeof navigator === 'undefined' || !navigator.gpu) return false;
      try {
        const adapterOpts = _gpuPowerPreference !== 'default' ? { powerPreference: _gpuPowerPreference } : undefined;
        const adapter = await navigator.gpu.requestAdapter(adapterOpts);
        if (adapter === null) return false;
        // requestAdapterInfo() is the standard API (Chrome 113+).
        // Fall back to the deprecated adapter.name when unavailable.
        try {
          const info = await adapter.requestAdapterInfo();
          _gpuAdapterVendor = info.vendor ?? '';
          _gpuAdapterName = info.description || info.device || info.architecture || 'GPU';
        } catch {
          _gpuAdapterName = adapter.name ?? 'GPU';
          _gpuAdapterVendor = '';
        }
        return true;
      } catch {
        return false;
      }
    })();
  }
  return _webGpuProbePromise;
}

async function ensureModelLoaded() {
  if (_loadState === 'ready') return;
  if (_loadState === 'loading') { await _loadPromise; return; }
  if (_loadState === 'error') {
    // Reset so a retry is possible after a transient failure
    _loadState = 'idle';
    _loadError = null;
  }

  _loadState = 'loading';
  _loadStartMs = performance.now();
  self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });

  _loadPromise = (async () => {
    const { AutoProcessor, AutoModelForImageTextToText, RawImage, env } = await import(_CDN_URL);
    env.useFSCache = false;

    const hasWebGpu = await probeWebGpu();
    const cfg = _MODELS[_activeModelKey];
    _processor = await AutoProcessor.from_pretrained(cfg.id);

    if (hasWebGpu) {
      try {
        _model = await AutoModelForImageTextToText.from_pretrained(cfg.id, { device: 'webgpu', dtype: cfg.webgpuDtype });
        _device = 'webgpu';
        _dtype = typeof cfg.webgpuDtype === 'string' ? cfg.webgpuDtype : 'mixed';
        _fp16FallbackUsed = false;
      } catch {
        if (cfg.webgpuDtypeFallback !== null) {
          _model = await AutoModelForImageTextToText.from_pretrained(cfg.id, { device: 'webgpu', dtype: cfg.webgpuDtypeFallback });
          _device = 'webgpu';
          _dtype = cfg.webgpuDtypeFallback;
          _fp16FallbackUsed = true;
        } else {
          _model = await AutoModelForImageTextToText.from_pretrained(cfg.id, { device: 'wasm', dtype: cfg.wasmDtype });
          _device = 'wasm';
          _dtype = typeof cfg.wasmDtype === 'string' ? cfg.wasmDtype : 'mixed';
          _fp16FallbackUsed = false;
        }
      }
    } else {
      _model = await AutoModelForImageTextToText.from_pretrained(cfg.id, { device: 'wasm', dtype: cfg.wasmDtype });
      _device = 'wasm';
      _dtype = typeof cfg.wasmDtype === 'string' ? cfg.wasmDtype : 'mixed';
    }

    _RawImage = RawImage;
    _loadEndMs = performance.now();
    _loadState = 'ready';
    self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });
  })();

  try {
    await _loadPromise;
  } catch (err) {
    _loadState = 'error';
    _loadError = err?.message ?? 'Model failed to load';
    self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });
    throw err;
  }
}

async function runInference(base64Frame, prompt, maxNewTokens = 96) {
  if (!base64Frame) {
    return {
      isAvailable: false,
      status: 'No frame captured',
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0,
      confidenceLabel: 'Unavailable',
    };
  }

  try {
    await ensureModelLoaded();
  } catch (err) {
    return {
      isAvailable: false,
      status: `Model unavailable: ${err?.message ?? 'load failed'}`,
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0,
      confidenceLabel: 'Unavailable',
    };
  }

  const image = await _RawImage.fromURL(base64Frame);
  const messages = [
    {
      role: 'user',
      content: [
        { type: 'image' },
        { type: 'text', text: prompt },
      ],
    },
  ];

  const text = _processor.apply_chat_template(messages, { add_generation_prompt: true });
  const inputs = await _processor(text, [image]);

  const inferStart = performance.now();
  const safeMaxNewTokens = Number.isFinite(maxNewTokens)
    ? Math.min(256, Math.max(32, Math.trunc(maxNewTokens)))
    : 96;
  const generatedIds = await _model.generate({
    ...inputs,
    max_new_tokens: safeMaxNewTokens,
  });
  _lastInferenceMs = Math.round(performance.now() - inferStart);
  _lastInferenceTimestamp = new Date().toISOString();
  _inferenceCount++;

  // Decode only the newly generated tokens, not the prompt prefix
  const newTokenIds = generatedIds.slice(null, [inputs.input_ids.dims[1], null]);
  const output = _processor.batch_decode(newTokenIds, { skip_special_tokens: true })[0].trim();
  _lastInferenceOutput = output;

  // Parse structured LABEL / NOTE response.
  // LABEL: is preferred. When absent (common with small models like 256M that don't
  // reliably follow format instructions), fall back to extracting the first sentence
  // of the raw output as a low-confidence activity rather than discarding the inference.
  const labelMatch = output.match(/LABEL:\s*([^|\n]+)/i);
  const noteMatch  = output.match(/NOTE:\s*([^\n]+)/i);

  const _DENY = new Set(['yes', 'no', 'ok', 'yeah', 'yep', 'nope', 'none', 'true', 'false', 'maybe']);

  let activity;
  let clinicalNote;
  let isUnstructured = false;

  if (!labelMatch) {
    // Fallback: use the first sentence of raw output as the activity summary.
    const rawTrimmed = output.replace(/\s+/g, ' ').trim();
    const firstSentence = rawTrimmed.split(/[.\n]/)[0].trim().slice(0, 80);

    if (firstSentence.length < 6 || _DENY.has(firstSentence.toLowerCase())) {
      return {
        isAvailable: false,
        status: 'Low-quality inference: unstructured output skipped',
        subjectHint: null,
        activity: 'Unavailable',
        clinicalPayload: '',
        isSignificant: false,
        significantReason: null,
        confidenceScore: 0.18,
        confidenceLabel: 'Low',
      };
    }

    activity = firstSentence;
    clinicalNote = rawTrimmed.slice(0, 200);
    isUnstructured = true;
  } else {
    activity     = labelMatch[1].trim().slice(0, 80);
    clinicalNote = (noteMatch?.[1] ?? output).trim();
  }

  const clinicalPayload = `<S>${clinicalNote}<E>`;

  // Quality gate: reject trivially short or deny-listed single-word outputs
  if (activity.length < 6 || _DENY.has(activity.toLowerCase())) {
    return {
      isAvailable: false,
      status: 'Low-quality inference: skipped',
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0.24,
      confidenceLabel: 'Low',
    };
  }

  const isSignificant = clinicalNote.length > 10;

  // Unstructured outputs from small models are capped at Low confidence (max 0.50).
  // Structured outputs with LABEL: use the full scoring range (0.55–0.98).
  const confidenceScore = isUnstructured
    ? Math.max(0.30, Math.min(0.50,
        0.32 +
        Math.min(activity.length, 32) / 120 +
        Math.min(clinicalNote.length, 160) / 500))
    : Math.max(0.55, Math.min(0.98,
        0.58 +
        Math.min(activity.length, 32) / 120 +
        Math.min(clinicalNote.length, 160) / 500 +
        (noteMatch ? 0.07 : 0) +
        (isSignificant ? 0.05 : 0)));
  const confidenceLabel = confidenceScore >= 0.85 ? 'High' : confidenceScore >= 0.72 ? 'Medium' : 'Low';

  return {
    isAvailable: true,
    status: isUnstructured ? 'Unstructured (low confidence)' : 'OK',
    subjectHint: null,
    activity,
    clinicalPayload,
    isSignificant,
    significantReason: isSignificant ? (isUnstructured ? 'Unstructured inference' : 'Inference result') : null,
    confidenceScore: Number(confidenceScore.toFixed(2)),
    confidenceLabel,
  };
}

// Message handler — each request carries a unique `id` so the bridge can
// match responses to the correct awaiting Promise.
self.onmessage = async (e) => {
  const { id, type, payload } = e.data;

  switch (type) {
    case 'RUN_INFERENCE': {
      let result;
      try {
        result = await runInference(payload.base64Frame, payload.prompt, payload.maxNewTokens);
      } catch (err) {
        result = {
          isAvailable: false,
          status: `Inference error: ${err?.message ?? 'unknown'}`,
          subjectHint: null,
          activity: 'Unavailable',
          clinicalPayload: '',
          isSignificant: false,
          significantReason: null,
          confidenceScore: 0,
          confidenceLabel: 'Unavailable',
        };
      }
      self.postMessage({ id, type: 'INFERENCE_RESULT', result });
      break;
    }

    case 'GET_STATE': {
      self.postMessage({ id, type: 'STATE', loadState: _loadState });
      break;
    }

    case 'SET_MODEL': {
      if (_MODELS[payload.modelKey] && _activeModelKey !== payload.modelKey) {
        _activeModelKey = payload.modelKey;
        _model = null;
        _processor = null;
        _RawImage = null;
        _loadState = 'idle';
        _loadError = null;
        _loadPromise = null;
        _device = null;
        _dtype = null;
        _fp16FallbackUsed = false;
        _loadStartMs = null;
        _loadEndMs = null;
        _inferenceCount = 0;
        _lastInferenceMs = null;
        _lastInferenceTimestamp = null;
        _lastInferenceOutput = null;
        self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });
      }
      self.postMessage({ id, type: 'MODEL_SET' });
      break;
    }

    case 'SET_POWER_PREFERENCE': {
      const valid = ['default', 'high-performance', 'low-power'];
      const pref = valid.includes(payload.preference) ? payload.preference : 'default';
      if (pref !== _gpuPowerPreference) {
        _gpuPowerPreference = pref;
        // Reset GPU probe and model load state so next inference uses the new adapter
        _webGpuProbePromise = null;
        _model = null;
        _processor = null;
        _RawImage = null;
        _loadState = 'idle';
        _loadError = null;
        _loadPromise = null;
        _device = null;
        _dtype = null;
        _fp16FallbackUsed = false;
        _loadStartMs = null;
        _loadEndMs = null;
        _inferenceCount = 0;
        _lastInferenceMs = null;
        _lastInferenceTimestamp = null;
        _lastInferenceOutput = null;
        self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });
      }
      self.postMessage({ id, type: 'POWER_PREFERENCE_SET' });
      break;
    }

    case 'GET_DIAGNOSTICS': {
      self.postMessage({
        id,
        type: 'DIAGNOSTICS',
        data: {
          modelId: _MODELS[_activeModelKey]?.id ?? _activeModelKey,
          loadState: _loadState,
          loadError: _loadError,
          device: _device,
          dtype: _dtype,
          fp16FallbackUsed: _fp16FallbackUsed,
          loadDurationMs: (_loadStartMs !== null && _loadEndMs !== null)
            ? Math.round(_loadEndMs - _loadStartMs)
            : null,
          inferenceCount: _inferenceCount,
          lastInferenceMs: _lastInferenceMs,
          lastInferenceTimestamp: _lastInferenceTimestamp,
          lastInferenceOutput: _lastInferenceOutput,
          webGpuPresent: typeof navigator !== 'undefined' && !!navigator.gpu,
          gpuAdapterVendor: _gpuAdapterVendor,
          gpuAdapterName: _gpuAdapterName,
        },
      });
      break;
    }
  }
};
