// Scene effects, all driven by numbers the session already produces (FxBridge.razor pushes a frame
// about four times a second):
//   sound        a drone whose pitch is the room's light, filter is its motion and chord is who's in
//                frame; a pluck panned to where someone walks in; a detuned blip on an unusual stat;
//                teletype clicks as a caption types out
//   announcer    reads captions and arrivals aloud (speechSynthesis)
//   heat shader  panel 06's 16×9 motion grid as a thermal image with afterimage (WebGL2)
//   trails       comet trails over the camera, accumulated into a long-exposure print per session
//   transitions  panels morph between sections (View Transitions API)
//   terrain      the wall's hour×weekday occupancy as a slowly swaying 3D height field (WebGL2)
// Sound and the announcer stay off until switched on in the header. Motion effects honour
// prefers-reduced-motion. Canvases sit over the plain HTML heat grids, which show if WebGL2 is missing.
(function () {
  'use strict';

  const PREFS_KEY = 'powatch.fx';
  const TYPE_MS = 30;          // per caption character; matches .fx-type in terminal.css
  const TRAIL_MS = 8000;
  const EXPO_W = 640;
  const EXPO_H = 360;
  const AMBER = '#f5a524';
  const CYAN = '#38bdf8';
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');

  const prefs = { sound: false, announcer: false };
  try { Object.assign(prefs, JSON.parse(localStorage.getItem(PREFS_KEY) || '{}')); } catch { /* private mode */ }

  let frame = { running: false, motion: 0, luminance: 0, present: 0, heat: [], palette: [], tracks: [], caption: null, captionAt: 0 };
  let lastCaptionAt = null;
  const trails = new Map();    // trackId -> { label, points: [{x, y, t}], hx, hy, alive }

  const clamp = (v) => Math.min(1, Math.max(0, v || 0));
  const colorFor = (label) => (label === 'person' ? AMBER : CYAN);

  // ── Sound ────────────────────────────────────────────────────────────────

  let ac = null;
  let master = null;
  let drone = null;
  let noise = null;
  let lastBlip = 0;

  function audio() {
    if (!prefs.sound) return null;
    if (!ac) {
      const Ctx = window.AudioContext || window.webkitAudioContext;
      if (!Ctx) return null;
      ac = new Ctx();
      master = ac.createGain();
      master.gain.value = 0.5;
      master.connect(ac.destination);
    }
    if (ac.state === 'suspended') ac.resume().catch(() => { });
    return ac;
  }

  function tone(ctx, { type = 'sine', freq, gain = 0.2, at = 0, dur = 0.3, pan = 0 }) {
    const t = ctx.currentTime + at;
    const osc = ctx.createOscillator();
    const g = ctx.createGain();
    const p = ctx.createStereoPanner();
    osc.type = type;
    osc.frequency.value = freq;
    p.pan.value = pan;
    g.gain.setValueAtTime(gain, t);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    osc.connect(g).connect(p).connect(master);
    osc.start(t);
    osc.stop(t + dur + 0.05);
  }

  function click(ctx, at) {
    if (!noise) {
      noise = ctx.createBuffer(1, Math.round(ctx.sampleRate * 0.02), ctx.sampleRate);
      const d = noise.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / d.length);
    }
    const src = ctx.createBufferSource();
    const f = ctx.createBiquadFilter();
    const g = ctx.createGain();
    src.buffer = noise;
    f.type = 'bandpass';
    f.frequency.value = 2500 + Math.random() * 1500;
    g.gain.value = 0.12;
    src.connect(f).connect(g).connect(master);
    src.start(ctx.currentTime + at);
  }

  function startDrone(ctx) {
    const filter = ctx.createBiquadFilter();
    const gain = ctx.createGain();
    filter.type = 'lowpass';
    filter.frequency.value = 300;
    filter.Q.value = 4;
    gain.gain.value = 0;
    gain.gain.setTargetAtTime(0.05, ctx.currentTime, 1.5);
    filter.connect(gain).connect(master);
    // Root, fifth, octave: the fifth joins with one visitor in frame, the octave with two.
    const voices = [1, 1.5, 2].map((ratio, i) => {
      const osc = ctx.createOscillator();
      const g = ctx.createGain();
      osc.type = i ? 'triangle' : 'sawtooth';
      g.gain.value = i ? 0 : 0.6;
      osc.connect(g).connect(filter);
      osc.start();
      return { osc, g, ratio };
    });
    return { filter, gain, voices };
  }

  function stopDrone() {
    if (!drone) return;
    const d = drone;
    drone = null;
    d.gain.gain.setTargetAtTime(0, ac.currentTime, 0.4);
    setTimeout(() => d.voices.forEach((v) => v.osc.stop()), 2500);
  }

  function steerDrone() {
    const ctx = frame.running ? audio() : null;
    if (!ctx) { stopDrone(); return; }
    drone ??= startDrone(ctx);
    const t = ctx.currentTime;
    const root = 55 * Math.pow(2, 2 * clamp(frame.luminance));   // A1 in the dark, A3 in full light
    drone.voices.forEach((v, i) => {
      v.osc.frequency.setTargetAtTime(root * v.ratio, t, 0.8);
      v.g.gain.setTargetAtTime(i === 0 || frame.present >= i ? 0.6 / (1 + i * 0.5) : 0, t, 1);
    });
    drone.filter.frequency.setTargetAtTime(250 + 6000 * clamp(frame.motion * 4), t, 0.3);
  }

  const PENTATONIC = [0, 3, 5, 7, 10, 12];

  function pluck(x, label) {
    const ctx = audio();
    if (!ctx) return;
    let hash = 0;
    for (const ch of label) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
    const freq = 220 * Math.pow(2, PENTATONIC[hash % PENTATONIC.length] / 12);
    const pan = clamp(x) * 2 - 1;
    tone(ctx, { type: 'triangle', freq, gain: 0.25, dur: 1.2, pan });
    tone(ctx, { type: 'sine', freq: freq * 2, gain: 0.08, at: 0.06, dur: 0.8, pan });
  }

  /** An unusual stat (|z| ≥ 2) just appeared: a short detuned blip, at most one every 20 s. */
  function blip() {
    const ctx = audio();
    if (!ctx || performance.now() - lastBlip < 20000) return;
    lastBlip = performance.now();
    tone(ctx, { type: 'square', freq: 440, gain: 0.05, dur: 0.15 });
    tone(ctx, { type: 'square', freq: 452, gain: 0.05, dur: 0.15 });
    tone(ctx, { type: 'square', freq: 415, gain: 0.05, at: 0.12, dur: 0.2 });
    tone(ctx, { type: 'square', freq: 427, gain: 0.05, at: 0.12, dur: 0.2 });
  }

  function say(text) {
    // Skip rather than queue: a backlog of stale announcements is worse than a missed one.
    if (!prefs.announcer || !window.speechSynthesis || speechSynthesis.pending) return;
    const u = new SpeechSynthesisUtterance(text);
    u.rate = 0.95;
    u.pitch = 0.9;
    speechSynthesis.speak(u);
  }

  function edgeOf(x, y) {
    const d = [[x, 'left'], [1 - x, 'right'], [y, 'top'], [1 - y, 'bottom']];
    return d.sort((a, b) => a[0] - b[0])[0][1];
  }

  function entered(track) {
    pluck(track.x, track.label);
    say(`${/^[aeiou]/i.test(track.label) ? 'An' : 'A'} ${track.label} entered from the ${edgeOf(track.x, track.y)}.`);
  }

  function captioned(text) {
    const ctx = audio();
    if (ctx) {
      const chars = Math.min(text.length, 160);
      for (let i = 0; i < chars; i++) if (text[i] !== ' ') click(ctx, (i * TYPE_MS) / 1000);
    }
    say(text);
  }

  // ── Frames from .NET ─────────────────────────────────────────────────────

  function resetSession() {
    trails.clear();
    expo?.clearRect(0, 0, EXPO_W, EXPO_H);
    expoStrokes = 0;
  }

  function update(json) {
    let next;
    try { next = JSON.parse(json); } catch { return; }
    if (next.running && !frame.running) resetSession();
    frame = next;

    const now = performance.now();
    for (const t of next.tracks) {
      let trail = trails.get(t.id);
      if (!trail) {
        trail = { label: t.label, points: [], hx: t.x, hy: t.y, alive: now };
        trails.set(t.id, trail);
        entered(t);
      }
      trail.alive = now;
      const last = trail.points.at(-1);
      if (!last || Math.hypot(last.x - t.x, last.y - t.y) > 0.002) {
        if (last) expose(last, t, trail.label);
        trail.points.push({ x: t.x, y: t.y, t: now });
      }
    }

    if (next.captionAt !== lastCaptionAt) {
      if (lastCaptionAt !== null && next.caption) captioned(next.caption);
      lastCaptionAt = next.captionAt;
    }

    steerDrone();
    wake();
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

  function wake() {
    if (!raf) raf = requestAnimationFrame(loop);
  }

  function loop(now) {
    raf = 0;
    const heat = frame.running ? document.querySelector('[data-fx-heat]') : null;
    const trail = frame.running || trails.size ? document.querySelector('[data-fx-trails]') : null;
    const land = terrain?.canvas.isConnected ? terrain : null;
    if (!heat && !trail && !land) return;
    raf = requestAnimationFrame(loop);
    // ~30 fps is plenty for these and leaves the GPU to the detector and the vision model.
    if (now - lastDraw < 33) return;
    lastDraw = now;
    if (heat) drawHeat(heat, now);
    if (trail) drawTrails(trail, now);
    if (land) drawTerrain(land, now);
  }

  function fit(canvas) {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = Math.max(1, Math.round(canvas.clientWidth * dpr));
    const h = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width !== w || canvas.height !== h) {
      canvas.width = w;
      canvas.height = h;
    }
    return dpr;
  }

  function drawTrails(canvas, now) {
    const dpr = fit(canvas);
    const ctx = canvas.getContext('2d');
    const w = canvas.width;
    const h = canvas.height;
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

  // ── WebGL2 helpers ───────────────────────────────────────────────────────

  function program(gl, vs, fs) {
    const p = gl.createProgram();
    for (const [type, src] of [[gl.VERTEX_SHADER, vs], [gl.FRAGMENT_SHADER, fs]]) {
      const s = gl.createShader(type);
      gl.shaderSource(s, src);
      gl.compileShader(s);
      gl.attachShader(p, s);
    }
    gl.linkProgram(p);
    if (gl.getProgramParameter(p, gl.LINK_STATUS)) return p;
    console.warn('fx shader', gl.getProgramInfoLog(p));
    return null;
  }

  /** WebGL2 and a linked program for a canvas laid over an HTML fallback; the fallback fades only once both work. */
  function glFor(canvas, vs, fs) {
    const gl = canvas.getContext('webgl2', { premultipliedAlpha: false, antialias: true });
    const prog = gl && program(gl, vs, fs);
    if (!prog) { canvas.hidden = true; return null; }
    canvas.parentElement?.classList.add('fx-on');
    return { gl, prog };
  }

  function uniforms(gl, prog, names) {
    return Object.fromEntries(names.map((n) => [n, gl.getUniformLocation(prog, n)]));
  }

  // ── Motion heat shader (panel 06) ────────────────────────────────────────

  const HEAT_VS = `#version 300 es
out vec2 uv;
void main() {
  vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
  uv = vec2(p.x, 1.0 - p.y);
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

  const HEAT_FS = `#version 300 es
precision mediump float;
in vec2 uv;
out vec4 o;
uniform sampler2D heat;
uniform float t;
vec3 thermal(float v) {
  vec3 c = mix(vec3(0.02, 0.03, 0.05), vec3(0.35, 0.1, 0.6), smoothstep(0.0, 0.25, v));
  c = mix(c, vec3(0.9, 0.15, 0.2), smoothstep(0.2, 0.5, v));
  c = mix(c, vec3(0.96, 0.65, 0.14), smoothstep(0.45, 0.75, v));
  return mix(c, vec3(1.0), smoothstep(0.75, 1.0, v));
}
void main() {
  vec2 q = uv + vec2(sin(uv.y * 40.0 + t * 3.0), cos(uv.x * 30.0 + t * 2.0)) * 0.002;
  vec3 c = thermal(texture(heat, q).r);
  float grid = max(step(0.97, fract(uv.x * 16.0)), step(0.95, fract(uv.y * 9.0)));
  c += 0.05 * grid;
  c *= 0.9 + 0.1 * step(1.0, mod(gl_FragCoord.y, 3.0));
  c *= 1.0 - 0.35 * length(uv - 0.5);
  o = vec4(c, 1.0);
}`;

  let heatGl = null;

  function initHeat(canvas) {
    const { gl, prog } = glFor(canvas, HEAT_VS, HEAT_FS) ?? {};
    if (!gl) return null;
    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.R8, 16, 9, 0, gl.RED, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return { canvas, gl, prog, u: uniforms(gl, prog, ['t']), disp: new Float32Array(144), bytes: new Uint8Array(144) };
  }

  function drawHeat(canvas, now) {
    if (canvas.hidden) return;
    if (heatGl?.canvas !== canvas) heatGl = initHeat(canvas);
    if (!heatGl) return;
    const { gl, prog, u, disp, bytes } = heatGl;
    const heat = frame.heat || [];
    let max = 0.05;
    for (const v of heat) max = Math.max(max, v);
    // Rise fast, fade slowly: movement leaves an afterimage for a second or two.
    for (let i = 0; i < 144; i++) {
      const target = (heat[i] || 0) / max;
      disp[i] = target > disp[i] ? disp[i] + (target - disp[i]) * 0.35 : disp[i] * 0.985;
      bytes[i] = Math.min(255, disp[i] * 255);
    }
    fit(canvas);
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.useProgram(prog);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 16, 9, gl.RED, gl.UNSIGNED_BYTE, bytes);
    gl.uniform1f(u.t, reduced.matches ? 0 : now / 1000);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  // ── Occupancy terrain (the stats wall) ───────────────────────────────────

  const LAND_VS = `#version 300 es
in vec3 a;
uniform float t, aspect, f, pointSize;
out float hgt;
void main() {
  hgt = a.y;
  vec3 p = vec3(a.x, a.y * 0.45, a.z);
  float r = 0.45 * sin(t * 0.00012);
  p.xz = vec2(p.x * cos(r) - p.z * sin(r), p.x * sin(r) + p.z * cos(r));
  const float tilt = 0.6;
  p.yz = vec2(p.y * cos(tilt) + p.z * sin(tilt), -p.y * sin(tilt) + p.z * cos(tilt));
  p.z += 2.4;
  gl_Position = vec4(p.x * f / aspect, p.y * f - 0.2 * p.z, 0.0, p.z);
  gl_PointSize = pointSize;
}`;

  const LAND_FS = `#version 300 es
precision highp float;
in float hgt;
out vec4 o;
uniform vec3 lo, hi;
uniform float alpha, pointSize, pulse;
void main() {
  if (pointSize > 0.0) {
    float d = length(gl_PointCoord - 0.5);
    if (d > 0.5) discard;
    o = vec4(mix(hi, vec3(1.0), 0.5), (1.0 - d * 2.0) * pulse);
    return;
  }
  o = vec4(mix(lo, hi, hgt) * (0.35 + 0.9 * hgt), alpha * (0.25 + 0.75 * hgt));
}`;

  const DAYS = 7;
  const HOURS = 24;
  let terrain = null;

  function initTerrain(canvas) {
    const { gl, prog } = glFor(canvas, LAND_VS, LAND_FS) ?? {};
    if (!gl) return null;
    const lines = [];
    const tris = [];
    for (let d = 0; d < DAYS; d++) {
      for (let h = 0; h < HOURS; h++) {
        const i = d * HOURS + h;
        if (h < HOURS - 1) lines.push(i, i + 1);
        if (d < DAYS - 1) lines.push(i, i + HOURS);
        if (h < HOURS - 1 && d < DAYS - 1) tris.push(i, i + 1, i + HOURS, i + 1, i + HOURS + 1, i + HOURS);
      }
    }
    const index = (list) => {
      const b = gl.createBuffer();
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, b);
      gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array(list), gl.STATIC_DRAW);
      return { b, n: list.length };
    };
    const vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const pos = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, pos);
    const loc = gl.getAttribLocation(prog, 'a');
    gl.enableVertexAttribArray(loc);
    gl.vertexAttribPointer(loc, 3, gl.FLOAT, false, 0, 0);
    return {
      canvas, gl, prog, vao, pos,
      lines: index(lines),
      tris: index(tris),
      u: uniforms(gl, prog, ['t', 'aspect', 'f', 'pointSize', 'lo', 'hi', 'alpha', 'pulse']),
      mesh: new Float32Array(DAYS * HOURS * 3),
      disp: new Float32Array(DAYS * HOURS),
      target: new Float32Array(DAYS * HOURS),
      now: 0
    };
  }

  /** The room's most vivid colour right now, brightened; amber when the room is grey or unseen. */
  function sceneColor() {
    let best = [0.96, 0.65, 0.14];
    let score = 0.25;
    for (const c of frame.palette || []) {
      const rgb = [(c >> 16) & 255, (c >> 8) & 255, c & 255].map((v) => v / 255);
      const mx = Math.max(...rgb);
      const s = (mx - Math.min(...rgb)) * mx;
      if (s > score) { score = s; best = rgb.map((v) => v / mx); }
    }
    return best;
  }

  /** Sets (or replaces) the wall's terrain data: 7×24 occupancy values and the current cell. */
  function setTerrain(canvas, json, nowIndex) {
    if (!canvas) return;
    let values;
    try { values = JSON.parse(json); } catch { return; }
    if (terrain?.canvas !== canvas) terrain = initTerrain(canvas);
    if (!terrain) return;
    const max = Math.max(1e-9, ...values);
    for (let i = 0; i < DAYS * HOURS; i++) terrain.target[i] = (values[i] || 0) / max;
    terrain.now = nowIndex;
    wake();
  }

  function drawTerrain(land, now) {
    const { canvas, gl, prog, vao, pos, u, mesh, disp, target } = land;
    for (let d = 0; d < DAYS; d++) {
      for (let h = 0; h < HOURS; h++) {
        const i = d * HOURS + h;
        disp[i] += (target[i] - disp[i]) * (reduced.matches ? 1 : 0.04);   // grows out of the floor
        mesh.set([(h / (HOURS - 1)) * 2 - 1, disp[i], ((d / (DAYS - 1)) * 2 - 1) * 0.4], i * 3);
      }
    }
    fit(canvas);
    const aspect = canvas.width / canvas.height;
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE);
    gl.useProgram(prog);
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, pos);
    gl.bufferData(gl.ARRAY_BUFFER, mesh, gl.DYNAMIC_DRAW);
    gl.uniform1f(u.t, reduced.matches ? 0 : now);
    gl.uniform1f(u.aspect, aspect);
    gl.uniform1f(u.f, 3 * Math.min(1, aspect / 1.5));
    gl.uniform3f(u.lo, 0.1, 0.45, 0.65);
    gl.uniform3fv(u.hi, sceneColor());
    gl.uniform1f(u.pointSize, 0);

    gl.uniform1f(u.alpha, 0.35);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, land.tris.b);
    gl.drawElements(gl.TRIANGLES, land.tris.n, gl.UNSIGNED_SHORT, 0);
    gl.uniform1f(u.alpha, 0.9);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, land.lines.b);
    gl.drawElements(gl.LINES, land.lines.n, gl.UNSIGNED_SHORT, 0);

    // "You are here": the current weekday and hour, pulsing.
    const pulse = reduced.matches ? 1 : 0.6 + 0.4 * Math.sin(now / 400);
    gl.uniform1f(u.pointSize, 14 * Math.min(window.devicePixelRatio || 1, 2) * pulse);
    gl.uniform1f(u.pulse, pulse);
    gl.drawArrays(gl.POINTS, land.now, 1);
  }

  // ── Section transitions ──────────────────────────────────────────────────

  /** Names panels by position so each one morphs into the panel at the same spot on the next page. */
  function namePanels(on) {
    document.querySelectorAll('.term-panel, .wall-tile').forEach((el, i) => {
      el.style.viewTransitionName = on && i < 16 ? `fx-panel-${i}` : '';
    });
  }

  /** Resolves once the routed page swaps (MainLayout keys its ErrorBoundary on the URI). */
  function pageSwapped() {
    return new Promise((resolve) => {
      const main = document.getElementById('main-content') || document.body;
      let timer = 0;
      const done = () => { observer.disconnect(); clearTimeout(timer); resolve(); };
      const observer = new MutationObserver(done);
      observer.observe(main, { childList: true });
      timer = setTimeout(done, 400);
    });
  }

  /** Blazor SPA navigation, with a view transition where the browser has one. */
  function navigate(path) {
    const go = () => (window.Blazor?.navigateTo ? window.Blazor.navigateTo(path) : (location.href = path || '/'));
    const same = new URL(path || '.', document.baseURI).pathname === location.pathname;
    if (!document.startViewTransition || reduced.matches || same) { go(); return; }
    namePanels(true);
    const swapped = pageSwapped();
    const vt = document.startViewTransition(() => { go(); return swapped.then(() => namePanels(true)); });
    vt.finished.finally(() => namePanels(false));
  }

  // Section keys go through navigate(); Blazor's own link handling is on bubble, so capture wins.
  document.addEventListener('click', (e) => {
    const link = e.target.closest?.('.term-keys a[href]');
    if (!link || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    if (!document.startViewTransition || reduced.matches) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    navigate(link.getAttribute('href'));
  }, true);

  // ── Preferences ──────────────────────────────────────────────────────────

  function pref(name) {
    return !!prefs[name];
  }

  function set(name, on) {
    if (!(name in prefs)) return false;
    prefs[name] = !!on;
    try { localStorage.setItem(PREFS_KEY, JSON.stringify(prefs)); } catch { /* private mode */ }
    if (name === 'sound') {
      const ctx = audio();
      if (ctx) tone(ctx, { type: 'triangle', freq: 660, gain: 0.12, dur: 0.25 });
      else { stopDrone(); ac?.suspend(); }
      steerDrone();
    }
    if (name === 'announcer') {
      if (on) say('Announcer on.');
      else window.speechSynthesis?.cancel();
    }
    return prefs[name];
  }

  window.powatchFx = { update, exposure, blip, terrain: setTerrain, navigate, pref, set };
})();
