(() => {
  // Preferred clinical voice — pick a clear, neutral voice when available
  function pickVoice() {
    const voices = window.speechSynthesis.getVoices();
    return voices.find(v => v.lang.startsWith('en') && v.localService) ||
           voices.find(v => v.lang.startsWith('en')) ||
           null;
  }

  window.powatchAudio = {
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
