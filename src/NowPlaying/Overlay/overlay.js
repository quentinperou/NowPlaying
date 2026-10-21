// The overlay page: listens to the app (Server-Sent Events: style, state, levels) and draws one layout, chosen by the
// address (/bar or /card). The progress goes on between two reports of the player; the "animation" visualizer is
// drawn here, the "spectrum" one comes from the app.
(function () {
  'use strict';
  const layout = /\/bar\/?$/i.test(location.pathname) ? 'bar' : 'card';
  const el = document.getElementById('overlay');
  const $ = s => el.querySelector(s);
  const BARS = 16;
  const viz = $('.viz');
  for (let i = 0; i < BARS; i++) viz.appendChild(document.createElement('i'));
  const bars = [...viz.children];

  el.classList.add(layout + '-layout');
  let style = null, state = { hasTrack: false }, receivedAt = performance.now(), levels = new Array(BARS).fill(0);

  const fmt = ms => {
    const s = Math.max(0, Math.floor(ms / 1000)), h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), r = s % 60;
    return (h ? h + ':' + String(m).padStart(2, '0') : m) + ':' + String(r).padStart(2, '0');
  };

  function applyStyle() {
    if (!style) return;
    const v = (k, x) => el.style.setProperty(k, x);
    v('--w', style.width + 'px'); v('--r', style.radius + 'px'); v('--fs', style.fontSize + 'px');
    v('--font', `"${style.font}", "Segoe UI", sans-serif`);
    v('--bg', style.background); v('--fg', style.text); v('--fg2', style.secondary); v('--track', style.track);
    el.classList.toggle('frosted', !!style.frosted);
    el.classList.toggle('no-cover', !style.showCover);
    el.classList.toggle('no-artist', !style.showArtist);
    el.classList.toggle('no-viz', !style.showVisualizer);
    el.classList.toggle('no-progress', !style.showProgress);
    el.classList.toggle('no-times', !style.showTimes);
    $('.live b').textContent = style.liveText || 'LIVE';
  }

  function applyState() {
    el.classList.toggle('hidden', !state.hasTrack || !style);
    $('.title').textContent = state.title || '';
    $('.artist').textContent = state.artist || '';
    $('.cover').style.backgroundImage = state.cover ? `url("${state.cover}")` : 'none';
    $('.t1').textContent = fmt(state.durationMs || 0);
  }

  function frame(now) {
    if (state.hasTrack && style) {
      if (style.showProgress) {
        // No length: a live stream if the player reports a position ("live" instead of the times and the bar), else
        // nothing (the app tells which).
        const at = (state.positionMs || 0) + (state.playing ? now - receivedAt : 0);
        const length = state.durationMs || 0;
        el.classList.toggle('is-live', length <= 0 && !!state.live);
        el.classList.toggle('no-length', length <= 0 && !state.live);
        const ms = length > 0 ? Math.min(length, at) : at;
        $('.t0').textContent = fmt(ms);
        $('.fill').style.width = (length > 0 ? ms / length * 100 : 0) + '%';
      }
      if (layout === 'bar' && style.showVisualizer) {
        for (let i = 0; i < BARS; i++) {
          let level = 0;
          if (state.playing) {
            if (style.visualizer === 'animation') {
              const t = now / 1000, wave = Math.sin(t * 2.4 + i * 0.6) * 0.5 + Math.sin(t * 1.3 + i * 1.7) * 0.3;
              level = Math.max(0.05, Math.min(1, 0.42 + 0.38 * wave * (1 - 0.4 * i / BARS)));
            } else {
              level = levels[i] || 0;
            }
          }
          bars[i].style.height = (4 + level * 30) + 'px';
        }
      }
    }
    requestAnimationFrame(frame);
  }

  // ?once: read what plays once, then stop listening (pictures of the page: a headless browser waits for the end of
  // every request).
  const once = new URLSearchParams(location.search).has('once');
  if (once) el.style.transition = 'none';

  function connect() {
    const events = new EventSource('/events?layout=' + layout);
    events.addEventListener('style', e => { style = JSON.parse(e.data); applyStyle(); applyState(); });
    events.addEventListener('state', e => {
      state = JSON.parse(e.data); receivedAt = performance.now(); applyState();
      if (once) events.close();
    });
    events.addEventListener('levels', e => {
      // 24 bands from the app, read as 16 bars.
      const bands = JSON.parse(e.data);
      for (let i = 0; i < BARS; i++) levels[i] = bands[Math.floor(i * bands.length / BARS)] || 0;
    });
    // The app closed: hide, the browser reconnects by itself when it is back.
    events.onerror = () => { el.classList.add('hidden'); };
  }

  connect();
  requestAnimationFrame(frame);
})();
