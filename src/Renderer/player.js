/* ============================================================
   Pulsenet Radio — player controller
   ============================================================ */

(function () {
  'use strict';

  // ---- Channel ID from URL query param ----
  var params    = new URLSearchParams(window.location.search);
  var channelId = params.get('channelId') || '';

  function uploadsPlaylistId(chId) {
    if (!chId || chId.length < 2) return '';
    return 'UU' + chId.slice(2);
  }

  // ---- State ----
  var player          = null;
  var playerReady     = false;
  var pendingPlaylist = null;
  var activeBtn       = null;

  // ---- Station icon hover preview ----
  var previewWrap = document.getElementById('station-preview');
  var previewImg  = document.getElementById('station-preview-img');

  function showPreview(iconSrc) {
    if (!previewWrap || !previewImg || !iconSrc) return;
    previewImg.src = iconSrc;
    previewWrap.classList.remove('hidden');
    // Trigger fade-in on next frame so the transition fires after display:flex
    requestAnimationFrame(function () {
      previewWrap.classList.add('visible');
    });
  }

  function hidePreview() {
    if (!previewWrap) return;
    previewWrap.classList.remove('visible');
    // Remove from layout after fade completes (matches transition: 0.18s)
    setTimeout(function () {
      previewWrap.classList.add('hidden');
    }, 200);
  }

  // ---- Build station buttons from stations.js config ----
  var stations = window.STATIONS || [];

  function buildButtons() {
    var leftCol  = document.getElementById('stations-left');
    var rightCol = document.getElementById('stations-right');
    if (!leftCol || !rightCol) return;

    stations.forEach(function (s) {
      var btn = document.createElement('button');
      btn.className = 'station-btn';
      btn.title = s.label;
      btn.dataset.stationId = s.id;

      if (s.icon) {
        var img = document.createElement('img');
        img.src = s.icon;
        img.alt = s.label;
        img.draggable = false;
        btn.appendChild(img);
      } else {
        // Placeholder: generic icon + station number
        var icon = document.createElement('div');
        icon.className = 'station-placeholder';
        icon.textContent = '\uD83D\uDCFB'; // 📻
        btn.appendChild(icon);

        var num = document.createElement('div');
        num.className = 'station-num';
        num.textContent = s.slot;
        btn.appendChild(num);
      }

      btn.addEventListener('click', function () {
        activateStation(s, btn);
      });

      if (s.icon) {
        btn.addEventListener('mouseenter', function () { showPreview(s.icon); });
        btn.addEventListener('mouseleave', hidePreview);
      }

      (s.side === 'left' ? leftCol : rightCol).appendChild(btn);
    });
  }

  function activateStation(station, btn) {
    if (activeBtn) activeBtn.classList.remove('active');
    activeBtn = btn;
    btn.classList.add('active');

    if (playerReady && player) {
      player.loadPlaylist({ listType: 'playlist', list: station.playlistId });
    } else {
      pendingPlaylist = station.playlistId;
    }
  }

  // ---- Load YouTube IFrame API ----
  var tag = document.createElement('script');
  tag.src = 'https://www.youtube.com/iframe_api';
  document.head.appendChild(tag);

  window.onYouTubeIframeAPIReady = function () {
    var listId = uploadsPlaylistId(channelId);
    var vars = {
      autoplay:       0,
      controls:       1,
      rel:            0,
      modestbranding: 1,
      iv_load_policy: 3,
      origin:         window.location.origin,
    };
    if (listId) {
      vars.listType = 'playlist';
      vars.list     = listId;
    }

    player = new YT.Player('player', {
      width:       '100%',
      height:      '100%',
      playerVars:  vars,
      events: {
        onReady:       onPlayerReady,
        onStateChange: onPlayerStateChange,
        onError:       onPlayerError,
      },
    });
  };

  function onPlayerReady() {
    playerReady = true;
    if (pendingPlaylist) {
      player.loadPlaylist({ listType: 'playlist', list: pendingPlaylist });
      pendingPlaylist = null;
    }
  }

  function onPlayerStateChange(event) {
    // Native YouTube controls handle playback UI — no custom state needed here.
    // Poll title while playing so the host can show it in a tray tooltip later.
    if (event.data === YT.PlayerState.PLAYING) {
      scheduleTrackUpdate();
    }
  }

  function onPlayerError(event) {
    console.warn('Pulsenet Radio player error:', event.data);
  }

  // Track title polling (API fires no title-change event).
  function updateTrackTitle() {
    if (!playerReady || !player) return;
    try {
      var data = player.getVideoData();
      if (data && data.title) {
        // Reserved for tray tooltip or future HUD element.
        window.__pulsenetNowPlaying = data.title;
      }
    } catch (_) {}
  }

  function scheduleTrackUpdate() {
    setTimeout(updateTrackTitle, 800);
    setTimeout(updateTrackTitle, 2500);
  }

  setInterval(function () {
    if (playerReady && player && player.getPlayerState() === YT.PlayerState.PLAYING) {
      updateTrackTitle();
    }
  }, 2000);

  // ---- Settings panel ----
  // Opacity slider maps 0–100% display → 50–100% actual (CSS opacity on html).
  // Human eyes perceive < 50% opacity as unusably faint on a monitor.
  var dragLocked     = false;
  var currentOpacity = 1.0;   // CSS opacity value sent to C#
  var currentZoom    = 100;   // zoom % sent to C#

  var settingsBtn    = document.getElementById('settings-btn');
  var settingsPanel  = document.getElementById('settings-panel');
  var lockBtn        = document.getElementById('lock-btn');
  var opacitySlider  = document.getElementById('opacity-slider');
  var opacityVal     = document.getElementById('opacity-val');
  var zoomSlider     = document.getElementById('zoom-slider');
  var zoomVal        = document.getElementById('zoom-val');
  var hotkeyBtn      = document.getElementById('hotkey-btn');

  if (settingsBtn) {
    settingsBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      settingsPanel.classList.toggle('hidden');
    });
  }

  if (lockBtn) {
    lockBtn.addEventListener('click', function () {
      dragLocked = !dragLocked;
      lockBtn.textContent = dragLocked ? '\uD83D\uDD12 Locked' : '\uD83D\uDD13 Unlocked';
      lockBtn.classList.toggle('locked', dragLocked);
    });
  }

  if (opacitySlider) {
    opacitySlider.addEventListener('input', function () {
      var pct = parseInt(this.value, 10);
      opacityVal.textContent = pct + '%';
      // Map display 0–100% → actual opacity 0.50–1.00
      currentOpacity = 0.50 + (pct / 100) * 0.50;
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'opacity', value: currentOpacity }));
      } catch (_) {}
    });
  }

  if (zoomSlider) {
    zoomSlider.addEventListener('input', function () {
      currentZoom = parseInt(this.value, 10);
      zoomVal.textContent = currentZoom + '%';
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'zoom', pct: currentZoom }));
      } catch (_) {}
    });
  }

  if (hotkeyBtn) {
    hotkeyBtn.addEventListener('click', function () {
      try {
        window.chrome.webview.postMessage('open-settings');
      } catch (_) {}
    });
  }

  // Close panel on click outside
  document.addEventListener('click', function (e) {
    if (settingsPanel && !settingsPanel.classList.contains('hidden')) {
      if (!settingsPanel.contains(e.target) && e.target !== settingsBtn) {
        settingsPanel.classList.add('hidden');
      }
    }
  });

  // ---- Window drag — frame border areas only ----
  // Sends dx/dy deltas to C# via WebView2 postMessage so the host window moves.
  var isDragging = false;
  var lastDrag   = null;

  document.addEventListener('mousedown', function (e) {
    if (e.button !== 0) return;
    // Skip interactive elements — buttons, sliders, settings panel
    if (e.target.closest && e.target.closest('#settings-btn, #settings-panel, .station-btn')) return;
    if (dragLocked) return;
    if (isDraggableArea(e.clientX, e.clientY)) {
      isDragging = true;
      lastDrag   = { x: e.screenX, y: e.screenY };
      e.preventDefault();
    }
  });

  document.addEventListener('mousemove', function (e) {
    if (!isDragging || !lastDrag) return;
    var dx = e.screenX - lastDrag.x;
    var dy = e.screenY - lastDrag.y;
    lastDrag = { x: e.screenX, y: e.screenY };
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'drag', dx: dx, dy: dy }));
    } catch (_) {}
  });

  document.addEventListener('mouseup', function () {
    isDragging = false;
    lastDrag   = null;
  });

  // Returns true if (x,y) is on the frame border (not video or button columns).
  function isDraggableArea(x, y) {
    // Video rect
    if (x >= 220 && x <= 1032 && y >= 100 && y <= 533) return false;
    // Left button column
    if (x < 220 && y >= 100 && y <= 533) return false;
    // Right button column
    if (x > 1032 && y >= 100 && y <= 533) return false;
    return true;
  }

  // ---- Initialise ----
  buildButtons();

})();
