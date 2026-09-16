// MediaRecorder bridge for handoff voice memos. Exposes three operations on window.powatchMemoRecorder:
//   - start():  acquire the microphone and begin recording. Throws if permission is denied or no
//               recorder can be constructed for the current browser.
//   - stop():   stop the recorder and return a Blob (the recorded bytes), the MIME type, and the
//               elapsed milliseconds.
//   - cancel(): abort the in-progress recording and discard the captured data.
//
// The browser is the source of truth for the audio format. Chrome/Edge default to audio/webm with
// Opus at 32 kbps (~120 KB for 30 s); Firefox uses audio/ogg; Safari uses audio/mp4. The recorded
// MIME is reported back to the caller so the upload picks the matching multipart Content-Type, and
// the playback <audio> element on the receiving side can render whatever the original captured.
//
// This module is intentionally framework-agnostic. The Razor component binds to these globals
// and is responsible for translating JS exceptions into user-visible messages.

(function () {
    'use strict';

    if (window.powatchMemoRecorder) return;

    let recorder = null;
    let chunks = [];
    let startedAtMs = null;
    let pendingStop = null;

    function pickMimeType() {
        const candidates = [
            'audio/webm;codecs=opus',
            'audio/webm',
            'audio/ogg;codecs=opus',
            'audio/mp4'
        ];
        if (typeof MediaRecorder === 'undefined') return null;
        for (const candidate of candidates) {
            if (MediaRecorder.isTypeSupported(candidate)) return candidate;
        }
        return '';
    }

    function ensureNotRecording() {
        if (recorder && recorder.state !== 'inactive') {
            throw new Error('A recording is already in progress.');
        }
    }

    window.powatchMemoRecorder = {
        isSupported: () => typeof MediaRecorder !== 'undefined' && !!navigator.mediaDevices?.getUserMedia,

        async start() {
            ensureNotRecording();
            if (!this.isSupported()) {
                throw new Error('Audio recording is not supported in this browser.');
            }
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            const mimeType = pickMimeType();
            recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
            chunks = [];
            startedAtMs = performance.now();

            recorder.addEventListener('dataavailable', (event) => {
                if (event.data && event.data.size > 0) chunks.push(event.data);
            });

            const stopped = new Promise((resolve, reject) => {
                recorder.addEventListener('stop', () => {
                    const elapsedMs = Math.max(0, Math.round(performance.now() - startedAtMs));
                    const blob = new Blob(chunks, { type: recorder.mimeType || 'audio/webm' });
                    // Stop the mic tracks; otherwise the OS keeps the recording indicator on.
                    stream.getTracks().forEach((track) => track.stop());
                    resolve({ blob, mimeType: blob.type, durationMs: elapsedMs });
                });
                recorder.addEventListener('error', (event) => {
                    stream.getTracks().forEach((track) => track.stop());
                    reject(event.error || new Error('MediaRecorder error.'));
                });
            });
            pendingStop = stopped;

            recorder.start();
        },

        async stop() {
            if (!recorder || recorder.state === 'inactive') {
                throw new Error('No recording in progress.');
            }
            recorder.stop();
            const result = await pendingStop;
            pendingStop = null;
            recorder = null;
            return result;
        },

        cancel() {
            if (recorder && recorder.state !== 'inactive') {
                recorder.stop();
                chunks = [];
            }
            recorder = null;
            pendingStop = null;
            startedAtMs = null;
        }
    };

    // Helper exposed on the same global so the .NET side can pull bytes back from a Blob URL.
    window.powatchMemoRecorder.revoke = function (url) {
        if (url && url.startsWith('blob:')) URL.revokeObjectURL(url);
    };

    window.powatchMemoRecorder.readAsBase64 = async function (url) {
        const response = await fetch(url);
        const buffer = await response.arrayBuffer();
        let binary = '';
        const bytes = new Uint8Array(buffer);
        const chunkSize = 0x8000;
        for (let i = 0; i < bytes.length; i += chunkSize) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
        }
        return btoa(binary);
    };
})();
