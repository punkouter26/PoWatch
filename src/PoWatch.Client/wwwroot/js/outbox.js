// The outbox: every ingest batch, from every session, waits here until the server answers for it.
// It is the only send queue (SensingSession posts from it, oldest first), and it is durable: kept in
// IndexedDB, so a reload, a crash or a browser restart loses nothing. Where IndexedDB is blocked
// (private windows, locked-down storage) it falls back to memory for the life of the page. Batches
// travel as JSON text so .NET parses them with its source-generated context (trim-safe).
(function () {
  'use strict';

  const MAX = 360; // an hour of ten-second batches; past it the oldest go and their ticks count as lost
  let _db = null;
  let _memory = null; // Map key -> record, once IndexedDB has failed

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

  /** Uses IndexedDB until it fails once, then memory: a batch is never dropped because storage is blocked. */
  async function store(idb, memory) {
    if (!_memory) {
      try { return await idb(); } catch { _memory = new Map(); }
    }
    return memory(_memory);
  }

  const all = () => store(() => run('readonly', (s) => s.getAll()), (m) => [...m.values()]);
  const add = (record) => store(() => run('readwrite', (s) => s.put(record)), (m) => { m.set(record.key, record); });
  const remove = (key) => store(() => run('readwrite', (s) => s.delete(key)), (m) => { m.delete(key); });

  const ticksIn = (json) => { try { return JSON.parse(json).ticks?.length ?? 0; } catch { return 0; } };

  /** Stores a batch; past the cap the oldest go. Resolves with how many ticks were dropped. */
  async function put(key, sessionId, json) {
    await add({ key, sessionId, json, at: Date.now() });
    const records = await all();
    if (records.length <= MAX) return 0;
    let lost = 0;
    for (const old of records.sort((a, b) => a.at - b.at).slice(0, records.length - MAX)) {
      lost += ticksIn(old.json);
      await remove(old.key);
    }
    return lost;
  }

  /** Every waiting batch, oldest first, as JSON text: [{ key, sessionId, json }]. */
  async function waiting() {
    const records = await all();
    return JSON.stringify(records.sort((a, b) => a.at - b.at).map((r) => ({ key: r.key, sessionId: r.sessionId, json: r.json })));
  }

  // A tab closed or reloaded mid-session never reaches Stop, and the server would list that session as
  // running forever. End it on the way out; its unsent batches still go out from this outbox later.
  let running = null;
  addEventListener('pagehide', () => { if (running) navigator.sendBeacon(`api/sessions/${running}/stop`); });
  const guard = (sessionId) => { running = sessionId; };
  const unguard = () => { running = null; };

  window.powatchOutbox = { put, remove, waiting, guard, unguard };
})();
