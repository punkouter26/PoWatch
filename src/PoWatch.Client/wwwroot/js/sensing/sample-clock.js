// A clock that keeps ticking in a hidden tab. Dedicated workers are exempt from the intensive
// timer throttling applied to background pages, so the pixel layer asks this worker when to sample.
let timer = null;

self.onmessage = (event) => {
  clearInterval(timer);
  const intervalMs = Math.max(50, Number(event.data?.intervalMs) || 250);
  timer = setInterval(() => self.postMessage(Date.now()), intervalMs);
};
