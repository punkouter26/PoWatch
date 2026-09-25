// inference-worker.js
// Runs AI model loading and token generation in a dedicated Web Worker,
// keeping the browser's main thread free so the UI stays responsive.
// The main thread (inference-bridge.js) handles all DOM access (video/canvas).

// Pinned, self-hosted transformers.js supply chain (rule §7): the library AND its ONNX Runtime wasm are
// vendored under wwwroot/lib/transformers-<version>/ and loaded from our own origin — no live third-party
// CDN import() at runtime, and the version cannot silently drift (@3 previously floated). To upgrade:
// vendor a new dist folder (transformers.min.js + ort-wasm-simd-threaded.jsep.{mjs,wasm}) and bump this line.
// The version lives in the folder NAME rather than a nested subfolder so the asset tree stays within the
// 2-level depth budget while keeping the pin explicit.
// Chrome does NOT hold messages while this module's top-level await (the registry fetch below) runs,
// and the real handler is only attached at the bottom of the file, so anything posted to a brand-new
// worker was silently dropped: the first caption of a fresh page never came back and the caption loop
// stayed busy for good. Buffer from the first line; the bottom of the file replays the buffer.
const _early = [];
self.onmessage = (e) => _early.push(e);

const _TRANSFORMERS_VERSION = '3.8.1';
const _TRANSFORMERS_BASE = new URL(`../lib/transformers-${_TRANSFORMERS_VERSION}/`, import.meta.url);
const _TRANSFORMERS_URL = new URL('transformers.min.js', _TRANSFORMERS_BASE).href;

// Transient-network retry. Model WEIGHTS still come from the HF hub (inherent to a browser VLM, §7),
// and on some networks those connections are reset mid-request — a TLS-inspecting proxy or a flaky
// route drops roughly a quarter of them. A single load issues many fetches (config, processor,
// tokenizer and several ONNX files, the largest ~190 MB) and transformers.js does not retry, so ONE
// reset failed the whole load: the UI showed "Model unavailable: Failed to fetch" and the observation
// loop skipped 100% of cycles while looking healthy. Wrapping the worker's global fetch is the only
// place the library's internal requests can be reached from here.
// Only idempotent methods are retried, so this can never re-send a mutating request.
const _FETCH_RETRIES = 3;
const _FETCH_BACKOFF_MS = 600;
const _nativeFetch = self.fetch.bind(self);

// Bytes the model loader actually pulled over the wire, accumulated here because transformers.js
// issues its own requests internally and reports no sizes. The System page's per-model self-test
// resets this before a load and reads it after, so "is this model too big for this laptop?" is
// answered with a measured number rather than a guess. Content-Length is still present on
// browser-cache hits, so a repeat run reports the same figure without re-downloading.
let _bytesFetched = 0;

async function fetchWithRetry(input, init) {
  const method = (init?.method ?? (typeof input === 'object' && input !== null ? input.method : null) ?? 'GET').toUpperCase();
  if (method !== 'GET' && method !== 'HEAD') return _nativeFetch(input, init);

  let lastError = null;
  for (let attempt = 0; attempt <= _FETCH_RETRIES; attempt++) {
    if (attempt > 0) {
      await new Promise((resolve) => setTimeout(resolve, _FETCH_BACKOFF_MS * 2 ** (attempt - 1)));
    }
    try {
      const response = await _nativeFetch(input, init);
      // 429 and 5xx are transient upstream states. A 4xx is a real answer — a missing or renamed
      // model file must fail fast, not burn three backoffs before reporting the same thing.
      if (response.status === 429 || response.status >= 500) {
        lastError = new Error(`HTTP ${response.status}`);
        continue;
      }
      if (method === 'GET' && response.ok) {
        const declaredLength = Number(response.headers.get('content-length'));
        if (Number.isFinite(declaredLength) && declaredLength > 0) _bytesFetched += declaredLength;
      }
      return response;
    } catch (err) {
      // A reset, DNS failure or TLS failure all surface as TypeError("Failed to fetch").
      if (init?.signal?.aborted) throw err;
      lastError = err;
    }
  }
  throw lastError ?? new Error('Failed to fetch');
}

self.fetch = fetchWithRetry;

