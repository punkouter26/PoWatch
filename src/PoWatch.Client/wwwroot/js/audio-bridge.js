(() => {
  let sharedContext;
  let masterGain;

  function ctx() {
    const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
    if (!AudioContextCtor) return null;
    if (!sharedContext) {
      sharedContext = new AudioContextCtor();
      masterGain = sharedContext.createGain();
      masterGain.gain.value = 0.6;
      masterGain.connect(sharedContext.destination);
    }
    if (sharedContext.state === 'suspended') sharedContext.resume().catch(() => {});
    return sharedContext;
  }

  function muted() { return window.__powatchMuted === true; }

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

  window.powatchAudio = {
    cue(kind) {
      switch (kind) {
        case 'start': tone([440, 880], 140, 'triangle'); break;
        case 'stop': tone([660, 330], 150, 'triangle'); break;
        case 'ack': tone(720, 60, 'sine'); break;
        case 'alert': tone([880, 990], 180, 'triangle'); break;
        default: tone(520, 35, 'sine');
      }
    },
    playChirp() { tone(880, 120, 'triangle', 0.04); },
    announce(text) {
      if (muted() || !text || !('speechSynthesis' in window)) return;
      const utterance = new SpeechSynthesisUtterance(text);
      utterance.rate = 0.95;
      const voices = window.speechSynthesis.getVoices();
      const voice = voices.find(v => v.lang.startsWith('en') && v.localService);
      if (voice) utterance.voice = voice;
      window.speechSynthesis.cancel();
      window.speechSynthesis.speak(utterance);
    }
  };
})();
