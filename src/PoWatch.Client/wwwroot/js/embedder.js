// embedder.js
// Sentence embeddings for captions (all-MiniLM-L6-v2, int8, ~23 MB, 384 dimensions). Used for
// "unlike anything lately" novelty (HighlightRules) and for search by meaning (caption-index.js).
// It runs on WASM on purpose: a few milliseconds a sentence, and the GPU stays with the detector and
// the caption model. Imported by inference-worker.js and embed-worker.js.

import { loadTransformers } from './transformers-env.js';

const MODEL = 'Xenova/all-MiniLM-L6-v2';
let _pipe = null;

function pipe() {
  return _pipe ??= loadTransformers()
    .then((t) => t.pipeline('feature-extraction', MODEL, { device: 'wasm', dtype: 'q8' }))
    .catch((err) => { _pipe = null; throw err; });
}

/** Unit-length vectors, one plain array per text (so they cross postMessage and JS interop as JSON). */
export async function embed(texts) {
  if (texts.length === 0) return [];
  const extractor = await pipe();
  const output = await extractor(texts, { pooling: 'mean', normalize: true });
  const [rows, dims] = output.dims;
  const data = output.data;
  const vectors = [];
  for (let r = 0; r < rows; r++) {
    vectors.push(Array.from(data.subarray(r * dims, (r + 1) * dims), (v) => Math.round(v * 1e5) / 1e5));
  }
  return vectors;
}

/** The same, or null when the model cannot be loaded — callers carry on without embeddings. */
export async function tryEmbed(texts) {
  try {
    return await embed(texts);
  } catch {
    return null;
  }
}