// Single source of truth for the model registry (rule 1.5): /model-registry.json, shared verbatim with
// the C# model picker. modelClass drives the loader (causal-lm vs image-text-to-text); the webgpu/wasm
// dtype fields drive the fallback chain (§7). To add a model, edit ONLY the JSON — no code in three places.
// Top-level await: _MODELS is populated before the real message handler exists (early messages wait in _early).
const _MODELS = await (async () => {
  const res = await fetch(new URL('../model-registry.json', import.meta.url));
  if (!res.ok) throw new Error(`model-registry.json failed to load (${res.status})`);
  const list = await res.json();
  return Object.fromEntries(list.map((m) => [m.key, m]));
})();

// Model state
let _processor = null;
let _model = null;
let _RawImage = null;
let _loadState = 'idle'; // 'idle' | 'loading' | 'ready' | 'error'
let _loadError = null;
let _loadPromise = null;
// First registry entry = the default model (the list is ordered best-first). Derived rather
// than hard-coded so removing a model from the JSON cannot leave this pointing at a key
// that no longer exists — which would throw on the first ensureModelLoaded().
let _activeModelKey = Object.keys(_MODELS)[0];

// Inference diagnostics
let _device = null;
let _dtype = null;
let _fp16FallbackUsed = false;
// How far the RUNTIME fallback chain has escalated after empty generations:
//   0 = original config, 1 = webgpu at fallback precision, 2 = wasm, 3 = exhausted.
// The load-time chain in ensureModelLoaded only reacts to from_pretrained THROWING. A backend that
// loads fine and then emits NaN logits (argmax pinned to one pad token every step, so the whole
// budget decodes to nothing) never reached those fallbacks, which is how empty output survived
// both fp16 and fp32. Escalation is capped so a genuinely mute model stops reloading each cycle.
let _runtimeFallbackStage = 0;
let _loadStartMs = null;
let _loadEndMs = null;
let _inferenceCount = 0;
let _lastInferenceMs = null;
let _lastInferenceTimestamp = null;
let _lastInferenceOutput = null;
let _inferLock = Promise.resolve();

// Cached single WebGPU adapter probe — avoids multiple requestAdapter() calls.
let _webGpuProbePromise = null;
let _gpuAdapterVendor = null;
let _gpuAdapterName = null;
let _gpuPowerPreference = 'default'; // 'default' | 'high-performance' | 'low-power'

function isWindowsPlatform() {
  try {
    return typeof navigator !== 'undefined' && /Windows/i.test(navigator.userAgent || '');
  } catch {
    return false;
  }
}

