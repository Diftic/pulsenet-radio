/* ============================================================
   PulseNet Player — OBS Browser Source variant
   Embeds the configured channel's live_stream URL. The channel is
   substituted at serve time by BrowserSourceServer (window.PULSENET_CHANNEL).
   ============================================================ */

(function () {
  'use strict';

  var iframe = document.getElementById('player');
  var app = document.getElementById('app');

  // ---- Scale the 1252×670 canvas to fit the OBS source viewport ----
  // OBS Browser Source size is whatever the streamer chose. We pick the
  // smaller of (vw/canvas-width, vh/canvas-height) so the whole frame is
  // always visible; the unused axis becomes transparent letterbox/pillarbox.
  function rescale() {
    if (!app) return;
    var s = Math.min(window.innerWidth / 1252, window.innerHeight / 670);
    if (!isFinite(s) || s <= 0) s = 1;
    app.style.transform = 'scale(' + s + ')';
  }
  window.addEventListener('resize', rescale);
  rescale();

  // ---- Build station buttons from stations.js ----
  // Visual only — no click/hover handlers and pointer-events:none in CSS.
  // Asset URLs in stations.js are relative ("assets/stations/foo.png");
  // prefix with '/' so they resolve against the localhost server root.
  function buildButtons() {
    var stations = window.STATIONS || [];
    var leftCol  = document.getElementById('stations-left');
    var rightCol = document.getElementById('stations-right');
    if (!leftCol || !rightCol) return;

    stations.forEach(function (s) {
      var btn = document.createElement('button');
      btn.className = 'station-btn';
      btn.dataset.stationId = s.id;

      if (s.icon) {
        var img = document.createElement('img');
        img.src = '/' + s.icon.replace(/^\/+/, '');
        img.alt = s.label;
        img.draggable = false;
        btn.appendChild(img);
      } else {
        var icon = document.createElement('div');
        icon.className = 'station-placeholder';
        icon.textContent = '📻';
        btn.appendChild(icon);
        var num = document.createElement('div');
        num.className = 'station-num';
        num.textContent = s.slot;
        btn.appendChild(num);
      }

      (s.side === 'left' ? leftCol : rightCol).appendChild(btn);
    });
  }
  buildButtons();

  if (!iframe) return;

  var channelId = (window.PULSENET_CHANNEL || '').trim();

  // Default the iframe to nothing — we only load the live_stream URL once the
  // server tells us the in-app player is actually playing. Setting src=''
  // (rather than display:none) tears down the YouTube connection completely
  // when paused so the streamer's bandwidth isn't burned on hidden buffering.
  function liveStreamUrl() {
    if (!channelId) return '';
    return 'https://www.youtube.com/embed/live_stream?channel='
      + encodeURIComponent(channelId)
      + '&autoplay=1';
  }

  function showStream() {
    var url = liveStreamUrl();
    if (!url) return;
    if (iframe.src !== url) iframe.src = url;
    iframe.style.visibility = 'visible';
  }

  function hideStream() {
    if (iframe.src) iframe.src = '';
    iframe.style.visibility = 'hidden';
  }

  hideStream();

  // EventSource auto-reconnects on disconnect. The server pushes the current
  // state on every (re)connect, so a player restart is invisible to OBS.
  var es = new EventSource('/events');
  es.onmessage = function (e) {
    try {
      var msg = JSON.parse(e.data);
      if (!msg || msg.type !== 'state') return;
      if (msg.isPlaying) showStream();
      else hideStream();
    } catch (_) {}
  };
  es.onerror = function () {
    // Don't log noise — EventSource handles reconnect itself.
  };
})();
