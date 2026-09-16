// IndexedDB-backed outbox for ingest requests. The store queues a copy of every
// IngestObservationRequestDto that the WASM client would have sent so that a WiFi blip or a
// server outage doesn't lose the observation. The drain loop walks the queue FIFO and posts
// each entry to the BFF; the BFF collapses retries by IdempotencyKey (see IdempotencyMiddleware).
//
// We use IndexedDB directly rather than localStorage because the V8 spec is the only Web Storage
// API that survives a tab unload and can carry JSON-typed payloads with non-trivial size.
//
// Schema:
//   store:  'inference-outbox' (keyPath: 'enqueuedAtUtc', autoIncrement: false)
//   record: { enqueuedAtUtc, idempotencyKey, payload, attemptCount, lastError }
//
// The id is the wall-clock timestamp of when the queue entry was created; this gives FIFO
// ordering by construction.

(function () {
    'use strict';

    const DB_NAME = 'powatch-outbox';
    const DB_VERSION = 1;
    const STORE_NAME = 'inference-outbox';

    let dbPromise = null;

    function openDb() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise((resolve, reject) => {
            if (typeof indexedDB === 'undefined') {
                reject(new Error('IndexedDB is not available in this browser.'));
                return;
            }
            const request = indexedDB.open(DB_NAME, DB_VERSION);
            request.addEventListener('upgradeneeded', () => {
                const db = request.result;
                if (!db.objectStoreNames.contains(STORE_NAME)) {
                    db.createObjectStore(STORE_NAME, { keyPath: 'enqueuedAtUtc' });
                }
            });
            request.addEventListener('success', () => resolve(request.result));
            request.addEventListener('error', () => reject(request.error || new Error('IndexedDB open failed.')));
        });
        return dbPromise;
    }

    async function withStore(mode, callback) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE_NAME, mode);
            const store = tx.objectStore(STORE_NAME);
            let result;
            const safeCallback = () => {
                try { result = callback(store); }
                catch (ex) { reject(ex); }
            };
            safeCallback();
            tx.addEventListener('complete', () => resolve(result));
            tx.addEventListener('error', () => reject(tx.error || new Error('IndexedDB transaction failed.')));
            tx.addEventListener('abort', () => reject(tx.error || new Error('IndexedDB transaction aborted.')));
        });
    }

    window.powatchOutbox = {
        async enqueue(payload, idempotencyKey) {
            // FIFO ordering: serial-tick timestamp as the key. Two consecutive enqueues inside
            // the same millisecond would collide, so we fall back to a small monotonic suffix
            // when needed.
            const base = Date.now();
            let key = base;
            const existing = await withStore('readonly', (store) => store.get(base));
            if (existing) {
                key = base + Math.floor(Math.random() * 1000) + 1;
            }

            const record = {
                enqueuedAtUtc: key,
                idempotencyKey: idempotencyKey || key.toString(),
                payload,
                attemptCount: 0,
                lastError: null
            };

            await withStore('readwrite', (store) => store.add(record));
            return record.enqueuedAtUtc;
        },

        async dequeue(enqueuedAtUtc) {
            await withStore('readwrite', (store) => store.delete(enqueuedAtUtc));
        },

        async markFailure(enqueuedAtUtc, errorMessage) {
            const db = await openDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction(STORE_NAME, 'readwrite');
                const store = tx.objectStore(STORE_NAME);
                const get = store.get(enqueuedAtUtc);
                get.addEventListener('success', () => {
                    const record = get.result;
                    if (!record) { resolve(); return; }
                    record.attemptCount = (record.attemptCount || 0) + 1;
                    record.lastError = errorMessage || 'unknown';
                    const put = store.put(record);
                    put.addEventListener('success', () => resolve());
                    put.addEventListener('error', () => reject(put.error));
                });
                get.addEventListener('error', () => reject(get.error));
            });
        },

        async listAll() {
            const db = await openDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction(STORE_NAME, 'readonly');
                const store = tx.objectStore(STORE_NAME);
                const records = [];
                const cursorRequest = store.openCursor();
                cursorRequest.addEventListener('success', () => {
                    const cursor = cursorRequest.result;
                    if (cursor) {
                        records.push(cursor.value);
                        cursor.continue();
                    }
                });
                tx.addEventListener('complete', () => resolve(records));
                tx.addEventListener('error', () => reject(tx.error));
            });
        },

        async clear() {
            await withStore('readwrite', (store) => store.clear());
        }
    };
})();
