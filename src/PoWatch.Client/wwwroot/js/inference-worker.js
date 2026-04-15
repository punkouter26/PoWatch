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
  'lfm2-vl-450m': {
    id: 'LiquidAI/LFM2.5-VL-450M-ONNX',
    label: 'LFM2.5-VL 450M',
    webgpuDtype: { vision_encoder: 'fp16', embed_tokens: 'fp16', decoder_model_merged: 'q4' },
    webgpuDtypeFallback: null,
    wasmDtype: { vision_encoder: 'q8', embed_tokens: 'fp16', decoder_model_merged: 'q8' },
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
function probeWebGpu() {
  if (!_webGpuProbePromise) {
    _webGpuProbePromise = (async () => {
      if (typeof navigator === 'undefined' || !navigator.gpu) return false;
      try {
        const adapter = await navigator.gpu.requestAdapter();
        return adapter !== null;
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

async function runInference(base64Frame, prompt) {
  if (!base64Frame) {
    return {
      isAvailable: false,
      status: 'No frame captured',
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
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
  const generatedIds = await _model.generate({
    ...inputs,
    max_new_tokens: 200,
  });
  _lastInferenceMs = Math.round(performance.now() - inferStart);
  _lastInferenceTimestamp = new Date().toISOString();
  _inferenceCount++;

  // Decode only the newly generated tokens, not the prompt prefix
  const newTokenIds = generatedIds.slice(null, [inputs.input_ids.dims[1], null]);
  const output = _processor.batch_decode(newTokenIds, { skip_special_tokens: true })[0].trim();
  _lastInferenceOutput = output;

  // Parse structured LABEL / NOTE response
  const labelMatch = output.match(/LABEL:\s*([^|\n]+)/i);
  const noteMatch  = output.match(/NOTE:\s*([^\n]+)/i);
  let activity     = (labelMatch?.[1] ?? output.split(/[.,;]/)[0]).trim().slice(0, 80);
  const clinicalNote    = (noteMatch?.[1]  ?? output).trim();
  const clinicalPayload = `<S>${clinicalNote}<E>`;

  // Quality gate: reject trivially short or deny-listed single-word outputs
  const _DENY = new Set(['yes', 'no', 'ok', 'yeah', 'yep', 'nope', 'none', 'true', 'false', 'maybe']);
  if (activity.length < 6 || _DENY.has(activity.toLowerCase())) {
    return {
      isAvailable: false,
      status: 'Low-quality inference: skipped',
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
    };
  }

  const isSignificant = clinicalNote.length > 10;

  return {
    isAvailable: true,
    status: 'OK',
    subjectHint: null,
    activity,
    clinicalPayload,
    isSignificant,
    significantReason: isSignificant ? 'Inference result' : null,
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
        result = await runInference(payload.base64Frame, payload.prompt);
      } catch (err) {
        result = {
          isAvailable: false,
          status: `Inference error: ${err?.message ?? 'unknown'}`,
          subjectHint: null,
          activity: 'Unavailable',
          clinicalPayload: '',
          isSignificant: false,
          significantReason: null,
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
        },
      });
      break;
    }
  }
};
