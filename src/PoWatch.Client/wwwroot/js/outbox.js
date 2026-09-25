// Durable outbox: ingest batches the server has not acknowledged yet, kept in IndexedDB so a
// reload, a crash or a browser restart does not lose them. SensingSession owns the retry logic;
// this file only stores. Batches travel as JSON text so .NET parses them with its
// source-generated context (trim-safe).
(function () {
  'use strict';

  const MAX = 360; // an hour of ten-second batches, the same cap as TickBatcher
  let _db = null;

  function open() {
    return _db ??= new Promise((resolve, reject) => {
      const req = indexedDB.open('powatch-outbox', 1);
      req.onupgradeneeded = () => req.result.createObjectStore('batches', { keyPath: 'key' }).createIndex('at', 'at');
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => { _db = null; reject(req.error); };
    });
  }

  /** Runs `work` in one transaction and resolves with its request's result once committed. */
  async function run(mode, work) {
    const db = await open();
    return new Promise((resolve, reject) => {
      const tx = db.transaction('batches', mode);
      const req = work(tx.objectStore('batches'));
      tx.oncomplete = () => resolve(req?.result);
      tx.onerror = () => reject(tx.error);
    });
  }

  /** Stores a batch; past the cap the oldest go, as in the in-memory queue. */
  async function put(key, sessionId, json) {
    await run('readwrite', (store) => store.put({ key, sessionId, json, at: Date.now() }));
    const extra = (await run('readonly', (store) => store.count())) - MAX;
    if (extra <= 0) return;
    await run('readwrite', (store) => {
      let left = extra;
      store.index('at').openCursor().onsuccess = (e) => {
        const cursor = e.target.result;
        if (cursor && left-- > 0) { cursor.delete(); cursor.continue(); }
      };
    });
  }

  const remove = (key) => run('readwrite', (store) => store.delete(key));

  /** Session ids that still have batches waiting. */
  async function sessions() {
    const all = await run('readonly', (store) => store.getAll());
    return [...new Set(all.map((b) => b.sessionId))];
  }

  /** One session's waiting batches as JSON text, oldest first. */
  async function batches(sessionId) {
    const all = await run('readonly', (store) => store.getAll());
    return all.filter((b) => b.sessionId === sessionId).sort((a, b) => a.at - b.at).map((b) => b.json);
  }

  // A tab closed or reloaded mid-session never reaches Stop, and the server would list that session as
  // running forever. End it on the way out; its unsent batches still replay from this outbox later.
  let running = null;
  addEventListener('pagehide', () => { if (running) navigator.sendBeacon(`api/sessions/${running}/stop`); });
  const guard = (sessionId) => { running = sessionId; };
  const unguard = () => { running = null; };

  window.powatchOutbox = { put, remove, sessions, batches, guard, unguard };
})();
