// Keeps the camera <video> alive across pages. The element lives in the layout (so navigating away
// from Live never removes it — Chrome pauses a video the moment it leaves the document) and is
// positioned over whichever "slot" the Live page offers. With no slot it is hidden but still playing,
// so the sensing session keeps sampling while you look at Stats or the stats wall.
(function () {
  'use strict';

  let _video = null;
  let _slot = null;
  let _observer = null;

  function place() {
    if (!_video || !_slot || !_slot.isConnected) return;
    const r = _slot.getBoundingClientRect();
    const main = document.getElementById('main-content');
    const m = main ? main.getBoundingClientRect() : { top: 0, left: 0, right: innerWidth, bottom: innerHeight };
    Object.assign(_video.style, {
      left: `${r.left}px`,
      top: `${r.top}px`,
      width: `${r.width}px`,
      height: `${r.height}px`,
      // Clip to the scrolling content area so the feed never slides over the header or the tape.
      clipPath: `inset(${Math.max(0, m.top - r.top)}px ${Math.max(0, r.right - m.right)}px ${Math.max(0, r.bottom - m.bottom)}px ${Math.max(0, m.left - r.left)}px)`,
      visibility: 'visible',
    });
  }

  function dock(video, slot) {
    _video = video;
    _slot = slot;
    _observer?.disconnect();
    _observer = new ResizeObserver(place);
    _observer.observe(slot);
    place();
  }

  function undock() {
    _slot = null;
    _observer?.disconnect();
    _observer = null;
    if (_video) _video.style.visibility = 'hidden';
  }

  addEventListener('resize', place);
  addEventListener('scroll', place, true);

  window.powatchFeed = { dock, undock };
})();
