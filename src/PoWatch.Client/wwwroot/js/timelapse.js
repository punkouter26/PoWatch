// On-device time-lapse. While a camera session runs, the pixel layer hands over one frame a
// minute; it is stored as a small JPEG in IndexedDB under its local day and never leaves the
// browser. A day plays back as a flipbook on a canvas, recorded as it plays with the native
// MediaRecorder into a WebM the viewer can save. Frames older than KEEP_DAYS are pruned.
(function () {
  'use strict';

  const WIDTH = 480;
  const KEEP_DAYS = 7;
  let _db = null;
  let _playing = false;
  let _videoUrl = null;

  function open() {
    return _db ??= new Promise((resolve, reject) => {
      const req = indexedDB.open('powatch-timelapse', 1);
      req.onupgradeneeded = () => req.result.createObjectStore('frames', { autoIncrement: true }).createIndex('day', 'day');
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => { _db = null; reject(req.error); };
    });
  }

  async function run(mode, work) {
    const db = await open();
    return new Promise((resolve, reject) => {
      const tx = db.transaction('frames', mode);
      const req = work(tx.objectStore('frames'));
      tx.oncomplete = () => resolve(req?.result);
      tx.onerror = () => reject(tx.error);
    });
  }

  /** The local calendar day as yyyy-MM-dd, the same key the History page uses. */
  function localDay(date) {
    const pad = (n) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  }

  /** Saves the current frame of `video` and prunes days past the keep window. Best-effort. */
  async function capture(video) {
    if (!video || video.readyState < 2 || !video.videoWidth) return;
    const canvas = document.createElement('canvas');
    canvas.width = Math.min(WIDTH, video.videoWidth);
    canvas.height = Math.round(canvas.width * video.videoHeight / video.videoWidth);
    canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
    const blob = await new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.7));
    if (!blob) return;

    const now = new Date();
    const cutoff = localDay(new Date(now.getFullYear(), now.getMonth(), now.getDate() - KEEP_DAYS + 1));
    try {
      await run('readwrite', (store) => store.add({ day: localDay(now), at: now.getTime(), blob }));
      await run('readwrite', (store) => {
        store.index('day').openCursor(IDBKeyRange.upperBound(cutoff, true)).onsuccess = (e) => {
          const cursor = e.target.result;
          if (cursor) { cursor.delete(); cursor.continue(); }
        };
      });
    } catch { /* storage full or blocked: the time-lapse is an extra, sensing carries on */ }
  }

  /** How many frames this device holds for a day (yyyy-MM-dd). */
  async function count(day) {
    try {
      return await run('readonly', (store) => store.index('day').count(day));
    } catch {
      return 0;
    }
  }

  /**
   * Plays a day's frames onto `canvas` at `fps`, stamping each with its time, and records the
   * playback. Resolves with an object URL for the WebM (null when nothing to play, already
   * playing, or MediaRecorder is unavailable — the flipbook still plays in that case).
   */
  async function play(canvas, day, fps) {
    if (_playing || !canvas) return null;
    const frames = (await run('readonly', (store) => store.index('day').getAll(day))).sort((a, b) => a.at - b.at);
    if (frames.length === 0) return null;
    _playing = true;

    try {
      const first = await createImageBitmap(frames[0].blob);
      canvas.width = first.width;
      canvas.height = first.height;
      first.close();
      const ctx = canvas.getContext('2d');

      const type = ['video/webm;codecs=vp9', 'video/webm'].find((t) => window.MediaRecorder?.isTypeSupported(t));
      const recorder = type ? new MediaRecorder(canvas.captureStream(fps), { mimeType: type }) : null;
      const chunks = [];
      if (recorder) {
        recorder.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
        recorder.start();
      }

      const stampSize = Math.max(12, Math.round(canvas.height / 18));
      for (const frame of frames) {
        const image = await createImageBitmap(frame.blob);
        ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
        image.close();
        const stamp = new Date(frame.at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        ctx.font = `600 ${stampSize}px ui-monospace, monospace`;
        ctx.fillStyle = 'rgba(0, 0, 0, 0.55)';
        ctx.fillRect(8, 8, ctx.measureText(stamp).width + 16, stampSize + 12);
        ctx.fillStyle = '#f5a524';
        ctx.fillText(stamp, 16, 8 + stampSize);
        await new Promise((resolve) => setTimeout(resolve, 1000 / fps));
      }

      if (!recorder) return null;
      await new Promise((resolve) => { recorder.onstop = resolve; recorder.stop(); });
      if (_videoUrl) URL.revokeObjectURL(_videoUrl);
      _videoUrl = URL.createObjectURL(new Blob(chunks, { type: 'video/webm' }));
      return _videoUrl;
    } finally {
      _playing = false;
    }
  }

  window.powatchTimelapse = { capture, count, play, keepDays: KEEP_DAYS };
})();
