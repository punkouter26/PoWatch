// inference-worker.js
// Runs AI model loading and token generation in a dedicated Web Worker,
// keeping the browser's main thread free so the UI stays responsive.
// The main thread (inference-bridge.js) handles all DOM access (video/canvas).

// The vendored transformers.js, its fetch retry and the frame hand-off live in transformers-env.js.
import { FETCH_ATTEMPTS, bitmapToRawImage, loadTransformers } from './transformers-env.js';

// The one caption model. Weights come from the HF hub; the dtype fields drive the fallback chain.
const _MODEL = {
  id: 'HuggingFaceTB/SmolVLM2-500M-Video-Instruct',
  webgpuDtype: 'fp16',
  webgpuDtypeFallback: 'fp32',
  wasmDtype: 'q8',
};

// Model state
let _processor = null;
let _model = null;
let _RawImage = null;
let _ModelClass = null;
let _loadState = 'idle'; // 'idle' | 'loading' | 'ready' | 'error'
let _loadError = null;
let _loadPromise = null;

// Inference diagnostics
let _device = null;
let _dtype = null;
let _fp16FallbackUsed = false;
// How far the RUNTIME fallback chain has escalated after empty generations:
//   0 = original config, 1 = webgpu at fallback precision, 2 = wasm, 3 = exhausted.
// A backend that loads fine and then emits NaN logits never throws, so load-time fallbacks alone
// let empty output survive both fp16 and fp32. Escalation is capped so a mute model stops reloading.
let _runtimeFallbackStage = 0;
let _loadDurationMs = null;
let _inferenceCount = 0;
let _lastInferenceMs = null;
let _lastInferenceTimestamp = null;
let _lastInferenceOutput = null;
let _inferLock = Promise.resolve();

async function hasWebGpu() {
  try {
    return !!navigator.gpu && (await navigator.gpu.requestAdapter()) !== null;
  } catch {
    return false;
  }
}

async function ensureModelLoaded() {
  if (_loadState === 'ready') return;
  if (_loadState === 'loading') { await _loadPromise; return; }

  _loadState = 'loading';
  _loadError = null;
  const started = performance.now();

  _loadPromise = (async () => {
    const { AutoProcessor, AutoModelForImageTextToText, RawImage } = await loadTransformers();

    _ModelClass = AutoModelForImageTextToText;
    _processor = await AutoProcessor.from_pretrained(_MODEL.id);

    if (await hasWebGpu()) {
      try {
        await loadOn('webgpu', _MODEL.webgpuDtype);
      } catch {
        await loadOn('webgpu', _MODEL.webgpuDtypeFallback);
        _fp16FallbackUsed = true;
      }
    } else {
      await loadOn('wasm', _MODEL.wasmDtype);
    }

    _RawImage = RawImage;
    _loadDurationMs = Math.round(performance.now() - started);
    _loadState = 'ready';
  })();

  try {
    await _loadPromise;
  } catch (err) {
    _loadState = 'error';
    _loadError = err?.message ?? 'Model failed to load';
    throw err;
  }
}

async function loadOn(device, dtype) {
  _model = await _ModelClass.from_pretrained(_MODEL.id, { device, dtype });
  _device = device;
  _dtype = dtype;
}

function describeError(err) {
  if (err == null) return 'unknown';
  if (typeof err === 'string' && err.trim()) return err.trim();
  if (typeof err === 'number' || typeof err === 'bigint') return String(err);
  const message = typeof err.message === 'string' ? err.message.trim() : '';
  if (message) return message;
  const asString = String(err);
  return asString && asString !== '[object Object]' ? asString : 'unknown';
}

async function prepareInputs(image, prompt) {
  const messages = [{ role: 'user', content: [{ type: 'image' }, { type: 'text', text: prompt }] }];
  const text = _processor.apply_chat_template(messages, { add_generation_prompt: true });
  // SmolVLM otherwise tiles the frame into up to 16 crops plus a global view — hundreds of image
  // tokens for a one-sentence caption. One global view is enough.
  return _processor(text, [image], { do_image_splitting: false });
}

