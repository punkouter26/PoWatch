// Request/response over a module Web Worker, shared by the detector and caption bridges.
// Each message carries an `id` the worker echoes back; a reply with type 'ERROR' rejects.
// The worker starts on first use and is recreated after a crash.
(function () {
  'use strict';

  window.powatchWorkerRpc = function (url) {
    let worker = null;
    let nextId = 1;
    const pending = new Map();

    function ensureWorker() {
      if (worker) return worker;
      worker = new Worker(url, { type: 'module' });
      worker.onmessage = (event) => {
        const entry = pending.get(event.data.id);
        if (!entry) return;
        pending.delete(event.data.id);
        if (event.data.type === 'ERROR') entry.reject(new Error(event.data.message));
        else entry.resolve(event.data);
      };
      worker.onerror = () => {
        for (const { reject } of pending.values()) reject(new Error('Worker crashed'));
        pending.clear();
        worker = null;
      };
      return worker;
    }

    /** Posts `message`; resolves to the worker's reply. timeoutMs = 0 waits forever (model loads are slow). */
    return function call(message, transfer = [], timeoutMs = 0) {
      const id = nextId++;
      return new Promise((resolve, reject) => {
        let timer = null;
        if (timeoutMs > 0) {
          timer = setTimeout(() => {
            if (pending.delete(id)) reject(new Error(`Worker did not answer ${message.type} within ${timeoutMs}ms`));
          }, timeoutMs);
        }
        pending.set(id, {
          resolve: (v) => { clearTimeout(timer); resolve(v); },
          reject: (e) => { clearTimeout(timer); reject(e); },
        });
        ensureWorker().postMessage({ ...message, id }, transfer);
      });
    };
  };
})();
