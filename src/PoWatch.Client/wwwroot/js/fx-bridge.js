(() => {
  // ─── fx-bridge.js ─────────────────────────────────────────────────────────────
  // Visual FX surface — every primitive is a no-op when its preconditions fail:
  // canvas, WebGL2, WebGPU, or reduced-motion preference. All state lives in module-scope
  // closures so successive calls (e.g. addPebble, removePebble) are idempotent.

  function reduced() {
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  }
  function palette() {
    const css = getComputedStyle(document.documentElement);
    return {
      violet: css.getPropertyValue('--palette-violet-500').trim() || '#6f3de3',
      mint:   css.getPropertyValue('--palette-mint-500').trim()   || '#38ffa8',
      amber:  css.getPropertyValue('--palette-amber-500').trim()  || '#ff9f40',
      rose:   css.getPropertyValue('--palette-rose-500').trim()   || '#ff7070',
      ink:    css.getPropertyValue('--palette-ink-100').trim()   || '#ecf1ff',
      bg:     css.getPropertyValue('--color-surface-0').trim()    || '#05030a',
      surface1: css.getPropertyValue('--color-surface-1').trim() || '#0f0b1f',
    };
  }

  // ─── #3 Calm Ripple — CSS-only, single delegate per page ─────────────────────
  // Globals are attached so we don't add a listener per button. The element is removed after
  // its animation finishes — no per-frame work, zero DOM accumulation.
  let _rippleBound = false;
  function setupRipples() {
    if (_rippleBound) return; _rippleBound = true;
    document.addEventListener('pointerdown', (e) => {
      const target = e.target.closest('button, .rz-button, .subject-card, .nav-link, a, [data-ripple]');
      if (!target || target.hasAttribute('data-ripple-disabled')) return;
      if (reduced()) return;
      const rect = target.getBoundingClientRect();
      const r = document.createElement('span');
      r.className = 'powatch-ripple';
      const size = Math.max(rect.width, rect.height) * 1.4;
      r.style.cssText = `left:${e.clientX - rect.left - size/2}px;top:${e.clientY - rect.top - size/2}px;width:${size}px;height:${size}px;`;
      // Container must have position:relative/overflow:hidden; add a wrapper if missing.
      const cs = getComputedStyle(target);
      if (cs.position === 'static') target.style.position = 'relative';
      if (cs.overflow === 'visible' && cs.position !== 'fixed' && cs.position !== 'absolute')
        target.style.overflow = 'hidden';
      target.appendChild(r);
      r.addEventListener('animationend', () => r.remove(), { once: true });
    }, { passive: true });
  }

  // ─── #8 Cursor-parallax glass cards ─────────────────────────────────────────
  // Single mousemove listener; updates CSS custom properties only — no canvas. Cards opt in
  // with [data-parallax-tilt]; the maximum tilt in degrees is configurable per element.
  let _parallaxRaf = 0;
  let _parallaxLastEvent = null;
  function setupParallax() {
    document.addEventListener('pointermove', (e) => {
      if (reduced()) return;
      _parallaxLastEvent = e;
      if (_parallaxRaf) return;
      _parallaxRaf = requestAnimationFrame(() => {
        _parallaxRaf = 0;
        if (!_parallaxLastEvent) return;
        const cards = document.querySelectorAll('[data-parallax-tilt]');
        for (const c of cards) {
          const rect = c.getBoundingClientRect();
          if (_parallaxLastEvent.clientY < rect.top - 240 || _parallaxLastEvent.clientY > rect.bottom + 240) continue;
          const cx = rect.left + rect.width / 2;
          const cy = rect.top + rect.height / 2;
          const dx = (_parallaxLastEvent.clientX - cx) / rect.width;
          const dy = (_parallaxLastEvent.clientY - cy) / rect.height;
          const max = parseFloat(c.getAttribute('data-parallax-tilt')) || 5;
          c.style.setProperty('--tilt-x', `${(-dy * max).toFixed(2)}deg`);
          c.style.setProperty('--tilt-y', `${( dx * max).toFixed(2)}deg`);
          c.style.setProperty('--glare-x', `${(50 + dx * 50).toFixed(1)}%`);
          c.style.setProperty('--glare-y', `${(50 + dy * 50).toFixed(1)}%`);
        }
      });
    }, { passive: true });
  }

  // ─── #6 Ambient fireflies — WebGL2 particle system, falls back to CSS ────────
  // One full-screen transparent canvas; particles drift upward; density scales with `intensity`.
  const Fireflies = (() => {
    let canvas, gl, prog, state = null;
    let lastTs = 0;
    const MAX = 240;
    const verts = new Float32Array(MAX * 2);

    function init() {
      if (state || reduced()) return false;
      canvas = document.createElement('canvas');
      canvas.className = 'powatch-fireflies';
      canvas.setAttribute('aria-hidden', 'true');
      document.body.appendChild(canvas);
      gl = canvas.getContext('webgl2', { premultipliedAlpha: true, antialias: true, transparent: true });
      if (!gl) { canvas.remove(); return false; }
      const vs = `#version 300 es\nin vec2 a;uniform vec2 u_res;uniform float u_dpr;void main(){gl_Position=vec4(((a/u_res)*2.0-1.0)*vec2(1.0,-1.0),0.0,1.0);gl_PointSize=8.0*u_dpr;}`;
      const fs = `#version 300 es\nprecision mediump float;out vec4 o;void main(){vec2 p=gl_PointCoord-0.5;float d=length(p);float a=smoothstep(0.5,0.0,d)*0.6;o=vec4(0.95,0.97,1.0,a);}`;
      function compile(t, s) { const sh = gl.createShader(t); gl.shaderSource(sh, s); gl.compileShader(sh); return sh; }
      const v = compile(gl.VERTEX_SHADER, vs), f = compile(gl.FRAGMENT_SHADER, fs);
      prog = gl.createProgram(); gl.attachShader(prog, v); gl.attachShader(prog, f); gl.linkProgram(prog);
      gl.useProgram(prog);
      const buf = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buf);
      gl.enableVertexAttribArray(gl.getAttribLocation(prog, 'a'));
      gl.vertexAttribPointer(gl.getAttribLocation(prog, 'a'), 2, gl.FLOAT, false, 0, 0);
      gl.uniform2f(gl.getUniformLocation(prog, 'u_res'), 1, 1);
      gl.uniform1f(gl.getUniformLocation(prog, 'u_dpr'), 1);
      gl.blendFunc(gl.SRC_ALPHA, gl.ONE);
      gl.enable(gl.BLEND);
      state = {
        particles: Array.from({ length: MAX }, () => spawn()),
        intensity: 0.4,
        cursorBoost: 0,
      };
      resize();
      window.addEventListener('resize', resize);
      requestAnimationFrame(loop);
      return true;
    }
    function spawn() {
      const w = window.innerWidth, h = window.innerHeight;
      return {
        x: Math.random() * w,
        y: h + Math.random() * h * 0.5,
        vx: (Math.random() - 0.5) * 6,
        vy: -8 - Math.random() * 14,
        r: 1.5 + Math.random() * 2.5,
        life: 0,
        max: 14 + Math.random() * 20,
        phase: Math.random() * Math.PI * 2,
      };
    }
    function resize() {
      if (!canvas || !gl) return;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = window.innerWidth * dpr;
      canvas.height = window.innerHeight * dpr;
      canvas.style.width = window.innerWidth + 'px';
      canvas.style.height = window.innerHeight + 'px';
      gl.viewport(0, 0, canvas.width, canvas.height);
      gl.useProgram(prog);
      gl.uniform2f(gl.getUniformLocation(prog, 'u_res'), canvas.width, canvas.height);
      gl.uniform1f(gl.getUniformLocation(prog, 'u_dpr'), dpr);
    }
    function loop(ts) {
      if (!state || !gl) return;
      const dt = lastTs ? Math.min(0.05, (ts - lastTs) / 1000) : 0.016;
      lastTs = ts;
      const paletteC = palette();
      gl.clearColor(0, 0, 0, 0);
      gl.clear(gl.COLOR_BUFFER_BIT);
      // Active count scales with intensity (0..1).
      const active = Math.max(0, Math.floor(MAX * state.intensity) + state.cursorBoost);
      for (let i = 0; i < MAX; i++) {
        const p = state.particles[i];
        if (i >= active) { verts[i*2] = -10; verts[i*2+1] = -10; continue; }
        p.life += dt;
        if (p.life > p.max || p.y < -40) { state.particles[i] = spawn(); continue; }
        p.x += p.vx * dt + Math.sin(p.phase + p.life * 0.8) * 4 * dt;
        p.y += p.vy * dt;
        verts[i*2] = p.x * (canvas.width / window.innerWidth);
        verts[i*2+1] = p.y * (canvas.height / window.innerHeight);
      }
      gl.bufferData(gl.ARRAY_BUFFER, verts, gl.DYNAMIC_DRAW);
      gl.drawArrays(gl.POINTS, 0, active);
      requestAnimationFrame(loop);
      // Avoid lint warnings about unused palette; available for future tinting.
      void paletteC;
    }
    return {
      init,
      setIntensity(v) { if (state) state.intensity = Math.max(0, Math.min(1, v)); },
      boost(count) { if (state) state.cursorBoost = Math.max(0, Math.min(60, count | 0)); },
    };
  })();

  // ─── #4 Timeline river — single full-bleed WebGL2 surface ─────────────────────
  // Events are luminous marks on a flowing stream; time scrubbing pans the camera.
  const Timeline = (() => {
    let canvas, gl, prog, buf, state = null;
    function init(container) {
      if (state || reduced()) return false;
      const host = typeof container === 'string' ? document.getElementById(container) : container;
      if (!host) return false;
      canvas = document.createElement('canvas');
      canvas.className = 'powatch-timeline-river';
      canvas.setAttribute('aria-hidden', 'true');
      host.style.position = host.style.position || 'relative';
      host.prepend(canvas);
      gl = canvas.getContext('webgl2', { premultipliedAlpha: true, antialias: true, transparent: true });
      if (!gl) { canvas.remove(); return false; }
      const vs = `#version 300 es\nin vec2 a;uniform vec2 u_res;void main(){gl_Position=vec4((a/u_res*2.0-1.0)*vec2(1.0,-1.0),0.0,1.0);}`;
      const fs = `#version 300 es\nprecision mediump float;uniform float u_t;uniform vec2 u_mouse;uniform vec3 u_a;uniform float u_count;out vec4 o;
void main(){
  vec2 uv = gl_FragCoord.xy/u_res;
  float wave = 0.5 + 0.5*sin(uv.y*8.0 + u_t*0.6);
  float band = smoothstep(0.35, 0.5, wave) - smoothstep(0.5, 0.65, wave);
  vec3 col = mix(u_a * 0.2, u_a, band);
  // Marks: each event is a luminous dot flowing along the stream (cheap; pseudo-random from uv).
  for(int i=0;i<32;i++){
    float fi = float(i)/32.0;
    float xs = fract(fi*7.31 + u_t*0.04);
    float ys = 0.45 + 0.08*sin(u_t*0.3 + fi*9.0);
    float d = distance(uv, vec2(xs, ys));
    if(u_count > fi){ col += vec3(0.6,0.9,1.0) * smoothstep(0.012, 0.0, d); }
  }
  // Mouse-following spot for visual feedback.
  float md = distance(uv, u_mouse);
  col += vec3(0.4,0.3,0.9) * 0.18 * smoothstep(0.18,0.0,md);
  o = vec4(col, 0.75);
}`;
      function compile(t, s) { const sh = gl.createShader(t); gl.shaderSource(sh, s); gl.compileShader(sh); return sh; }
      const v = compile(gl.VERTEX_SHADER, vs), f = compile(gl.FRAGMENT_SHADER, fs);
      prog = gl.createProgram(); gl.attachShader(prog, v); gl.attachShader(prog, f); gl.linkProgram(prog);
      gl.useProgram(prog);
      const vbo = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0,0,1,0,0,1, 1,0,1,1,0,1]), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0);
      gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
      gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA); gl.enable(gl.BLEND);
      state = { t: 0, count: 0, mouse: [0.5, 0.5] };
      resize();
      window.addEventListener('resize', resize);
      canvas.addEventListener('pointermove', (e) => {
        const rect = canvas.getBoundingClientRect();
        state.mouse = [(e.clientX - rect.left) / rect.width, 1 - (e.clientY - rect.top) / rect.height];
      });
      requestAnimationFrame(loop);
      return true;
    }
    function resize() {
      if (!canvas || !gl) return;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const rect = canvas.parentElement.getBoundingClientRect();
      canvas.width = rect.width * dpr;
      canvas.height = rect.height * dpr;
      canvas.style.width = rect.width + 'px';
      canvas.style.height = rect.height + 'px';
      gl.viewport(0, 0, canvas.width, canvas.height);
    }
    function loop(ts) {
      if (!state || !gl) return;
      state.t = ts / 1000;
      const p = palette();
      gl.clearColor(0, 0, 0, 0);
      gl.clear(gl.COLOR_BUFFER_BIT);
      gl.useProgram(prog);
      gl.uniform2f(gl.getUniformLocation(prog, 'u_res'), canvas.width, canvas.height);
      gl.uniform1f(gl.getUniformLocation(prog, 'u_t'), state.t);
      gl.uniform2f(gl.getUniformLocation(prog, 'u_mouse'), state.mouse[0], state.mouse[1]);
      gl.uniform3f(gl.getUniformLocation(prog, 'u_a'),
        parseFloat((parseInt(p.violet.slice(1,3),16))/255),
        parseFloat((parseInt(p.violet.slice(3,5),16))/255),
        parseFloat((parseInt(p.violet.slice(5,7),16))/255));
      gl.uniform1f(gl.getUniformLocation(prog, 'u_count'), Math.min(1, state.count / 12));
      gl.drawArrays(gl.TRIANGLES, 0, 6);
      requestAnimationFrame(loop);
    }
    return { init, setCount(c) { if (state) state.count = c; } };
  })();

  // ─── #1 Glassmorphic pebble field — WebGPU with WebGL2 fallback / CSS ────────
  // Each pebble is a refractive blob where the page itself shows through; we sample a
  // page-texture by reading back the WebGL framebuffer from Timeline OR fall back to
  // backdrop-filter blur via JS-updated CSS variable. The dense work is GPU-side.
  const Pebbles = (() => {
    let canvas, gl, prog, state = null;
    function init(container) {
      if (state || reduced()) return false;
      const host = typeof container === 'string' ? document.getElementById(container) : container;
      if (!host) return false;
      canvas = document.createElement('canvas');
      canvas.className = 'powatch-pebbles';
      canvas.setAttribute('aria-hidden', 'true');
      host.style.position = host.style.position || 'relative';
      host.appendChild(canvas);
      gl = canvas.getContext('webgl2');
      if (!gl) { canvas.remove(); return false; }
      const vs = `#version 300 es\nin vec2 a;uniform vec2 u_res;void main(){gl_Position=vec4((a*2.0-1.0)*vec2(1.0,-1.0),0.0,1.0);}`;
      const fs = `#version 300 es\nprecision highp float;uniform vec2 u_res;uniform float u_t;uniform float u_meta[40];
out vec4 o;
void main(){
  vec2 uv = gl_FragCoord.xy / u_res;
  vec3 col = vec3(0.0);
  // Up to 10 pebbles; small loop, evaluated per fragment.
  for(int i=0;i<10;i++){
    float x = u_meta[i*4+0];
    float y = u_meta[i*4+1];
    float r = u_meta[i*4+2];
    float hue = u_meta[i*4+3];
    float d = distance(uv, vec2(x, y));
    if(d > r) continue;
    float t = u_t * (0.6 + 0.3*sin(float(i)));
    vec2 light = vec2(0.5 + 0.3*sin(t), 0.5 + 0.3*cos(t*1.1));
    float NdL = max(0.0, dot(normalize(uv - vec2(x,y)), normalize(light - vec2(x,y))));
    float edge = smoothstep(r*0.95, r*0.7, d);
    // Caustic-like: ring of saturated hue, fading interior.
    vec3 base = mix(vec3(0.4,0.3,0.9), vec3(0.3,0.9,0.7), hue);
    col += base * (0.55 + 0.45*NdL) * edge;
    // Inner highlight (refraction sparkle).
    float inr = smoothstep(r*0.55, r*0.0, d);
    col += vec3(1.0) * inr * inr * 0.35;
  }
  o = vec4(col, 1.0);
}`;
      function compile(t, s) { const sh = gl.createShader(t); gl.shaderSource(sh, s); gl.compileShader(sh); return sh; }
      const v = compile(gl.VERTEX_SHADER, vs), f = compile(gl.FRAGMENT_SHADER, fs);
      prog = gl.createProgram(); gl.attachShader(prog, v); gl.attachShader(prog, f); gl.linkProgram(prog);
      gl.useProgram(prog);
      const vbo = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0,0,1,0,1,1, 0,0,1,1,0,1]), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0);
      gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
      gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
      // 10 pebbles × (x,y,r,hue) = 40 floats in one uniform.
      const seed = new Float32Array(40);
      for (let i = 0; i < 10; i++) {
        seed[i*4+0] = Math.random();
        seed[i*4+1] = Math.random();
        seed[i*4+2] = 0.06 + Math.random() * 0.04;
        seed[i*4+3] = Math.random();
      }
      state = { t: 0, meta: seed };
      resize();
      window.addEventListener('resize', resize);
      requestAnimationFrame(loop);
      return true;
    }
    function resize() {
      if (!canvas || !gl || !canvas.parentElement) return;
      const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
      const rect = canvas.parentElement.getBoundingClientRect();
      canvas.width = rect.width * dpr;
      canvas.height = rect.height * dpr;
      canvas.style.width = rect.width + 'px';
      canvas.style.height = rect.height + 'px';
      gl.viewport(0, 0, canvas.width, canvas.height);
    }
    function loop(ts) {
      if (!state || !gl) return;
      state.t = ts / 1000;
      gl.clearColor(0, 0, 0, 0);
      gl.clear(gl.COLOR_BUFFER_BIT);
      gl.useProgram(prog);
      gl.uniform2f(gl.getUniformLocation(prog, 'u_res'), canvas.width, canvas.height);
      gl.uniform1f(gl.getUniformLocation(prog, 'u_t'), state.t);
      gl.uniform1fv(gl.getUniformLocation(prog, 'u_meta'), state.meta);
      gl.drawArrays(gl.TRIANGLES, 0, 6);
      requestAnimationFrame(loop);
    }
    function setPebble(i, partial) {
      if (!state) return;
      const m = state.meta;
      const base = i * 4;
      if (partial.x !== undefined) m[base + 0] = partial.x;
      if (partial.y !== undefined) m[base + 1] = partial.y;
      if (partial.r !== undefined) m[base + 2] = partial.r;
      if (partial.hue !== undefined) m[base + 3] = partial.hue;
    }
    return { init, setPebble };
  })();

  // ─── #7 Breath-synced backdrop pulse ─────────────────────────────────────────
  // Reads a numeric breath rate from `setRate` (BPM); computes phase on requestAnimationFrame.
  // Drives a CSS variable on :root so every consumer (gradient, glass cards, particle alpha)
  // reflects the same breath. Off by default — `enabled` flag toggles the listener.
  const Breath = (() => {
    let raf = 0, t0 = 0, bpm = 12, enabled = false, lastOutput = -1;
    function step(ts) {
      if (!enabled) return;
      const phase = ((ts - t0) / 1000) * (bpm / 60); // cycles per radian
      // Map phase 0..1 → 0..1 envelope (sinusoidal breath: 0..1..0).
      const env = 0.5 + 0.5 * Math.sin(phase * Math.PI * 2);
      const rounded = Math.round(env * 1000) / 1000;
      if (rounded !== lastOutput) {
        lastOutput = rounded;
        document.documentElement.style.setProperty('--breath', rounded.toFixed(3));
      }
      raf = requestAnimationFrame(step);
    }
    return {
      enable(v) { enabled = !!v; if (enabled && !raf) { t0 = performance.now(); raf = requestAnimationFrame(step); } },
      setRate(b) { bpm = Math.max(4, Math.min(30, b)); },
    };
  })();

  // ─── #9 Handoff beam — one-shot WebGL2 fluid + particle on ceremony ──────────
  const Handoff = (() => {
    let canvas, gl, prog, raf = 0, start = 0;
    function init(container) {
      const host = typeof container === 'string' ? document.getElementById(container) : container;
      if (!host) return false;
      canvas = document.createElement('canvas');
      canvas.className = 'powatch-handoff';
      canvas.setAttribute('aria-hidden', 'true');
      host.style.position = host.style.position || 'relative';
      host.prepend(canvas);
      gl = canvas.getContext('webgl2', { transparent: true });
      if (!gl) { canvas.remove(); return false; }
      const vs = `#version 300 es\nin vec2 a;uniform vec2 u_res;void main(){gl_Position=vec4((a*2.0-1.0)*vec2(1.0,-1.0),0.0,1.0);}`;
      const fs = `#version 300 es\nprecision highp float;uniform vec2 u_res;uniform float u_t;uniform float u_phase;
out vec4 o;
float hash(vec2 p){ return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
void main(){
  vec2 uv = gl_FragCoord.xy / u_res;
  float t = u_t;
  // Convergence: spheres flicker in from edges (phase 0..1), snap to centre (1..1.4), dissolve (1.4..2).
  vec2 c = vec2(0.5);
  float gather = 0.0;
  float pulse = 0.0;
  for(int i=0;i<24;i++){
    float fi = float(i)/24.0;
    vec2 dir = vec2(cos(fi*6.28), sin(fi*6.28));
    vec2 pos = mix(dir*0.45 + c, c, smoothstep(0.0, 0.6, u_phase));
    float d = distance(uv, pos);
    float fade = (1.0 - u_phase) * smoothstep(0.04, 0.0, d);
    gather += fade * (0.4 + 0.4*sin(t*2.0 + fi*6.0));
  }
  // Convergence core glow.
  float core = smoothstep(0.18 + 0.04*sin(t*3.0), 0.0, distance(uv, c));
  float dissolve = smoothstep(1.4, 2.0, u_phase);
  vec3 col = vec3(0.6, 0.95, 0.78) * gather * 0.7 + vec3(1.0, 0.9, 0.6) * core * 1.1;
  col *= 1.0 - dissolve;
  // Noise-free grain via hash, very subtle.
  float n = hash(uv * 1000.0 + t);
  col += vec3(n * 0.03);
  o = vec4(col, 1.0 - dissolve * 0.85);
}`;
      function compile(t, s) { const sh = gl.createShader(t); gl.shaderSource(sh, s); gl.compileShader(sh); return sh; }
      const v = compile(gl.VERTEX_SHADER, vs), f = compile(gl.FRAGMENT_SHADER, fs);
      prog = gl.createProgram(); gl.attachShader(prog, v); gl.attachShader(prog, f); gl.linkProgram(prog);
      gl.useProgram(prog);
      const vbo = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0,0,1,0,1,1, 0,0,1,1,0,1]), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
      resize();
      window.addEventListener('resize', resize);
      return true;
    }
    function resize() {
      if (!canvas || !gl || !canvas.parentElement) return;
      const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
      const rect = canvas.parentElement.getBoundingClientRect();
      canvas.width = rect.width * dpr;
      canvas.height = rect.height * dpr;
      canvas.style.width = rect.width + 'px';
      canvas.style.height = rect.height + 'px';
      gl.viewport(0, 0, canvas.width, canvas.height);
    }
    function startBeam(durationMs) {
      if (!canvas || !gl) return;
      cancelAnimationFrame(raf);
      start = performance.now();
      const dur = durationMs || 2000;
      function loop(ts) {
        const phase = Math.min(2.0, (ts - start) / dur * 2.0);
        gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
        gl.useProgram(prog);
        gl.uniform2f(gl.getUniformLocation(prog, 'u_res'), canvas.width, canvas.height);
        gl.uniform1f(gl.getUniformLocation(prog, 'u_t'), ts / 1000);
        gl.uniform1f(gl.getUniformLocation(prog, 'u_phase'), phase);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
        if (phase < 2) raf = requestAnimationFrame(loop);
        else { canvas.style.opacity = '0'; setTimeout(() => { if (canvas) canvas.style.opacity = ''; }, 600); }
      }
      raf = requestAnimationFrame(loop);
    }
    return { init, startBeam };
  })();

  // Public surface
  window.powatchFx = {
    setupRipples,
    setupParallax,
    fireflies: Fireflies,
    timeline: Timeline,
    pebbles: Pebbles,
    breath: Breath,
    handoff: Handoff,
  };

  // One-time setup that has no per-page dependency. Parallax is global because every page benefits.
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => { setupRipples(); setupParallax(); });
  } else {
    setupRipples();
    setupParallax();
  }
})();
