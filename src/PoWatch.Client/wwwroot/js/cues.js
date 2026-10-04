// Sound cues and desktop notifications. Every sound is synthesised with the Web Audio API (no audio
// files): short oscillator notes with a gain envelope, panned left or right to where in the frame the
// thing happened. The level (off, low, mid, high) is remembered in this browser.
(function () {
    'use strict';

    const KEY = 'powatch:sound';
    const LEVELS = { off: 0, low: 0.3, mid: 0.6, high: 1 };
    const ORDER = ['off', 'low', 'mid', 'high'];

    // Each note: [frequency Hz, starts at s, lasts s, waveform].
    const CUES = {
        start: [[440, 0, 0.09, 'sine'], [660, 0.09, 0.16, 'sine']],
        stop: [[660, 0, 0.09, 'sine'], [330, 0.09, 0.18, 'sine']],
        enter: [[880, 0, 0.06, 'sine']],
        regular: [[587, 0, 0.08, 'sine'], [880, 0.08, 0.14, 'sine']],
        alert: [[740, 0, 0.12, 'square'], [740, 0.18, 0.12, 'square']],
        error: [[196, 0, 0.28, 'sawtooth']],
        click: [[1200, 0, 0.025, 'square']],
    };

    let level = 'low';
    try {
        const stored = localStorage.getItem(KEY);
        if (stored in LEVELS) level = stored;
    } catch { /* private mode */ }

    let ctx = null;

    /** Plays a cue; `pan` runs from -1 (left edge of the frame) to 1 (right edge). */
    function play(name, pan) {
        const volume = LEVELS[level];
        const notes = CUES[name];
        if (!volume || !notes) return;
        try {
            ctx ??= new AudioContext();
            if (ctx.state === 'suspended') ctx.resume();
            const out = ctx.createStereoPanner();
            out.pan.value = Math.max(-1, Math.min(1, Number(pan) || 0));
            out.connect(ctx.destination);

            for (const [frequency, at, lasts, wave] of notes) {
                const from = ctx.currentTime + at;
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = wave;
                osc.frequency.value = frequency;
                // A fast attack and an exponential decay: a pluck, not a beep.
                gain.gain.setValueAtTime(0.0001, from);
                gain.gain.exponentialRampToValueAtTime(0.18 * volume, from + 0.012);
                gain.gain.exponentialRampToValueAtTime(0.0001, from + lasts);
                osc.connect(gain).connect(out);
                osc.start(from);
                osc.stop(from + lasts + 0.03);
            }
        } catch {
            // No audio device, or the browser has not seen a click yet: stay silent.
        }
    }

    /** Steps off → low → mid → high → off, remembers it, and returns the new level. */
    function cycle() {
        level = ORDER[(ORDER.indexOf(level) + 1) % ORDER.length];
        try { localStorage.setItem(KEY, level); } catch { /* private mode */ }
        play('click');
        return level;
    }

    /** Shows a desktop notification if the user allowed them; returns whether it was shown. */
    function notify(text) {
        try {
            if (!('Notification' in window) || Notification.permission !== 'granted') return false;
            new Notification('PoWatch', { body: text, tag: 'powatch-alert', icon: 'icon-192.png' });
            return true;
        } catch {
            return false;
        }
    }

    /** Asks for permission (must follow a click); returns "granted", "denied", "default" or "unsupported". */
    async function allowNotifications() {
        if (!('Notification' in window)) return 'unsupported';
        try { return await Notification.requestPermission(); } catch { return 'denied'; }
    }

    window.powatchCues = { play, cycle, level: () => level, notify, allowNotifications };
})();
