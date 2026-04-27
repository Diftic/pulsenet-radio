/* ============================================================
   PulseNet Player — player controller
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
  var player           = null;
  var playerReady      = false;
  var pendingPlaylist  = null;
  var pendingVideoId   = null;
  var activeBtn        = null;
  var liveStreamActive = false; // true when #player is a raw live_stream iframe

  // PulseNet Broadcasting main channel — used by the top-left "live" button.
  // We resolve the current broadcast via the live_stream embed URL so restarts
  // automatically follow the new videoId instead of breaking on past-broadcasts.
  var PULSENET_LIVE_CHANNEL = 'UCIMaIJsfJEMi5yJIe5nAb0g';

  // ---- Idle logo ----
  var idleLogo = document.getElementById('idle-logo');

  function showIdleLogo() {
    if (idleLogo) idleLogo.classList.remove('hidden');
  }

  function hideIdleLogo() {
    if (idleLogo) idleLogo.classList.add('hidden');
  }

  // ---- Station icon hover preview ----
  var previewWrap    = document.getElementById('station-preview');
  var previewImg     = document.getElementById('station-preview-img');
  var previewHideTimer = null;

  function showPreview(iconSrc) {
    if (!previewWrap || !previewImg || !iconSrc) return;
    // Cancel any pending hide so moving between buttons doesn't flicker
    if (previewHideTimer) { clearTimeout(previewHideTimer); previewHideTimer = null; }
    previewImg.src = iconSrc;
    previewWrap.classList.remove('hidden');
    requestAnimationFrame(function () {
      previewWrap.classList.add('visible');
    });
  }

  function hidePreview() {
    if (!previewWrap) return;
    previewWrap.classList.remove('visible');
    previewHideTimer = setTimeout(function () {
      previewWrap.classList.add('hidden');
      previewHideTimer = null;
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
        var previewSrc = s.live ? s.icon : (function (path) {
          var slash = path.lastIndexOf('/');
          return path.slice(0, slash + 1) + 'Offline_' + path.slice(slash + 1);
        })(s.icon);
        btn.addEventListener('mouseenter', function () { showPreview(previewSrc); });
        btn.addEventListener('mouseleave', hidePreview);
      }

      (s.side === 'left' ? leftCol : rightCol).appendChild(btn);
    });
  }

  function postCurrentStation(label) {
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'currentStation', label: label }));
    } catch (_) {}
  }

  function activateStation(station, btn) {
    if (activeBtn) activeBtn.classList.remove('active');
    activeBtn = btn;
    btn.classList.add('active');
    hideIdleLogo();
    postCurrentStation(station.label);
    // Track title resets on station switch — clear so the next poll re-pushes.
    lastReportedTitle = '';

    // Coming back from a live_stream iframe, or API player not yet built —
    // rebuild the YT.Player, then hand the station over once it's ready.
    if (liveStreamActive || !player) {
      createApiPlayer(function () { loadStationIntoPlayer(station); });
      return;
    }

    if (playerReady) {
      loadStationIntoPlayer(station);
    } else {
      pendingVideoId  = station.videoId  || null;
      pendingPlaylist = station.playlistId || null;
    }
  }

  function loadStationIntoPlayer(station) {
    if (!player) return;
    if (station.videoId) {
      player.loadVideoById(station.videoId);
    } else if (station.playlistId) {
      player.loadPlaylist({ listType: 'playlist', list: station.playlistId });
    }
  }

  // ---- Live stream / API player swap ----
  // YT.Player can only load specific videoIds. Live streams get a new videoId
  // whenever the broadcaster restarts, so pinning to one breaks when a stream
  // ends ("This live stream recording is not available"). The live_stream
  // embed URL always resolves to the current broadcast — but it only works as
  // a raw iframe, not through the IFrame API. So we swap between two modes:
  // API player for playlists/videos, raw iframe for live channel playback.

  function teardownPlayer() {
    if (player && typeof player.destroy === 'function') {
      try { player.destroy(); } catch (_) {}
    }
    player = null;
    playerReady = false;
    postKeyForwardCapable();

    // YT.Player replaces #player (a <div>) with an <iframe> of the same id.
    // Restore a clean <div id="player"> so the next creation has something
    // to mount against. Use replaceChild so we preserve the child index —
    // inserting at index 0 would put us before the leading whitespace text
    // node and shift the iframe's baseline by 1px.
    var wrap = document.getElementById('video-wrap');
    if (!wrap) return;
    var div = document.createElement('div');
    div.id = 'player';
    var existing = document.getElementById('player');
    if (existing && existing.parentNode) {
      existing.parentNode.replaceChild(div, existing);
    } else {
      wrap.appendChild(div);
    }
  }

  function createApiPlayer(onReadyCb) {
    // Only tear down when there's something to tear down. On initial load
    // the HTML-provided #player div is already clean; re-creating it via JS
    // produced a subtle 1px vertical offset versus the parser-created node.
    if (player || liveStreamActive) teardownPlayer();
    liveStreamActive = false;

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
        onReady: function () {
          playerReady = true;
          postKeyForwardCapable();
          if (onReadyCb) {
            onReadyCb();
          } else if (pendingVideoId) {
            player.loadVideoById(pendingVideoId);
            pendingVideoId = null;
          } else if (pendingPlaylist) {
            player.loadPlaylist({ listType: 'playlist', list: pendingPlaylist });
            pendingPlaylist = null;
          }
        },
        onStateChange: onPlayerStateChange,
        onError:       onPlayerError,
      },
    });
  }

  function loadLiveStream(chId) {
    teardownPlayer();
    liveStreamActive = true;
    postKeyForwardCapable();

    var div = document.getElementById('player');
    if (!div || !div.parentNode) return;

    // enablejsapi=1 lets YT.Player attach to the iframe and surface
    // onStateChange events for native YouTube pause/play. Without it the OBS
    // Browser Source has no way to know the streamer paused. We replace the
    // existing #player element with an iframe carrying the same id so YT.Player
    // can target it directly.
    var iframe = document.createElement('iframe');
    iframe.id = 'player';
    iframe.src = 'https://www.youtube.com/embed/live_stream?channel='
      + encodeURIComponent(chId) + '&autoplay=1&enablejsapi=1';
    iframe.setAttribute('frameborder', '0');
    iframe.setAttribute('allow', 'autoplay; encrypted-media; picture-in-picture');
    iframe.setAttribute('allowfullscreen', '');
    iframe.style.position = 'absolute';
    iframe.style.inset    = '0';
    iframe.style.width    = '100%';
    iframe.style.height   = '100%';
    iframe.style.border   = 'none';
    iframe.style.display  = 'block';
    div.parentNode.replaceChild(iframe, div);

    player = new YT.Player('player', {
      events: {
        onReady: function () {
          playerReady = true;
          postKeyForwardCapable();
        },
        onStateChange: onPlayerStateChange,
        onError:       onPlayerError,
      },
    });
  }

  // ---- Key-forward capability push ----
  // C# only swallows allow-listed keys when forwarding will succeed. For the
  // raw live-stream iframe (no IFrame API) and the moments before YT.Player is
  // ready we report false so YouTube's native click-then-keys flow continues
  // to work.
  function postKeyForwardCapable() {
    var canForward = !liveStreamActive && playerReady && !!player;
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'keyForwardCapable', value: canForward }));
    } catch (_) {}
  }

  // ---- Hover state push for keyboard hook gating ----
  // C#'s keyboard hook only forwards keys to the embed when the cursor is over
  // #video-wrap. Tracked here in the DOM so it works at any DPI / zoom without
  // any C#-side coordinate math.
  (function () {
    var wrap = document.getElementById('video-wrap');
    if (!wrap) return;
    function post(over) {
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'hoverVideo', over: over }));
      } catch (_) {}
    }
    wrap.addEventListener('mouseenter', function () { post(true); });
    wrap.addEventListener('mouseleave', function () { post(false); });
  })();

  // ---- Hover hotkey forwarder ----
  // C# global keyboard hook intercepts a small allow-list of YouTube keys when
  // the cursor is over the video rect (without stealing focus) and calls this
  // function to drive the IFrame API directly. Live-stream raw iframes have no
  // API surface so they're a no-op.
  window.__pulsenetForwardKey = function (action) {
    if (liveStreamActive) return;
    if (!playerReady || !player) return;
    try {
      switch (action) {
        case 'space':
          var st = player.getPlayerState();
          if (st === YT.PlayerState.PLAYING) player.pauseVideo();
          else player.playVideo();
          break;
        case 'mute':
          if (player.isMuted()) player.unMute();
          else player.mute();
          break;
        case 'left':
          player.seekTo(Math.max(0, player.getCurrentTime() - 5), true);
          break;
        case 'right':
          player.seekTo(player.getCurrentTime() + 5, true);
          break;
        case 'up':
          if (player.isMuted()) player.unMute();
          player.setVolume(Math.min(100, player.getVolume() + 5));
          break;
        case 'down':
          player.setVolume(Math.max(0, player.getVolume() - 5));
          break;
      }
    } catch (_) {}
  };

  // ---- Load YouTube IFrame API ----
  var tag = document.createElement('script');
  tag.src = 'https://www.youtube.com/iframe_api';
  document.head.appendChild(tag);

  window.onYouTubeIframeAPIReady = function () {
    createApiPlayer(null);
  };

  function onPlayerStateChange(event) {
    // Native YouTube controls handle playback UI — no custom state needed here.
    // Poll title while playing so the host can show it in a tray tooltip later.
    if (event.data === YT.PlayerState.PLAYING) {
      scheduleTrackUpdate();
    }
    // Restore idle logo when playlist finishes or player is unstarted with no active station
    if (event.data === YT.PlayerState.ENDED && !activeBtn) {
      showIdleLogo();
    }
  }

  function onPlayerError(event) {
    console.warn('PulseNet Player error:', event.data);
  }

  // Track title polling (API fires no title-change event).
  var lastReportedTitle = '';
  function updateTrackTitle() {
    if (!playerReady || !player) return;
    try {
      var data = player.getVideoData();
      if (data && data.title && data.title !== lastReportedTitle) {
        lastReportedTitle = data.title;
        window.__pulsenetNowPlaying = data.title;
        try {
          window.chrome.webview.postMessage(JSON.stringify({ type: 'nowPlaying', title: data.title }));
        } catch (_) {}
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

  // ---- Special utility buttons ----

  var homeBtn  = document.getElementById('pulsenet-home-btn');
  var aboutBtn = document.getElementById('about-btn');

  if (homeBtn) {
    homeBtn.addEventListener('click', function () {
      if (activeBtn) activeBtn.classList.remove('active');
      activeBtn = null;
      hideIdleLogo();
      loadLiveStream(PULSENET_LIVE_CHANNEL);
      postCurrentStation('Pulse Broadcasting Network - LIVE Music');
      // No track title from live_stream iframe — clear so the banner shows just the station.
      lastReportedTitle = '';
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'nowPlaying', title: '' }));
      } catch (_) {}
    });
  }

  if (aboutBtn) {
    aboutBtn.addEventListener('click', function () {
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'about' }));
      } catch (_) {}
    });
    aboutBtn.addEventListener('mouseenter', function () { showPreview('assets/info.png'); });
    aboutBtn.addEventListener('mouseleave', hidePreview);
  }

  // ---- Settings panel ----
  // Opacity slider maps 0–100% display → 30–100% actual (CSS opacity on html).
  var dragLocked     = false;
  var currentOpacity = 1.0;   // CSS opacity value sent to C#
  var currentZoom    = 100;   // zoom % sent to C#

  var settingsBtn    = document.getElementById('settings-btn');
  var settingsPanel  = document.getElementById('settings-panel');
  var lockBtn        = document.getElementById('lock-btn');
  var opacitySlider  = document.getElementById('opacity-slider');
  var opacityVal     = document.getElementById('opacity-val');
  var zoomDownBtn    = document.getElementById('zoom-down-btn');
  var zoomUpBtn      = document.getElementById('zoom-up-btn');
  var zoomInput      = document.getElementById('zoom-input');
  var hotkeyInput    = document.getElementById('hotkey-input');
  var minimizeSelect = document.getElementById('minimize-mode-select');

  if (settingsBtn) {
    settingsBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      // Fold any open sub-panel before toggling the main settings panel so we
      // never end up with both visible / one stacked behind the other.
      var miniplayerPanelEl = document.getElementById('miniplayer-settings-panel');
      if (miniplayerPanelEl && !miniplayerPanelEl.classList.contains('hidden')) {
        miniplayerPanelEl.classList.add('hidden');
        try {
          window.chrome.webview.postMessage(JSON.stringify({ type: 'bannerEditMode', value: false }));
        } catch (_) {}
      }
      var streamerPanelEl = document.getElementById('streamer-settings-panel');
      if (streamerPanelEl && !streamerPanelEl.classList.contains('hidden')) {
        streamerPanelEl.classList.add('hidden');
      }
      settingsPanel.classList.toggle('hidden');
    });
  }

  var discordBtn = document.getElementById('discord-btn');
  if (discordBtn) {
    discordBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      try {
        window.chrome.webview.postMessage(JSON.stringify({
          type: 'openUrl',
          url: 'https://discord.com/invite/Vxn7kzzWGJ',
        }));
      } catch (_) {}
    });
  }

  // Version is pushed in by C# (BuildSyncScript sets window.__pulsenetVersion).
  // The button label always reads the current global so a re-sync after a
  // successful in-place update would surface the new number without restart.
  var versionBtn = document.getElementById('version-btn');
  function versionLabel() {
    var v = window.__pulsenetVersion || '0.0.0';
    return 'v' + v + ' — Check for updates';
  }
  window.__pulsenetRefreshVersionLabel = function () {
    if (versionBtn && !versionBtn.disabled) versionBtn.textContent = versionLabel();
  };
  if (versionBtn) {
    versionBtn.textContent = versionLabel();
    versionBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      if (versionBtn.disabled) return;
      versionBtn.disabled = true;
      versionBtn.textContent = 'Checking for updates…';
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'checkForUpdates' }));
      } catch (_) {
        versionBtn.disabled = false;
        versionBtn.textContent = versionLabel();
      }
    });
  }
  // Called by C# once CheckAsync has finished and any modal has been
  // dismissed, so the button returns to its idle state.
  window.__pulsenetUpdateCheckDone = function () {
    if (!versionBtn) return;
    versionBtn.disabled = false;
    versionBtn.textContent = versionLabel();
  };

  if (lockBtn) {
    lockBtn.addEventListener('click', function () {
      dragLocked = !dragLocked;
      lockBtn.textContent = dragLocked ? '\uD83D\uDD12 Locked' : '\uD83D\uDD13 Unlocked';
      lockBtn.classList.toggle('locked', dragLocked);
      if (settingsPanel) settingsPanel.classList.add('hidden');
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'lock', locked: dragLocked }));
      } catch (_) {}
    });
  }

  if (opacitySlider) {
    opacitySlider.addEventListener('input', function () {
      var pct = parseInt(this.value, 10);
      opacityVal.textContent = pct + '%';
      // Map display 0–100% → actual opacity 0.30–1.00
      currentOpacity = 0.30 + (pct / 100) * 0.70;
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'opacity', value: currentOpacity }));
      } catch (_) {}
    });
  }

  function sendZoom(pct) {
    currentZoom = Math.min(100, Math.max(30, pct));
    if (zoomInput) zoomInput.value = currentZoom;
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'zoom', pct: currentZoom }));
    } catch (_) {}
  }

  function currentZoomValue() {
    if (zoomInput) {
      var v = parseInt(zoomInput.value, 10);
      if (!isNaN(v)) return v;
    }
    return currentZoom;
  }

  if (zoomDownBtn) {
    zoomDownBtn.addEventListener('click', function () { sendZoom(currentZoomValue() - 10); });
  }

  if (zoomUpBtn) {
    zoomUpBtn.addEventListener('click', function () { sendZoom(currentZoomValue() + 10); });
  }

  if (zoomInput) {
    zoomInput.addEventListener('change', function () {
      var val = parseInt(this.value, 10);
      if (isNaN(val)) val = currentZoom;
      sendZoom(val);
    });
  }

  // ---- Hotkey recorder ----
  var heldKeys = {};

  function sendHotkey() {
    var keys = Object.keys(heldKeys);
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'hotkey', keys: keys }));
    } catch (_) {}
  }

  if (hotkeyInput) {
    hotkeyInput.addEventListener('focus', function () {
      heldKeys = {};
      hotkeyInput.value = '';
      try { window.chrome.webview.postMessage(JSON.stringify({ type: 'hotkey-focus', active: true })); } catch (_) {}
    });

    hotkeyInput.addEventListener('blur', function () {
      heldKeys = {};
      try { window.chrome.webview.postMessage(JSON.stringify({ type: 'hotkey-focus', active: false })); } catch (_) {}
    });

    hotkeyInput.addEventListener('keydown', function (e) {
      e.preventDefault();
      heldKeys[e.code] = true;
      hotkeyInput.value = Object.keys(heldKeys).join(' + ');
    });

    hotkeyInput.addEventListener('keyup', function (e) {
      e.preventDefault();
      // Send when all keys released
      delete heldKeys[e.code];
      if (Object.keys(heldKeys).length === 0) {
        // Re-build from the recorded combo before clearing
        var recorded = hotkeyInput.value;
        var keys = recorded ? recorded.split(' + ') : [];
        if (keys.length > 0) {
          try { window.chrome.webview.postMessage(JSON.stringify({ type: 'hotkey', keys: keys })); } catch (_) {}
        }
        hotkeyInput.blur();
      }
    });
  }


  if (minimizeSelect) {
    minimizeSelect.addEventListener('change', function () {
      try {
        window.chrome.webview.postMessage(JSON.stringify({
          type: 'minimizeMode',
          value: minimizeSelect.value,
        }));
      } catch (_) {}
    });
  }

  // ---- Miniplayer Settings sub-panel ----
  // Swaps in over the main settings panel; the banner becomes interactable while
  // open so the user can drag it (when unlocked) and see resize/opacity changes
  // applied live.
  var miniplayerBtn        = document.getElementById('miniplayer-settings-btn');
  var miniplayerPanel      = document.getElementById('miniplayer-settings-panel');
  var miniplayerBackBtn    = document.getElementById('miniplayer-back-btn');
  var bannerLockBtn        = document.getElementById('banner-lock-btn');
  var bannerOpacitySlider  = document.getElementById('banner-opacity-slider');
  var bannerOpacityVal     = document.getElementById('banner-opacity-val');
  var bannerScaleInput     = document.getElementById('banner-scale-input');
  var bannerScaleDownBtn   = document.getElementById('banner-scale-down-btn');
  var bannerScaleUpBtn     = document.getElementById('banner-scale-up-btn');
  var bannerResetBtn       = document.getElementById('banner-reset-btn');

  var bannerLocked = true;

  function postBannerEdit(active) {
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'bannerEditMode', value: active }));
    } catch (_) {}
  }

  function updateBannerLockBtn() {
    if (!bannerLockBtn) return;
    bannerLockBtn.textContent = bannerLocked ? '\uD83D\uDD12 Banner locked' : '\uD83D\uDD13 Banner unlocked';
    bannerLockBtn.classList.toggle('locked', bannerLocked);
  }

  function showMiniplayerPanel() {
    if (settingsPanel) settingsPanel.classList.add('hidden');
    if (miniplayerPanel) miniplayerPanel.classList.remove('hidden');
    postBannerEdit(true);
  }

  function hideMiniplayerPanel() {
    if (miniplayerPanel) miniplayerPanel.classList.add('hidden');
    if (settingsPanel) settingsPanel.classList.remove('hidden');
    postBannerEdit(false);
  }

  if (miniplayerBtn) {
    miniplayerBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      showMiniplayerPanel();
    });
  }

  if (miniplayerBackBtn) {
    miniplayerBackBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      hideMiniplayerPanel();
    });
  }

  if (bannerLockBtn) {
    bannerLockBtn.addEventListener('click', function () {
      bannerLocked = !bannerLocked;
      updateBannerLockBtn();
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'bannerLock', locked: bannerLocked }));
      } catch (_) {}
    });
  }

  if (bannerOpacitySlider) {
    bannerOpacitySlider.addEventListener('input', function () {
      var pct = parseInt(this.value, 10);
      if (bannerOpacityVal) bannerOpacityVal.textContent = pct + '%';
      // Map display 20-100 → actual opacity 0.20-1.00 directly.
      var v = pct / 100;
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'bannerOpacity', value: v }));
      } catch (_) {}
    });
  }

  function sendBannerScale(pct) {
    var clamped = Math.min(120, Math.max(20, pct));
    if (bannerScaleInput) bannerScaleInput.value = clamped;
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'bannerScale', value: clamped }));
    } catch (_) {}
  }

  function currentBannerScale() {
    if (bannerScaleInput) {
      var v = parseInt(bannerScaleInput.value, 10);
      if (!isNaN(v)) return v;
    }
    return 100;
  }

  if (bannerScaleDownBtn) {
    bannerScaleDownBtn.addEventListener('click', function () { sendBannerScale(currentBannerScale() - 10); });
  }
  if (bannerScaleUpBtn) {
    bannerScaleUpBtn.addEventListener('click', function () { sendBannerScale(currentBannerScale() + 10); });
  }
  if (bannerScaleInput) {
    bannerScaleInput.addEventListener('change', function () {
      var v = parseInt(this.value, 10);
      if (isNaN(v)) v = 100;
      sendBannerScale(v);
    });
  }

  if (bannerResetBtn) {
    bannerResetBtn.addEventListener('click', function () {
      try {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'bannerReset' }));
      } catch (_) {}
    });
  }

  // ---- Streamer Info sub-panel ----
  // Static OBS-setup walkthrough plus a Streamer Mode toggle that gates the
  // AudioBridge service (default off — non-streamers don't want the duplicate
  // audio path).
  var streamerBtn        = document.getElementById('streamer-settings-btn');
  var streamerPanel      = document.getElementById('streamer-settings-panel');
  var streamerBackBtn    = document.getElementById('streamer-back-btn');
  var streamerModeToggle = document.getElementById('streamer-mode-toggle');

  function showStreamerPanel() {
    if (settingsPanel) settingsPanel.classList.add('hidden');
    if (streamerPanel) streamerPanel.classList.remove('hidden');
  }

  function hideStreamerPanel() {
    if (streamerPanel) streamerPanel.classList.add('hidden');
    if (settingsPanel) settingsPanel.classList.remove('hidden');
  }

  if (streamerBtn) {
    streamerBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      showStreamerPanel();
    });
  }

  if (streamerBackBtn) {
    streamerBackBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      hideStreamerPanel();
    });
  }

  if (streamerModeToggle) {
    streamerModeToggle.addEventListener('change', function () {
      try {
        window.chrome.webview.postMessage(JSON.stringify({
          type: 'streamerMode',
          enabled: !!streamerModeToggle.checked,
        }));
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
    if (streamerPanel && !streamerPanel.classList.contains('hidden')) {
      if (!streamerPanel.contains(e.target) && e.target !== settingsBtn) {
        streamerPanel.classList.add('hidden');
      }
    }
  });

  // Frame drag — mousedown on non-interactive frame areas tells C# to start drag.
  // JS is authoritative: only areas that are NOT a button, input, video, or settings
  // panel will initiate drag, so button clicks are never treated as drag starts.
  document.addEventListener('mousedown', function (e) {
    var el = e.target;
    while (el && el !== document.documentElement) {
      if (
        el.tagName === 'BUTTON'        ||
        el.tagName === 'INPUT'         ||
        el.tagName === 'A'             ||
        el.id     === 'video-wrap'     ||
        el.id     === 'settings-panel'
      ) return;
      el = el.parentElement;
    }
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'startDrag' }));
    } catch (_) {}
  });

  // Window-level lock state is synced to C# so the drag logic there can honour it.

  // ---- Initialise ----
  buildButtons();

})();
