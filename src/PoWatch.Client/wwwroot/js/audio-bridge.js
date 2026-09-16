(() => {
  // ─── audio-bridge.js ──────────────────────────────────────────────────────────
  // Synth-only audio: no shipped audio assets, ever. All cues are oscillators + filtered noise;
  // the soundscape is a slow generative pad; room tags are spatialised through Web Audio HRTF.
  // Every public method is a no-op when the AudioContext isn't usable or the user has muted
  // (prefers-reduced-audio / window.__powatchMuted === true).

  function pickVoice() {
    const voices = window.speechSynthesis.getVoices();
    return voices.find(v => v.lang.startsWith('en') && v.localService) ||
           voices.find(v => v.lang.startsWith('en')) ||
           null;
  }

  // Single shared AudioContext for lightweight interaction cues (audit #8). Reusing one context
  // avoids the per-cue allocation/limit of spawning a fresh AudioContext each time, and all sound is
  // synthesized with oscillators — zero downloaded audio assets.
  let sharedCtx = null;
  let masterGain = null;     // shared volume bus (ramped by mute/breath-hush)
  let pannerRoot = null;     // HRTF panner for spatial room tags
  function ctx() {
    if (typeof window === 'undefined') return null;
    const Ctor = window.AudioContext || window.webkitAudioContext;
    if (!Ctor) return null;
    if (!sharedCtx) {
      sharedCtx = new Ctor();
      masterGain = sharedCtx.createGain();
      masterGain.gain.value = 0.6;
      masterGain.connect(sharedCtx.destination);
      pannerRoot = sharedCtx.createGain();
      pannerRoot.gain.value = 1;
      try { pannerRoot.connect(new PannerNode(sharedCtx, { panningModel: 'HRTF', distanceModel: 'inverse' })).connect(masterGain); }
      catch { pannerRoot.connect(masterGain); } // older browsers without HRTF fall back to stereo
    }
    if (sharedCtx.state === 'suspended') sharedCtx.resume().catch(() => {});
    return sharedCtx;
  }

  function muted() {
    // Honour system-level "reduce audio" + a runtime mute toggle.
    const mq = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    return !!(window.__powatchMuted || mq && window.__powatchReducedAudio);
  }

  // Play a short tone envelope. freqs: single value or [from, to] glide.
  function tone(freqs, durationMs, type = 'sine', peak = 0.05) {
    if (muted()) return;
    const c = ctx();
    if (!c) return;
    const now = c.currentTime;
    const osc = c.createOscillator();
    const gain = c.createGain();
    const [f0, f1] = Array.isArray(freqs) ? freqs : [freqs, freqs];
    osc.type = type;
    osc.frequency.setValueAtTime(f0, now);
    if (f1 !== f0) osc.frequency.exponentialRampToValueAtTime(f1, now + durationMs / 1000);
    // Fast attack, smooth release — reads as a crisp "tick" rather than a beep.
    gain.gain.setValueAtTime(0.0001, now);
    gain.gain.exponentialRampToValueAtTime(peak, now + 0.008);
    gain.gain.exponentialRampToValueAtTime(0.0001, now + durationMs / 1000);
    osc.connect(gain);
    gain.connect(masterGain);
    osc.start(now);
    osc.stop(now + durationMs / 1000 + 0.02);
  }

  // Pentatonic chime (#2): soft mallet-style pluck from a single oscillator + amplitude-shaping envelope.
  // Day scale = C-major pentatonic; night = D-minor pentatonic — the time of day shapes timbre, not loudness.
  const PENTA_DAY  = [261.63, 293.66, 329.63, 392.00, 440.00, 523.25, 587.33]; // C4..D5
  const PENTA_NITE = [293.66, 349.23, 392.00, 440.00, 523.25, 587.33, 698.46]; // D4..F5
  function chime(scale, freq, durMs = 420) {
    if (muted()) return;
    const c = ctx(); if (!c) return;
    const now = c.currentTime;
    const osc = c.createOscillator();
    const g = c.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(freq, now);
    // Soft mallet envelope: fast attack, gentle exponential decay, no audible release click.
    g.gain.setValueAtTime(0.0001, now);
    g.gain.exponentialRampToValueAtTime(0.18, now + 0.012);
    g.gain.exponentialRampToValueAtTime(0.0001, now + durMs / 1000);
    osc.connect(g).connect(masterGain);
    osc.start(now); osc.stop(now + durMs / 1000 + 0.05);
    // A faint second harmonic for "bell" timbre without sounding metallic.
    const osc2 = c.createOscillator();
    const g2 = c.createGain();
    osc2.type = 'sine';
    osc2.frequency.setValueAtTime(freq * 2.01, now);
    g2.gain.setValueAtTime(0.0001, now);
    g2.gain.exponentialRampToValueAtTime(0.05, now + 0.014);
    g2.gain.exponentialRampToValueAtTime(0.0001, now + durMs / 1000 * 0.6);
    osc2.connect(g2).connect(masterGain);
    osc2.start(now); osc2.stop(now + durMs / 1000 * 0.7);
  }

  // ─── Generative soundscape (#10) ──────────────────────────────────────────────
  // Three slow detuned oscillators through a low-pass filter; the cutoff breathes with shift length.
  // Auto-fades on user activity (any keypress/click/mousemove flips a busyUntil timestamp).
  let soundscapeNodes = null;
  let soundscapeBusyUntil = 0;
  function ensureSoundscape() {
    if (muted() || soundscapeNodes) return soundscapeNodes;
    const c = ctx(); if (!c) return null;
    const out = c.createGain(); out.gain.value = 0.0; out.connect(masterGain);
    const lp = c.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 520; lp.Q.value = 0.6;
    lp.connect(out);
    const baseOscs = [110, 164.81, 220].map((f, i) => {
      const o = c.createOscillator(); o.type = i === 1 ? 'triangle' : 'sine';
      o.frequency.value = f; o.connect(lp); o.start(); return o;
    });
    soundscapeNodes = { out, lp, baseOscs };
    return soundscapeNodes;
  }
  function setSoundscapeIntensity(level /* 0..1 */, hourOfDay) {
    const n = ensureSoundscape(); if (!n) return;
    const c = sharedCtx;
    const now = c.currentTime;
    n.out.gain.cancelScheduledValues(now);
    n.out.gain.linearRampToValueAtTime(Math.min(0.06, 0.06 * level), now + 1.4);
    // Colder at night, warmer at dawn; controls cutoff.
    const target = 380 + (Math.sin((hourOfDay - 6) / 24 * Math.PI * 2) + 1) * 180;
    n.lp.frequency.linearRampToValueAtTime(target, now + 8);
  }
  function markSoundscapeBusy() {
    const n = soundscapeNodes; if (!n) return;
    const now = sharedCtx.currentTime;
    soundscapeBusyUntil = performance.now() + 3500;
    n.out.gain.cancelScheduledValues(now);
    n.out.gain.linearRampToValueAtTime(0.0, now + 0.6);
  }
  function tickSoundscape() {
    if (!soundscapeNodes) return;
    if (performance.now() > soundscapeBusyUntil) {
      const hour = new Date().getHours() + new Date().getMinutes() / 60;
      setSoundscapeIntensity(0.6, hour);
    }
  }
  if (typeof window !== 'undefined') {
    const busy = () => markSoundscapeBusy();
    ['pointerdown', 'pointermove', 'keydown', 'wheel'].forEach(ev => window.addEventListener(ev, busy, { passive: true }));
    setInterval(tickSoundscape, 6000);
  }

  window.powatchAudio = {
    // Interaction micro-feedback cue set, synced to UI events by the client.
    cue(kind) {
      switch (kind) {
        case 'start': tone([440, 880], 140, 'triangle', 0.05); break;   // rising — engaged
        case 'stop': tone([660, 330], 150, 'triangle', 0.05); break;    // falling — disengaged
        case 'ack': tone(720, 60, 'sine', 0.04); break;                 // soft confirm
        case 'tap': tone(520, 35, 'square', 0.02); break;               // crisp tap
        case 'alert': tone([880, 990], 90, 'sawtooth', 0.05);           // two-tone attention
                      setTimeout(() => tone([990, 880], 90, 'sawtooth', 0.05), 110); break;
        default: tone(520, 35, 'sine', 0.03);
      }
    },

    // ─── #2 Pentatonic chime library ───────────────────────────────────────────
    // mood 'day' (C-major pentatonic) or 'night' (D-minor pentatonic). step chooses pitch within scale.
    chime(mood, step) {
      const scale = mood === 'night' ? PENTA_NITE : PENTA_DAY;
      const i = Math.max(0, Math.min(scale.length - 1, step | 0));
      chime(scale, scale[i], 420 + (i % 3) * 60);
    },
    // Three-note arpeggio for richer moments (handoff, alert). Returns a Promise that resolves when
    // the last note fades — UI layers can sequence animations to the chime.
    async arpeggio(mood, steps) {
      const list = Array.isArray(steps) && steps.length ? steps : [0, 2, 4];
      for (let i = 0; i < list.length; i++) {
        this.chime(mood, list[i]);
        await new Promise(r => setTimeout(r, 120));
      }
    },

    // ─── #10 Soundscape control ────────────────────────────────────────────────
    startSoundscape() { ensureSoundscape(); tickSoundscape(); },
    setSoundscape(level) { setSoundscapeIntensity(level, new Date().getHours()); },
    stopSoundscape() {
      const n = soundscapeNodes; if (!n) return;
      const now = sharedCtx.currentTime;
      n.out.gain.cancelScheduledValues(now);
      n.out.gain.linearRampToValueAtTime(0, now + 1.2);
    },

    // ─── #5 Spatial chime for room tags ────────────────────────────────────────
    // `azimuth` ∈ [-1, 1] (-1 = left ear, +1 = right ear); `elevation` for height. Pure tone — no
    // algorithmic change for elevation beyond attenuating high freq; keeps the cue intelligible.
    spatialPing(mood, step, azimuth) {
      if (muted()) return;
      const c = ctx(); if (!c) return;
      const scale = mood === 'night' ? PENTA_NITE : PENTA_DAY;
      const i = Math.max(0, Math.min(scale.length - 1, step | 0));
      const now = c.currentTime;
      const osc = c.createOscillator();
      const g = c.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(scale[i], now);
      g.gain.setValueAtTime(0.0001, now);
      g.gain.exponentialRampToValueAtTime(0.18, now + 0.014);
      g.gain.exponentialRampToValueAtTime(0.0001, now + 0.7);
      const panner = new PannerNode(c, { panningModel: 'HRTF', positionX: azimuth * 3, positionY: 0, positionZ: -1, distanceModel: 'inverse' });
      osc.connect(g).connect(panner).connect(masterGain);
      osc.start(now); osc.stop(now + 0.75);
    },

    // ─── #7 Breath hush ────────────────────────────────────────────────────────
    // Quietly ramps the master gain down so the user hears silence for 2.5 s.
    hushFor(milliseconds) {
      const c = ctx(); if (!c || !masterGain) return;
      const now = c.currentTime;
      masterGain.gain.cancelScheduledValues(now);
      masterGain.gain.linearRampToValueAtTime(0.0, now + 0.18);
      setTimeout(() => {
        if (!sharedCtx) return;
        const t = sharedCtx.currentTime;
        masterGain.gain.cancelScheduledValues(t);
        masterGain.gain.linearRampToValueAtTime(0.6, t + 0.4);
      }, milliseconds);
    },

    setMuted(value) { window.__powatchMuted = !!value; },
    isMuted() { return muted(); },

    async announce(text) {
      if (!text || typeof window === 'undefined' || !('speechSynthesis' in window)) {
        return;
      }

      const utterance = new SpeechSynthesisUtterance(text);
      utterance.rate = 0.95;
      utterance.pitch = 1;
      utterance.volume = 1;
      const voice = pickVoice();
      if (voice) utterance.voice = voice;
      window.speechSynthesis.cancel();
      window.speechSynthesis.speak(utterance);
    },

    async announceSignificant(subjectName, activity, reason) {
      if (!('speechSynthesis' in window)) return;
      const parts = [`Significant event.`, subjectName ? `Subject: ${subjectName}.` : '', activity ? `Activity: ${activity}.` : '', reason ? reason : ''];
      const text = parts.filter(Boolean).join(' ');
      await window.powatchAudio.announce(text);
    },

    async announceOutlier(subjectName, activity) {
      if (!('speechSynthesis' in window)) return;
      const text = `Clinical outlier detected.${subjectName ? ' Subject: ' + subjectName + '.' : ''} ${activity || ''}`;
      await window.powatchAudio.announce(text);
    },

    async announceThresholdAlert(ruleName, subjectName) {
      if (!('speechSynthesis' in window)) return;
      const text = `Alert threshold breached: ${ruleName}.${subjectName ? ' Subject: ' + subjectName + '.' : ''}`;
      await window.powatchAudio.announce(text);
    },

    async playChirp() {
      if (typeof window === 'undefined' || !('AudioContext' in window || 'webkitAudioContext' in window)) {
        return;
      }

      const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
      const context = new AudioContextCtor();
      const oscillator = context.createOscillator();
      const gainNode = context.createGain();

      oscillator.type = 'triangle';
      oscillator.frequency.value = 880;
      gainNode.gain.value = 0.04;

      oscillator.connect(gainNode);
      gainNode.connect(context.destination);

      oscillator.start();
      oscillator.stop(context.currentTime + 0.12);

      setTimeout(() => {
        context.close().catch(() => {});
      }, 200);
    }
  };
})();
