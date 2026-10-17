// embed-worker.js
// Sentence embeddings off the main thread, for caption search (caption-index.js). The caption worker
// embeds its own captions; this one serves the page, so searching never loads a vision model.
// Speaks worker-rpc.js: replies echo the `id`, and a failure is a type 'ERROR' reply.
import { embed } from './embedder.js';

self.onmessage = async (e) => {
  const { id, texts } = e.data ?? {};
  try {
    self.postMessage({ id, vectors: await embed(texts ?? []) });
  } catch (err) {
    self.postMessage({ id, type: 'ERROR', message: String(err?.message ?? err) });
  }
};
