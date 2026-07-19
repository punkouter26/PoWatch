// webcam-shell.js — GPU-accelerated scanline overlay (Idea #5).
//
// The current CSS overlay paints repeating-linear-gradient + a transform
// keyframe every frame on the main thread. This module swaps in a tiny
// WebGL2 fragment shader that runs on the GPU: one uniform "time" drives
// the scanline scroll, another uniform "intensity" toggles idle → thinking.
//
// Falls back gracefully when WebGL isn't available (old Safari / private mode).

(function () {
    'use strict';

    function compile(gl, type, src) {
        var sh = gl.createShader(type);
        gl.shaderSource(sh, src);
        gl.compileShader(sh);
        if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
            console.warn('[webcam-shell] shader compile failed:', gl.getShaderInfoLog(sh));
            gl.deleteShader(sh);
            return null;
        }
        return sh;
    }

    function link(gl, vs, fs) {
        var p = gl.createProgram();
        gl.attachShader(p, vs);
        gl.attachShader(p, fs);
        gl.linkProgram(p);
        if (!gl.getProgramParameter(p, gl.LINK_STATUS)) {
            console.warn('[webcam-shell] program link failed:', gl.getProgramInfoLog(p));
            gl.deleteProgram(p);
            return null;
        }
        return p;
    }

    function makeOverlay(canvas) {
        var gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
        if (!gl) return null;

        var vs = compile(gl, gl.VERTEX_SHADER, [
            'attribute vec2 a;',
            'void main(){ gl_Position = vec4(a, 0.0, 1.0); }'
        ].join('\n'));
        var fs = compile(gl, gl.FRAGMENT_SHADER, [
            'precision mediump float;',
            'uniform float u_time;',
            'uniform float u_intensity;',
            'uniform vec2 u_size;',
            'void main(){',
            '  float y = (gl_FragCoord.y + u_time * 60.0) / max(u_size.y, 1.0);',
            '  float scan = 0.5 + 0.5 * sin(y * 600.0);',           /* high-freq scanline */
            '  float glow = 0.5 + 0.5 * sin(y * 60.0);',             /* slow glow band */
            '  float v = mix(0.0, scan * 0.18 + glow * 0.04, u_intensity);',
            '  gl_FragColor = vec4(0.22, 1.0, 0.66, v);',            /* neon-mint accent */
            '}'
        ].join('\n'));
        if (!vs || !fs) return null;
        var prog = link(gl, vs, fs);
        if (!prog) return null;

        var buf = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, buf);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([
            -1, -1,  1, -1, -1,  1,
            -1,  1,  1, -1,  1,  1
        ]), gl.STATIC_DRAW);
        var a = gl.getAttribLocation(prog, 'a');
        var uTime = gl.getUniformLocation(prog, 'u_time');
        var uIntensity = gl.getUniformLocation(prog, 'u_intensity');
        var uSize = gl.getUniformLocation(prog, 'u_size');
        gl.enableVertexAttribArray(a);
        gl.vertexAttribPointer(a, 2, gl.FLOAT, false, 0, 0);

        return {
            gl: gl,
            render: function (time, intensity, w, h) {
                gl.viewport(0, 0, w, h);
                gl.useProgram(prog);
                gl.uniform1f(uTime, time * 0.001);
                gl.uniform1f(uIntensity, intensity);
                gl.uniform2f(uSize, w, h);
                gl.drawArrays(gl.TRIANGLES, 0, 6);
            }
        };
    }

    function attach(canvas) {
        if (!canvas) return;
        var ctx = makeOverlay(canvas);
        if (!ctx) {
            canvas.classList.add('webcam-shell-fallback');
            return;
        }
        var running = false;
        var startTs = 0;
        var intensity = 0;

        function loop(ts) {
            if (!running) return;
            var w = canvas.clientWidth | 0;
            var h = canvas.clientHeight | 0;
            if (canvas.width !== w || canvas.height !== h) {
                canvas.width = w; canvas.height = h;
            }
            // Ease intensity toward target so toggles don't pop.
            intensity += (targetIntensity - intensity) * 0.06;
            ctx.render(ts - startTs, intensity, w, h);
            raf = requestAnimationFrame(loop);
        }
        var raf = 0;
        var targetIntensity = 0;

        canvas.addEventListener('powatch:thinking', function (ev) {
            targetIntensity = ev && ev.detail && ev.detail.on ? 0.85 : 0.0;
            if (!running) {
                running = true;
                startTs = performance.now();
                raf = requestAnimationFrame(loop);
            }
        });
    }

    window.powatchWebcamShell = {
        attach: attach,
        // Helper used by Blazor: dispatch when thinking state changes.
        setThinking: function (on) {
            document.querySelectorAll('.webcam-shell').forEach(function (c) {
                c.dispatchEvent(new CustomEvent('powatch:thinking', { detail: { on: !!on } }));
            });
        }
    };

    function bootExisting() {
        document.querySelectorAll('.webcam-shell').forEach(attach);
    }
    if (document.readyState !== 'loading') bootExisting();
    else document.addEventListener('DOMContentLoaded', bootExisting);

    // Watch for newly added .webcam-shell canvases (Blazor adds them after DOMContentLoaded).
    var obs = new MutationObserver(function (muts) {
        for (var i = 0; i < muts.length; i++) {
            for (var j = 0; j < muts[i].addedNodes.length; j++) {
                var n = muts[i].addedNodes[j];
                if (n.nodeType !== 1) continue;
                if (n.classList && n.classList.contains('webcam-shell')) attach(n);
                if (n.querySelectorAll) n.querySelectorAll('.webcam-shell').forEach(attach);
            }
        }
    });
    if (document.body) obs.observe(document.body, { childList: true, subtree: true });
})();
