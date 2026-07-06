(() => {
  // Preferred clinical voice — pick a clear, neutral voice when available
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
  function ctx() {
    if (typeof window === 'undefined') return null;
    const Ctor = window.AudioContext || window.webkitAudioContext;
    if (!Ctor) return null;
    if (!sharedCtx) sharedCtx = new Ctor();
    if (sharedCtx.state === 'suspended') sharedCtx.resume().catch(() => {});
    return sharedCtx;
  }

  // Play a short tone envelope. freqs: single value or [from, to] glide.
  function tone(freqs, durationMs, type = 'sine', peak = 0.05) {
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
    gain.connect(c.destination);
    osc.start(now);
    osc.stop(now + durationMs / 1000 + 0.02);
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