function decodeGenerated(generatedIds, inputs) {
  const newTokenIds = generatedIds.slice(null, [inputs.input_ids.dims[1], null]);
  let output = _processor.batch_decode(newTokenIds, { skip_special_tokens: true })[0].trim();

  if (output.length === 0) {
    // Some backends echo the whole conversation; recover the assistant's reply from the full decode.
    try {
      const full = (_processor.batch_decode(generatedIds, { skip_special_tokens: true })[0] ?? '').trim();
      const split = full.split(/Assistant:\s*/i);
      if (split.length > 1) output = split[split.length - 1].trim();
    } catch {
      // Nothing to recover.
    }
  }
  return output;
}

function nextRuntimeFallback() {
  if (_runtimeFallbackStage === 0 && _device === 'webgpu' && _dtype !== _MODEL.webgpuDtypeFallback) {
    return { device: 'webgpu', dtype: _MODEL.webgpuDtypeFallback };
  }
  // A wasm session created as a fallback from a failed WebGPU run can still be unusable.
  // Reload it once with freshly prepared tensors before giving up.
  if (_runtimeFallbackStage <= 2) return { device: 'wasm', dtype: _MODEL.wasmDtype };
  return null;
}

function unavailable(status, rawOutput = '') {
  return { isAvailable: false, status, rawOutput, activity: 'Unavailable', caption: '' };
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
    // "Failed to fetch" reads as an app bug rather than what it is — the weights could not be downloaded.
    const detail = describeError(err);
    return unavailable(/failed to fetch|networkerror|network error|load failed/i.test(detail)
      ? `Model unavailable: could not download model weights after ${FETCH_ATTEMPTS} attempts — check the network connection to the model host`
      : `Model unavailable: ${detail}`);
  }

  const inferStart = performance.now();
  const safeMaxNewTokens = Number.isFinite(maxNewTokens)
    ? Math.min(96, Math.max(16, Math.trunc(maxNewTokens)))
    : 32;

  // Frames arrive as a transferred ImageBitmap: no JPEG encode on the page, no base64 copy, no decode here.
  const { image } = bitmapToRawImage(_RawImage, bitmap);
  bitmap.close?.();
  // Rebuild tensors after every backend switch: reusing WebGPU inputs on wasm is how generate()
  // started throwing a bare ONNX code with no .message.
  let inputs = await prepareInputs(image, prompt);
  let generateError = null;
  let output = '';

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
      output = decodeGenerated(generatedIds, inputs);
      if (output.length > 0) break;
    }

    const fallback = nextRuntimeFallback();
    if (!fallback) break;

    _runtimeFallbackStage += 1;
    try {
      await loadOn(fallback.device, fallback.dtype);
      _fp16FallbackUsed = fallback.device === 'webgpu';
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
    return unavailable(generateError
      ? `Inference error: ${generateError}`
      : (_runtimeFallbackStage > 0 ? `Model returned an empty response (retried on ${_device}/${_dtype})` : 'Model returned an empty response'));
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

// Each request carries a unique `id` so the bridge can match responses to the awaiting Promise.
self.onmessage = async (e) => {
  const { id, type, payload } = e.data;

  switch (type) {
    case 'RUN_INFERENCE': {
      const run = _inferLock.then(() => runInference(payload.frame, payload.prompt, payload.maxNewTokens));
      _inferLock = run.then(() => undefined, () => undefined);
      let result;
      try {
        result = await run;
      } catch (err) {
        result = unavailable(`Inference error: ${describeError(err)}`);
      }
      self.postMessage({ id, result });
      break;
    }

    case 'GET_DIAGNOSTICS': {
      self.postMessage({
        id,
        result: {
          modelId: _MODEL.id,
          loadState: _loadState,
          loadError: _loadError,
          device: _device,
          dtype: _dtype,
          fp16FallbackUsed: _fp16FallbackUsed,
          loadDurationMs: _loadDurationMs,
          inferenceCount: _inferenceCount,
          lastInferenceMs: _lastInferenceMs,
          lastInferenceTimestamp: _lastInferenceTimestamp,
          lastInferenceOutput: _lastInferenceOutput,
          webGpuPresent: !!navigator.gpu,
        },
      });
      break;
    }
  }
};