function probeWebGpu() {
  if (!_webGpuProbePromise) {
    _webGpuProbePromise = (async () => {
      if (typeof navigator === 'undefined' || !navigator.gpu) return false;
      try {
        const shouldUsePowerPreference = _gpuPowerPreference !== 'default' && !isWindowsPlatform();
        const adapterOpts = shouldUsePowerPreference ? { powerPreference: _gpuPowerPreference } : undefined;
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

// Drop the loaded model and every derived diagnostic, leaving _activeModelKey alone. SET_MODEL,
// SET_POWER_PREFERENCE and the per-model self-test all need exactly this, and they had drifted into
// three hand-maintained copies of the same seventeen assignments — adding a field to one and not the
// others is how a stale device/dtype survives a model switch and misreports on the System page.
// Disposing releases the ONNX session (and its GPU buffers) instead of waiting for the collector,
// which matters when the self-test loads five models back to back on a laptop.
async function unloadModel() {
  const previousModel = _model;
  _model = null;
  _processor = null;
  _RawImage = null;
  _loadState = 'idle';
  _loadError = null;
  _loadPromise = null;
  _device = null;
  _dtype = null;
  _fp16FallbackUsed = false;
  _runtimeFallbackStage = 0;
  _loadStartMs = null;
  _loadEndMs = null;
  _inferenceCount = 0;
  _lastInferenceMs = null;
  _lastInferenceTimestamp = null;
  _lastInferenceOutput = null;

  if (previousModel && typeof previousModel.dispose === 'function') {
    try { await previousModel.dispose(); } catch { /* best effort — the reference is already gone */ }
  }
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
    const { AutoProcessor, AutoModelForImageTextToText, AutoModelForCausalLM, Qwen2VLForConditionalGeneration, Florence2ForConditionalGeneration, RawImage, env } = await import(_TRANSFORMERS_URL);
    env.useFSCache = false;
    // Load the ONNX Runtime wasm from the same vendored, pinned directory (offline supply chain, §7)
    // instead of letting transformers.js fetch it from its default CDN.
    env.backends.onnx.wasm.wasmPaths = _TRANSFORMERS_BASE.href;
    // Multi-threaded wasm needs SharedArrayBuffer, which needs cross-origin isolation. When the
    // page is isolated this is a large win on the CPU backend; when it is not, it stays single
    // threaded rather than throwing. Guarded so enabling COOP/COEP later needs no code change.
    if (typeof self !== 'undefined' && self.crossOriginIsolated) {
      env.backends.onnx.wasm.numThreads = Math.max(1, Math.min(4, navigator.hardwareConcurrency || 1));
    }

    const hasWebGpu = await probeWebGpu();
    const cfg = _MODELS[_activeModelKey];
    // Some VLM architectures (e.g. FastVLM / llava_qwen2) expose themselves as causal-LM heads rather
    // than the image-text-to-text auto class. Pick the loader per model so new families drop in cleanly.
    const ModelClass =
      cfg.modelClass === 'causal-lm' ? AutoModelForCausalLM
      : cfg.modelClass === 'qwen2vl' ? Qwen2VLForConditionalGeneration
      : cfg.modelClass === 'florence2' ? Florence2ForConditionalGeneration
      : AutoModelForImageTextToText;
    _processor = await AutoProcessor.from_pretrained(cfg.id);

    if (hasWebGpu) {
      try {
        _model = await ModelClass.from_pretrained(cfg.id, { device: 'webgpu', dtype: cfg.webgpuDtype });
        _device = 'webgpu';
        _dtype = typeof cfg.webgpuDtype === 'string' ? cfg.webgpuDtype : 'mixed';
        _fp16FallbackUsed = false;
      } catch {
        if (cfg.webgpuDtypeFallback !== null) {
          _model = await ModelClass.from_pretrained(cfg.id, { device: 'webgpu', dtype: cfg.webgpuDtypeFallback });
          _device = 'webgpu';
          _dtype = cfg.webgpuDtypeFallback;
          _fp16FallbackUsed = true;
        } else {
          _model = await ModelClass.from_pretrained(cfg.id, { device: 'wasm', dtype: cfg.wasmDtype });
          _device = 'wasm';
          _dtype = typeof cfg.wasmDtype === 'string' ? cfg.wasmDtype : 'mixed';
          _fp16FallbackUsed = false;
        }
      }
    } else {
      _model = await ModelClass.from_pretrained(cfg.id, { device: 'wasm', dtype: cfg.wasmDtype });
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

function describeError(err) {
  if (err == null) return 'unknown';
  if (typeof err === 'string' && err.trim()) return err.trim();
  if (typeof err === 'number' || typeof err === 'bigint') return String(err);
  const message = typeof err.message === 'string' ? err.message.trim() : '';
  if (message) return message;
  try {
    const asString = String(err);
    if (asString && asString !== '[object Object]') return asString;
  } catch {
    // ignore
  }
  return 'unknown';
}

// Frames arrive as a transferred ImageBitmap (no JPEG encode on the page, no base64 copy, no decode
// here), the same hand-off the detector worker uses.
let _frameCanvas = null;

function toRawImage(bitmap) {
  if (!_frameCanvas || _frameCanvas.width !== bitmap.width || _frameCanvas.height !== bitmap.height) {
    _frameCanvas = new OffscreenCanvas(bitmap.width, bitmap.height);
  }
  const ctx = _frameCanvas.getContext('2d', { willReadFrequently: true });
  ctx.drawImage(bitmap, 0, 0);
  const pixels = ctx.getImageData(0, 0, bitmap.width, bitmap.height);
  return new _RawImage(pixels.data, bitmap.width, bitmap.height, 4).rgb();
}

// Florence-2 is a task-token captioner: it ignores the free-text prompt, so it cannot echo it.
const _FLORENCE_TASK = '<CAPTION>';

function isFlorence() {
  return _MODELS[_activeModelKey]?.modelClass === 'florence2';
}

async function prepareInputs(image, prompt) {
  if (isFlorence()) return _processor(image, _processor.construct_prompts(_FLORENCE_TASK));

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
  // SmolVLM otherwise upscales the frame to its longest_edge and tiles it into up to 16 crops plus a
  // global view — hundreds of image tokens for a one-sentence caption. One global view is enough.
  return _processor(text, [image], { do_image_splitting: false });
}

function decodeGenerated(generatedIds, inputs) {
  if (isFlorence()) {
    return { output: _processor.batch_decode(generatedIds, { skip_special_tokens: true })[0].trim(), generationDiagnostic: null };
  }
  const newTokenIds = generatedIds.slice(null, [inputs.input_ids.dims[1], null]);
  let output = _processor.batch_decode(newTokenIds, { skip_special_tokens: true })[0].trim();
  let generationDiagnostic = null;

  if (output.length === 0) {
    let fullDecoded = '';
    try {
      fullDecoded = (_processor.batch_decode(generatedIds, { skip_special_tokens: true })[0] ?? '').trim();
    } catch (err) {
      fullDecoded = `<full decode failed: ${describeError(err)}>`;
    }

    const inputLen = inputs.input_ids.dims[1];
    const genDims = Array.isArray(generatedIds.dims) ? generatedIds.dims.join('x') : String(generatedIds.dims);
    generationDiagnostic =
      `inputTokens=${inputLen} generatedDims=${genDims} ` +
      `slicedChars=0 fullDecodeChars=${fullDecoded.length} device=${_device} dtype=${_dtype}` +
      (fullDecoded.length > 0 ? ` | fullDecode="${fullDecoded.slice(0, 400)}"` : '');

    if (fullDecoded.length > 0) {
      const assistantSplit = fullDecoded.split(/Assistant:\s*/i);
      const recovered = (assistantSplit.length > 1 ? assistantSplit[assistantSplit.length - 1] : '').trim();
      if (recovered.length > 0) output = recovered;
    }
  }

  return { output, generationDiagnostic };
}

function nextRuntimeFallback() {
  const cfg = _MODELS[_activeModelKey];
  if (_runtimeFallbackStage === 0 && _device === 'webgpu' && cfg?.webgpuDtypeFallback) {
    return { device: 'webgpu', dtype: cfg.webgpuDtypeFallback };
  }
  if (_runtimeFallbackStage <= 1 && cfg?.wasmDtype && _device !== 'wasm') {
    return { device: 'wasm', dtype: cfg.wasmDtype };
  }
  // A wasm session created as a fallback from a failed WebGPU run can still be unusable.
  // Reload it once with freshly prepared tensors before giving up.
  if (_runtimeFallbackStage <= 2 && cfg?.wasmDtype) {
    return { device: 'wasm', dtype: cfg.wasmDtype };
  }
  return null;
}

async function reloadModel(device, dtype) {
  const cfg = _MODELS[_activeModelKey];
  self.postMessage({ type: 'STATE_UPDATE', loadState: 'loading' });
  const { AutoModelForImageTextToText, AutoModelForCausalLM, Qwen2VLForConditionalGeneration, Florence2ForConditionalGeneration } = await import(_TRANSFORMERS_URL);
  const RetryModelClass =
    cfg.modelClass === 'causal-lm' ? AutoModelForCausalLM
    : cfg.modelClass === 'qwen2vl' ? Qwen2VLForConditionalGeneration
    : cfg.modelClass === 'florence2' ? Florence2ForConditionalGeneration
    : AutoModelForImageTextToText;
  _model = await RetryModelClass.from_pretrained(cfg.id, { device, dtype });
  _device = device;
  _dtype = typeof dtype === 'string' ? dtype : 'mixed';
  _fp16FallbackUsed = device === 'webgpu';
  self.postMessage({ type: 'STATE_UPDATE', loadState: 'ready' });
}

function unavailable(status, rawOutput = '', generationDiagnostic = null) {
  return { isAvailable: false, status, rawOutput, generationDiagnostic, activity: 'Unavailable', caption: '' };
}

// Words that say nothing about the scene, and the signature of a small model stuck in a loop.
const _DENY = new Set(['yes', 'no', 'ok', 'yeah', 'yep', 'nope', 'none', 'true', 'false', 'maybe']);

function hasRepetition(text) {
  const counts = Object.create(null);
  for (const w of text.toLowerCase().split(/\s+/)) {
    if (w.length < 2) continue;
    counts[w] = (counts[w] ?? 0) + 1;
    if (counts[w] > 3) return true;
  }
  return /(.{3,})\1{3,}/i.test(text);
}

async function runInference(bitmap, prompt, maxNewTokens = 32) {
  if (!bitmap) return unavailable('No frame captured');

  try {
    await ensureModelLoaded();
  } catch (err) {
    // "Failed to fetch" is what the browser reports for a reset, DNS or TLS failure alike, and on its
    // own it reads as an app bug rather than what it is — the model weights could not be downloaded.
    const detail = describeError(err);
    return unavailable(/failed to fetch|networkerror|network error|load failed/i.test(detail)
      ? `Model unavailable: could not download model weights after ${_FETCH_RETRIES + 1} attempts — check the network connection to the model host`
      : `Model unavailable: ${detail}`);
  }

  const inferStart = performance.now();
  const safeMaxNewTokens = Number.isFinite(maxNewTokens)
    ? Math.min(96, Math.max(16, Math.trunc(maxNewTokens)))
    : 32;

  const image = toRawImage(bitmap);
  bitmap.close?.();
  // Rebuild tensors after every backend switch. Reusing WebGPU inputs on wasm (or a poisoned
  // session) is how generate() started throwing a bare ONNX code with no .message.
  let inputs = await prepareInputs(image, prompt);
  let generateError = null;
  let output = '';
  let generationDiagnostic = null;

  for (let attempt = 0; attempt < 4; attempt++) {
    generateError = null;
    let generatedIds = null;
    try {
      generatedIds = await _model.generate({
        ...inputs,
        max_new_tokens: safeMaxNewTokens,
        do_sample: false,
        // Greedy decoding on a small model loops ("the the the…"); forbidding a repeated trigram
        // stops that at the source instead of rejecting the caption afterwards.
        no_repeat_ngram_size: 3,
      });
    } catch (err) {
      generateError = describeError(err);
    }

    if (generatedIds) {
      ({ output, generationDiagnostic } = decodeGenerated(generatedIds, inputs));
      if (output.length > 0) break;
    }

    const fallback = nextRuntimeFallback();
    if (!fallback) break;

    _runtimeFallbackStage += 1;
    try {
      await reloadModel(fallback.device, fallback.dtype);
      inputs = await prepareInputs(image, prompt);
    } catch (err) {
      generateError = `Fallback to ${fallback.device}/${fallback.dtype} failed: ${describeError(err)}`;
      break;
    }
  }

  _lastInferenceMs = Math.round(performance.now() - inferStart);
  _lastInferenceTimestamp = new Date().toISOString();
  _inferenceCount++;
  _lastInferenceOutput = output;

  // Distinguish "the model said nothing" from "the model said something we rejected".
  if (output.length === 0) {
    return unavailable(
      generateError
        ? `Inference error: ${generateError}`
        : (_runtimeFallbackStage > 0 ? `Model returned an empty response (retried on ${_device}/${_dtype})` : 'Model returned an empty response'),
      '',
      generationDiagnostic || (generateError ? `generateFailed device=${_device} dtype=${_dtype} error=${generateError}` : null));
  }

  // The prompt asks for one plain sentence; the C# caption parser keeps the first one. Here only the
  // replies that are clearly not a description are dropped.
  const text = output.replace(/\s+/g, ' ').trim();
  const activity = text.split(/[.\n]/)[0].trim().slice(0, 80);
  if (activity.length < 6 || _DENY.has(activity.toLowerCase()) || hasRepetition(activity)) {
    return unavailable('Low-quality inference: skipped', output);
  }

  // Small VLMs copy instruction text verbatim; a fabricated record that reads plausibly is worse than a
  // rejected one. Only the instruction line counts: words from the "Visible:" hint are meant to be reused.
  const instruction = (prompt ?? '').split('\n')[0].toLowerCase();
  const promptWords = new Set(instruction.match(/[a-z]{4,}/g) ?? []);
  const activityWords = activity.toLowerCase().match(/[a-z]{4,}/g) ?? [];
  const echoed = activityWords.filter((w) => promptWords.has(w)).length;
  if (/[<>]/.test(activity) || (activityWords.length >= 3 && echoed / activityWords.length >= 0.8)) {
    return unavailable('Low-quality inference: prompt echoed instead of describing the scene', output);
  }

  // A sentence ending on a bare article or preposition ran out of tokens mid-thought.
  if (/\b(?:is|has|in|at|of|on|with)\s+(?:a|an|the)\s*\.?\s*$|\ba\s+bit\s*\.?\s*$/i.test(activity)) {
    return unavailable('Low-quality inference: incomplete sentence', output);
  }

  return { isAvailable: true, status: 'OK', activity, caption: text.slice(0, 200) };
}

// Per-model self-test for the System page. Loads ONE registry model and runs a single generation
// against a fixed frame supplied by the bridge, then reports what actually happened on THIS device:
// which backend was chosen, which dtype survived the fallback chain, how many bytes came down, how
// long the load and the generation took, and the verbatim reply.
//
// It deliberately drives the real path — ensureModelLoaded() and runInference(), the same two calls
// the observation loop makes — rather than a private loader. A test with its own code path can pass
// while the loop still fails, which is the opposite of what someone checking "will this run on my
// laptop?" needs.
//
// The worker holds one model at a time, so the test necessarily evicts whatever was loaded. The
// previously selected key is restored UNLOADED before returning, so the Live Room reloads its own
// model on next use instead of silently inheriting the test's.
async function runModelTest(modelKey, frame, prompt, maxNewTokens) {
  const cfg = _MODELS[modelKey];
  const result = {
    modelKey,
    modelId: cfg?.id ?? modelKey,
    label: cfg?.label ?? modelKey,
    ok: false,
    stage: 'registry',
    error: null,
    device: null,
    dtype: null,
    fp16FallbackUsed: false,
    webGpuPresent: typeof navigator !== 'undefined' && !!navigator.gpu,
    gpuAdapterName: null,
    loadMs: null,
    inferenceMs: null,
    totalMs: null,
    bytesFetched: 0,
    rawOutput: '',
    pipelineStatus: null,
  };

  if (!cfg) {
    result.error = `Unknown model key '${modelKey}' — not in model-registry.json`;
    return result;
  }

  const previousKey = _activeModelKey;
  const startedMs = performance.now();

  await unloadModel();
  _activeModelKey = modelKey;
  _bytesFetched = 0;

  result.stage = 'load';
  try {
    await ensureModelLoaded();
  } catch (err) {
    // ensureModelLoaded already wrapped the network retries, so a fetch failure here means the
    // weights are genuinely unreachable — say that instead of the bare browser message.
    const detail = describeError(err);
    result.error = /failed to fetch|networkerror|network error|load failed/i.test(detail)
      ? `Could not download the model weights after ${_FETCH_RETRIES + 1} attempts — check the network connection to the model host`
      : detail;
    result.bytesFetched = _bytesFetched;
    result.totalMs = Math.round(performance.now() - startedMs);
    result.gpuAdapterName = _gpuAdapterName;
    await unloadModel();
    _activeModelKey = previousKey;
    self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });
    return result;
  }

  result.stage = 'inference';
  let inference = null;
  try {
    inference = await runInference(frame, prompt, maxNewTokens);
  } catch (err) {
    result.error = describeError(err);
  }

  // Read the backend AFTER generating: the runtime fallback chain can move the model from webgpu to
  // wasm mid-run, and reporting the load-time choice would hide exactly the escalation the operator
  // needs to see.
  result.device = _device;
  result.dtype = _dtype;
  result.fp16FallbackUsed = _fp16FallbackUsed;
  result.gpuAdapterName = _gpuAdapterName;
  result.loadMs = (_loadStartMs !== null && _loadEndMs !== null) ? Math.round(_loadEndMs - _loadStartMs) : null;
  result.inferenceMs = _lastInferenceMs;
  result.bytesFetched = _bytesFetched;
  result.rawOutput = _lastInferenceOutput ?? '';
  result.pipelineStatus = inference?.status ?? null;
  result.totalMs = Math.round(performance.now() - startedMs);

  // Pass means the ENGINE works: the model loaded and generated text. The quality gates that reject
  // an unstructured or short caption are a property of the prompt and the model's size, not of
  // whether this device can run it — a 256M captioner routinely produces a perfectly good sentence
  // that the LABEL gate declines. Gating the self-test on them would report a working laptop as
  // broken, so the gate's verdict is carried separately in pipelineStatus instead.
  result.ok = result.rawOutput.length > 0;
  result.stage = 'done';
  if (!result.ok && !result.error) {
    result.error = inference?.status ?? 'The model loaded but generated no text';
  }

  await unloadModel();
  _activeModelKey = previousKey;
  self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });
  return result;
}

// Message handler — each request carries a unique `id` so the bridge can
// match responses to the correct awaiting Promise.
// Makes a registry model the active one, dropping whatever was loaded; unknown or null keys are ignored.
async function useModel(modelKey) {
  if (!_MODELS[modelKey] || _activeModelKey === modelKey) return;
  _activeModelKey = modelKey;
  await unloadModel();
  self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });
}

self.onmessage = async (e) => {
  const { id, type, payload } = e.data;

  switch (type) {
    case 'RUN_INFERENCE': {
      // The page's model pick travels with each request; switching inside the lock never cuts a run short.
      const run = _inferLock.then(async () => {
        await useModel(payload.modelKey);
        return runInference(payload.frame, payload.prompt, payload.maxNewTokens);
      });
      _inferLock = run.then(() => undefined, () => undefined);
      let result;
      try {
        result = await run;
      } catch (err) {
        result = unavailable(`Inference error: ${describeError(err)}`);
      }
      self.postMessage({ id, type: 'INFERENCE_RESULT', result });
      break;
    }

    case 'MODEL_TEST': {
      // Shares _inferLock with RUN_INFERENCE: the test swaps the loaded model out from under the
      // worker, so it must never overlap an observation cycle.
      const test = _inferLock.then(() => runModelTest(
        payload.modelKey, payload.frame, payload.prompt, payload.maxNewTokens));
      _inferLock = test.then(() => undefined, () => undefined);
      let testResult;
      try {
        testResult = await test;
      } catch (err) {
        testResult = {
          modelKey: payload.modelKey,
          modelId: payload.modelKey,
          label: payload.modelKey,
          ok: false,
          stage: 'error',
          error: describeError(err),
          device: null,
          dtype: null,
          fp16FallbackUsed: false,
          webGpuPresent: typeof navigator !== 'undefined' && !!navigator.gpu,
          gpuAdapterName: null,
          loadMs: null,
          inferenceMs: null,
          totalMs: null,
          bytesFetched: 0,
          rawOutput: '',
          pipelineStatus: null,
        };
      }
      self.postMessage({ id, type: 'MODEL_TEST_RESULT', result: testResult });
      break;
    }

    case 'GET_STATE': {
      self.postMessage({ id, type: 'STATE', loadState: _loadState });
      break;
    }

    case 'SET_MODEL': {
      await useModel(payload.modelKey);
      self.postMessage({ id, type: 'MODEL_SET' });
      break;
    }

    case 'UNLOAD_MODEL': {
      // Standby / wake-on-motion: drop the loaded model and derived diagnostics without
      // changing _activeModelKey, so the next RUN_INFERENCE call picks up the same model.
      // Releases the ONNX session + GPU buffers immediately rather than waiting for GC.
      await unloadModel();
      self.postMessage({ id, type: 'MODEL_UNLOADED' });
      self.postMessage({ type: 'STATE_UPDATE', loadState: _loadState });
      break;
    }

    case 'SET_POWER_PREFERENCE': {
      const valid = ['default', 'high-performance', 'low-power'];
      let pref = valid.includes(payload.preference) ? payload.preference : 'default';
      if (isWindowsPlatform()) {
        // Current Chromium on Windows ignores powerPreference and logs a warning.
        pref = 'default';
      }
      if (pref !== _gpuPowerPreference) {
        _gpuPowerPreference = pref;
        // Reset GPU probe and model load state so next inference uses the new adapter
        _webGpuProbePromise = null;
        await unloadModel();
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

for (const e of _early.splice(0)) self.onmessage(e);
