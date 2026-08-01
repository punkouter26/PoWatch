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
const _TRANSFORMERS_VERSION = '3.8.1';
const _TRANSFORMERS_BASE = new URL(`../lib/transformers-${_TRANSFORMERS_VERSION}/`, import.meta.url);
const _TRANSFORMERS_URL = new URL('transformers.min.js', _TRANSFORMERS_BASE).href;

// Single source of truth for the model registry (rule 1.5): /model-registry.json, shared verbatim with
// the C# model picker. modelClass drives the loader (causal-lm vs image-text-to-text); the webgpu/wasm
// dtype fields drive the fallback chain (§7). To add a model, edit ONLY the JSON — no code in three places.
// This is a module worker, so top-level await guarantees _MODELS is populated before any message is
// processed (message events are queued until module evaluation, including this await, completes).
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
let _activeModelKey = 'smolvlm-256m';

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
    const { AutoProcessor, AutoModelForImageTextToText, AutoModelForCausalLM, RawImage, env } = await import(_TRANSFORMERS_URL);
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
    const ModelClass = cfg.modelClass === 'causal-lm' ? AutoModelForCausalLM : AutoModelForImageTextToText;
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
  let output = _processor.batch_decode(newTokenIds, { skip_special_tokens: true })[0].trim();

  // An empty generation is the signature failure of SmolVLM at fp16 on WebGPU: the model loads and
  // runs (burning the full inference time) but the logits go NaN, so every token decodes to nothing.
  // webgpuDtypeFallback previously only covered LOAD exceptions, so this case never recovered — it
  // fed the quality gate an empty string, which surfaced as "unstructured output skipped" and
  // silently starved the ingest pipeline. Retry once at the declared fallback precision.
  while (output.length === 0 && _runtimeFallbackStage < 3) {
    const cfg = _MODELS[_activeModelKey];

    // Choose the next rung of the chain: reduced precision on WebGPU, then the wasm backend, which
    // is numerically independent of the GPU path and is what actually rescues a NaN-producing GPU.
    let nextDevice = null;
    let nextDtype = null;
    if (_runtimeFallbackStage === 0 && _device === 'webgpu' && cfg?.webgpuDtypeFallback) {
      nextDevice = 'webgpu';
      nextDtype = cfg.webgpuDtypeFallback;
    } else if (_runtimeFallbackStage <= 1 && cfg?.wasmDtype) {
      nextDevice = 'wasm';
      nextDtype = cfg.wasmDtype;
    }

    _runtimeFallbackStage = nextDevice === 'wasm' ? 2 : _runtimeFallbackStage + 1;
    if (!nextDevice) {
      _runtimeFallbackStage = 3;
      break;
    }

    self.postMessage({ type: 'STATE_UPDATE', loadState: 'loading' });
    const { AutoModelForImageTextToText, AutoModelForCausalLM } = await import(_TRANSFORMERS_URL);
    const RetryModelClass = cfg.modelClass === 'causal-lm' ? AutoModelForCausalLM : AutoModelForImageTextToText;
    _model = await RetryModelClass.from_pretrained(cfg.id, { device: nextDevice, dtype: nextDtype });
    _device = nextDevice;
    _dtype = typeof nextDtype === 'string' ? nextDtype : 'mixed';
    _fp16FallbackUsed = nextDevice === 'webgpu';
    self.postMessage({ type: 'STATE_UPDATE', loadState: 'ready' });

    const retryIds = await _model.generate({ ...inputs, max_new_tokens: safeMaxNewTokens });
    output = _processor
      .batch_decode(retryIds.slice(null, [inputs.input_ids.dims[1], null]), { skip_special_tokens: true })[0]
      .trim();
  }

  // An empty decode has two very different causes that looked identical: the model genuinely
  // emitted nothing, or generate() returned ONLY the new tokens (not prompt+new) so slicing at
  // input_ids.dims[1] cut past the end and threw the answer away. Decode the untrimmed sequence:
  // if THAT has content, the slice is the bug, and the recovered text is the real answer.
  let generationDiagnostic = null;
  if (output.length === 0) {
    let fullDecoded = '';
    try {
      fullDecoded = (_processor.batch_decode(generatedIds, { skip_special_tokens: true })[0] ?? '').trim();
    } catch (err) {
      fullDecoded = `<full decode failed: ${err?.message ?? 'unknown'}>`;
    }

    const inputLen = inputs.input_ids.dims[1];
    const genDims = Array.isArray(generatedIds.dims) ? generatedIds.dims.join('x') : String(generatedIds.dims);
    generationDiagnostic =
      `inputTokens=${inputLen} generatedDims=${genDims} ` +
      `slicedChars=0 fullDecodeChars=${fullDecoded.length} device=${_device} dtype=${_dtype}` +
      (fullDecoded.length > 0 ? ` | fullDecode="${fullDecoded.slice(0, 400)}"` : '');

    // Recover the answer when the untrimmed decode carries text the slice discarded. The chat
    // template echoes the prompt, so take whatever follows the final Assistant turn.
    if (fullDecoded.length > 0) {
      const assistantSplit = fullDecoded.split(/Assistant:\s*/i);
      const recovered = (assistantSplit.length > 1 ? assistantSplit[assistantSplit.length - 1] : '').trim();
      if (recovered.length > 0) {
        output = recovered;
      }
    }
  }

  _lastInferenceOutput = output;

  // Distinguish "the model said nothing" from "the model said something we rejected". These had
  // identical symptoms before, which is why 33 cycles of empty output read as a quality problem.
  if (output.length === 0) {
    return {
      isAvailable: false,
      status: _runtimeFallbackStage > 0
        ? `Model returned an empty response (retried on ${_device}/${_dtype})`
        : 'Model returned an empty response',
      rawOutput: '',
      generationDiagnostic,
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0,
      confidenceLabel: 'Empty',
    };
  }

  // Parse structured LABEL / NOTE response.
  // LABEL: is preferred. Accept common misspellings/malformations emitted by small models:
  //   - LABLE, LABELL, LABERF (off-by-one/extra chars)
  //   - LABEL (correct)
  // Also handles: L A B E L (space-separated), [LABEL], etc.
  // When absent, fall back to extracting the first sentence as low-confidence activity.
  const labelRegex = /(?:^|\n)\s*\[?L\s*A\s*B\s*E\s*L\s*\]?\s*:\s*([^|\n]+)/i;
  const labelMatch = output.match(labelRegex);
  const noteMatch  = output.match(/NOTE:\s*([^\n]+)/i);

  const _DENY = new Set(['yes', 'no', 'ok', 'yeah', 'yep', 'nope', 'none', 'true', 'false', 'maybe']);

  // Detect repetitive / hallucinated output, e.g.:
  //   "I am I I I I I I I" — any word repeated more than 3 times
  //   "I'm'NON'NON'NON..."  — a short substring repeated 4+ times in a row
  //   "the most common and most common..." — word-pair repetition
  // Returns true when the text is dominated by repetition and should be discarded.
  function hasRepetition(text) {
    const words = text.toLowerCase().split(/\s+/);
    const wordCounts = Object.create(null);
    for (const w of words) {
      if (w.length < 2) continue;
      wordCounts[w] = (wordCounts[w] ?? 0) + 1;
      if (wordCounts[w] > 3) return true;
    }
    // Character-level: any 3+-char chunk that repeats 4+ times consecutively
    return /(.{3,})\1{3,}/i.test(text);
  }

  let activity;
  let clinicalNote;
  let isUnstructured = false;

  if (!labelMatch) {
    // Fallback: use the first sentence of raw output as the activity summary.
    const rawTrimmed = output.replace(/\s+/g, ' ').trim();
    const firstSentence = rawTrimmed.split(/[.\n]/)[0].trim().slice(0, 80);
    // Enhanced normalization: remove malformed label prefixes and clean up
    const normalizedSentence = firstSentence
      .replace(/^[L\s]*A[A\s]*B[B\s]*E[E\s]*L[L\s]*\s*:\s*/i, '') // Space-separated LABEL variants
      .replace(/^(?:LABEL|LABLE|LABELL|LABERF)\s*:\s*/i, '')          // Compact variants
      .replace(/^\[.*?\]\s*/, '')                                      // Remove bracketed prefixes
      .replace(/^[^\w\s]*/, '')                                        // Remove leading non-alphanumeric
      .trim();

    if (normalizedSentence.length < 6 || _DENY.has(normalizedSentence.toLowerCase()) || hasRepetition(normalizedSentence)) {
      return {
        isAvailable: false,
        status: 'Low-quality inference: unstructured output skipped',
        rawOutput: output,
        subjectHint: null,
        activity: 'Unavailable',
        clinicalPayload: '',
        isSignificant: false,
        significantReason: null,
        confidenceScore: 0.18,
        confidenceLabel: 'Low',
      };
    }

    activity = normalizedSentence;
    clinicalNote = rawTrimmed.slice(0, 200);
    isUnstructured = true;
  } else {
    activity = labelMatch[1].trim().slice(0, 80);
    // When NOTE is absent, fall back to the activity text to avoid storing the raw
    // "LABEL: <text>" prefix in the clinical description field.
    clinicalNote = noteMatch?.[1]?.trim() ?? activity;
  }

  // Guard: reject any output that echoes prompt placeholder tokens (e.g. "<5 word activity>")
  if (/[<>]/.test(activity)) {
    return {
      isAvailable: false,
      status: 'Low-quality inference: prompt echo detected',
        rawOutput: output,
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0,
      confidenceLabel: 'Low',
    };
  }

  // Quality gate: reject trivially short, deny-listed, or repetitive outputs
  if (activity.length < 6 || _DENY.has(activity.toLowerCase()) || hasRepetition(activity)) {
    return {
      isAvailable: false,
      status: 'Low-quality inference: skipped',
        rawOutput: output,
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0.24,
      confidenceLabel: 'Low',
    };
  }

  // Guard: reject activities that echo prompt format hints, e.g. "5 words: 1", "word count:", etc.
  // These occur when small models repeat instructions rather than describing the scene.
  if (/^\d+\s+words?/i.test(activity) || /^word\s+count/i.test(activity) || /^(?:short\s+)?activity\s+phrase/i.test(activity)) {
    return {
      isAvailable: false,
      status: 'Low-quality inference: prompt format echo detected',
        rawOutput: output,
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0,
      confidenceLabel: 'Low',
    };
  }

  // Guard: reject output that parrots the prompt instead of describing the scene. Small VLMs copy
  // instruction text and worked examples verbatim — the old prompt's example line was recorded as a
  // real observation four times in a row. A fabricated record that reads plausibly is worse than a
  // rejected one, so anything substantially overlapping the prompt is discarded.
  const promptWords = new Set(
    (prompt ?? '').toLowerCase().match(/[a-z]{4,}/g) ?? []);
  const activityWords = activity.toLowerCase().match(/[a-z]{4,}/g) ?? [];
  const echoedWordCount = activityWords.filter((w) => promptWords.has(w)).length;
  if (
    /^(?:task|answer|question|instruction|example|reply|response|output)\s*[:\-]/i.test(activity) ||
    (activityWords.length >= 3 && echoedWordCount / activityWords.length >= 0.8)
  ) {
    return {
      isAvailable: false,
      status: 'Low-quality inference: prompt echoed instead of describing the scene',
      rawOutput: output,
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0,
      confidenceLabel: 'Low',
    };
  }

  // Guard: reject obviously incomplete sentences that indicate the model did not finish its output.
  // e.g. "The scene is a bit", "Person is a", "Room has a"
  // An activity ending with a bare article or preposition is structurally unfinished.
  if (/\b(?:is\s+a|is\s+an|is\s+the|has\s+a|has\s+an|a\s+bit|in\s+a|in\s+the|at\s+a|at\s+the|of\s+a|on\s+a)\s*\.?\s*$/i.test(activity)) {
    return {
      isAvailable: false,
      status: 'Low-quality inference: incomplete sentence',
        rawOutput: output,
      subjectHint: null,
      activity: 'Unavailable',
      clinicalPayload: '',
      isSignificant: false,
      significantReason: null,
      confidenceScore: 0,
      confidenceLabel: 'Low',
    };
  }

  // Significance is NOT decided here any more. This used to be `clinicalNote.length > 10`, which
  // every well-formed caption satisfied, so 100% of observations arrived flagged "Notable" — the
  // amber tint, the unacknowledged counters and the spoken announcements all lost their meaning.
  // The server now classifies from the caption's content (ActivitySignificanceClassifier) and
  // returns its verdict on the ingest response; the worker just reports what it saw.
  const clinicalPayload = `<S>${clinicalNote}<E>`;

  // A plain caption is now the EXPECTED result, not a degraded one: the prompt asks a question
  // rather than demanding a LABEL/NOTE format the small models cannot produce. Capping captions at
  // 0.50 and tagging them "Unstructured inference" made every correct observation look suspect.
  // A LABEL: reply still scores slightly higher because it carries an explicit activity/note split.
  const confidenceScore = Math.max(0.40, Math.min(isUnstructured ? 0.88 : 0.98,
    (isUnstructured ? 0.46 : 0.58) +
    Math.min(activity.length, 32) / 120 +
    Math.min(clinicalNote.length, 160) / 500 +
    (noteMatch ? 0.07 : 0)));
  const confidenceLabel = confidenceScore >= 0.85 ? 'High' : confidenceScore >= 0.72 ? 'Medium' : 'Low';

  return {
    isAvailable: true,
    status: 'OK',
    subjectHint: null,
    activity,
    clinicalPayload,
    isSignificant: false,
    significantReason: null,
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
        _runtimeFallbackStage = 0;
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
      let pref = valid.includes(payload.preference) ? payload.preference : 'default';
      if (isWindowsPlatform()) {
        // Current Chromium on Windows ignores powerPreference and logs a warning.
        pref = 'default';
      }
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
        _runtimeFallbackStage = 0;
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
