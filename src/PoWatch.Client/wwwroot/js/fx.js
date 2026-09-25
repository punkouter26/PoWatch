// Comet trails over the camera, accumulated into a long-exposure print per session. Driven by the
// tracks FxBridge.razor pushes about four times a second. Honours prefers-reduced-motion.
(function () {
  'use strict';

  const TRAIL_MS = 8000;
  const EXPO_W = 640;
  const EXPO_H = 360;
  const AMBER = '#f5a524';
  const CYAN = '#38bdf8';
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');

  let running = false;
  const trails = new Map();    // trackId -> { label, points: [{x, y, t}], hx, hy, alive }
  const colorFor = (label) => (label === 'person' ? AMBER : CYAN);

  // ── Frames from .NET ─────────────────────────────────────────────────────

  function update(json) {
    let next;
    try { next = JSON.parse(json); } catch { return; }
    if (next.running && !running) {
      trails.clear();
      expo?.clearRect(0, 0, EXPO_W, EXPO_H);
      expoStrokes = 0;
    }
    running = next.running;

    const now = performance.now();
    for (const t of next.tracks) {
      let trail = trails.get(t.id);
      if (!trail) {
        trail = { label: t.label, points: [], hx: t.x, hy: t.y, alive: now };
        trails.set(t.id, trail);
      }
      trail.alive = now;
      const last = trail.points.at(-1);
      if (!last || Math.hypot(last.x - t.x, last.y - t.y) > 0.002) {
        if (last) expose(last, t, trail.label);
        trail.points.push({ x: t.x, y: t.y, t: now });
      }
    }
    if (!raf) raf = requestAnimationFrame(loop);
  }

  // ── Long exposure ────────────────────────────────────────────────────────

  let expo = null;
  let expoStrokes = 0;

  function expose(from, to, label) {
    if (!expo) {
      const c = document.createElement('canvas');
      c.width = EXPO_W;
      c.height = EXPO_H;
      expo = c.getContext('2d');
    }
    expo.globalCompositeOperation = 'lighter';
    expo.strokeStyle = colorFor(label);
    expo.globalAlpha = 0.35;
    expo.lineWidth = 3;
    expo.lineCap = 'round';
    expo.shadowColor = colorFor(label);
    expo.shadowBlur = 10;
    expo.beginPath();
    expo.moveTo(from.x * EXPO_W, from.y * EXPO_H);
    expo.lineTo(to.x * EXPO_W, to.y * EXPO_H);
    expo.stroke();
    expoStrokes++;
  }

  /** The session's paths over a dimmed frame of the room, as a PNG data URL; null if nobody moved. */
  function exposure(video) {
    if (!expo || !expoStrokes) return null;
    const out = document.createElement('canvas');
    out.width = EXPO_W;
    out.height = EXPO_H;
    const c = out.getContext('2d');
    c.fillStyle = '#05070a';
    c.fillRect(0, 0, EXPO_W, EXPO_H);
    if (video && video.readyState >= 2) {
      c.globalAlpha = 0.3;
      c.filter = 'grayscale(1)';
      c.drawImage(video, 0, 0, EXPO_W, EXPO_H);
      c.filter = 'none';
      c.globalAlpha = 1;
    }
    c.globalCompositeOperation = 'lighter';
    c.drawImage(expo.canvas, 0, 0);
    c.globalCompositeOperation = 'source-over';
    c.font = '11px monospace';
    c.fillStyle = 'rgba(255, 255, 255, 0.6)';
    c.fillText(`POWATCH · LONG EXPOSURE · ${new Date().toLocaleString()}`, 10, EXPO_H - 10);
    return out.toDataURL('image/png');
  }

  // ── Drawing loop ─────────────────────────────────────────────────────────

  let raf = 0;
  let lastDraw = 0;

  function loop(now) {
    raf = 0;
    const canvas = running || trails.size ? document.querySelector('[data-fx-trails]') : null;
    if (!canvas) return;
    raf = requestAnimationFrame(loop);
    // ~30 fps is plenty and leaves the GPU to the detector and the vision model.
    if (now - lastDraw < 33) return;
    lastDraw = now;
    drawTrails(canvas, now);
  }

  function drawTrails(canvas, now) {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = Math.max(1, Math.round(canvas.clientWidth * dpr));
    const h = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width !== w || canvas.height !== h) {
      canvas.width = w;
      canvas.height = h;
    }
    const ctx = canvas.getContext('2d');
    ctx.clearRect(0, 0, w, h);
    ctx.globalCompositeOperation = 'lighter';
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    for (const [id, trail] of trails) {
      trail.points = trail.points.filter((p) => now - p.t < TRAIL_MS);
      const alive = now - trail.alive < 1500;
      if (!alive && !trail.points.length) { trails.delete(id); continue; }
      const last = trail.points.at(-1);
      if (last) {
        // The head eases toward the latest centroid, so ~1 Hz detections still glide.
        const ease = reduced.matches ? 1 : 0.12;
        trail.hx += (last.x - trail.hx) * ease;
        trail.hy += (last.y - trail.hy) * ease;
      }
      const color = colorFor(trail.label);
      ctx.strokeStyle = color;
      ctx.shadowColor = color;
      ctx.shadowBlur = 10 * dpr;
      const path = [...trail.points.slice(0, -1), { x: trail.hx, y: trail.hy, t: now }];
      for (let i = 1; i < path.length; i++) {
        const age = (now - path[i - 1].t) / TRAIL_MS;
        ctx.globalAlpha = Math.max(0, 1 - age) * 0.9;
        ctx.lineWidth = (1 + 3 * (1 - age)) * dpr;
        ctx.beginPath();
        ctx.moveTo(path[i - 1].x * w, path[i - 1].y * h);
        ctx.lineTo(path[i].x * w, path[i].y * h);
        ctx.stroke();
      }
      if (alive) {
        ctx.globalAlpha = 1;
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(trail.hx * w, trail.hy * h, 4 * dpr, 0, Math.PI * 2);
        ctx.fill();
      }
    }
    ctx.globalAlpha = 1;
    ctx.shadowBlur = 0;
  }

  window.powatchFx = { update, exposure };
})();
