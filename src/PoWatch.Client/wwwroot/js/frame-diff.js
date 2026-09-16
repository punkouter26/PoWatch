// Frame-diff utility for adaptive observer cadence. Runs in the browser before the model is
// invoked: if two consecutive frames are near-identical, slow the cadence; if they differ
// sharply, ramp it back up. The exact rule lives on the managed side (CadenceProfile); this
// file just produces a normalised [0.0, 1.0] score for the renderer to consume.
//
// Algorithm: integer-mean-absolute-difference over the RGBA byte buffer. The score is the
// fraction of bytes that differ by more than a small threshold; this is not as perceptually
// faithful as a structural-similarity index, but it is O(N) over the byte buffer, fits in a
// single WebAssembly-friendly pass, and gives a stable signal for "did anything move?". A
// caregiver sitting still for an hour produces a near-zero score; a person walking through
// the room produces a near-one score.

(function () {
    'use strict';

    const BYTE_THRESHOLD = 12;

    window.powatchFrameDiff = {
        /**
         * Compare two ImageData objects and return a score in [0.0, 1.0].
         * Returns 1.0 (full change) if the dimensions differ — different sizes mean the camera
         * resized, and a resize is treated as a scene change worth running the model for.
         */
        score(previous, current) {
            if (!previous || !current) return 1.0;
            if (previous.width !== current.width || previous.height !== current.height) return 1.0;
            if (!previous.data || !current.data) return 1.0;
            if (previous.data.length !== current.data.length) return 1.0;

            const a = previous.data;
            const b = current.data;
            let diffCount = 0;
            // Walk every byte — RGBA gives us 4 channels per pixel. Sampling every 4th byte
            // (red channel) would be 4x faster but loses sensitivity to subtle colour shifts;
            // for a "did anything change?" signal the full walk is acceptable at 640x480.
            for (let i = 0; i < a.length; i++) {
                const delta = a[i] - b[i];
                if (delta < 0 ? -delta > BYTE_THRESHOLD : delta > BYTE_THRESHOLD) {
                    diffCount++;
                }
            }
            return Math.min(1.0, diffCount / a.length);
        }
    };
})();
