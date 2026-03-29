/* ============================================================
   Pulsenet Radio — YouTube IFrame API player controller
   ============================================================ */

(function () {
  'use strict';

  // ---- Read channel ID from URL query param ----
  const params   = new URLSearchParams(window.location.search);
  const channelId = params.get('channelId') || '';

  // YouTube uploads playlist = replace first 2 chars of channel ID (UC→UU).
  function uploadsPlaylistId(chId) {
    if (!chId || chId.length < 2) return '';
    return 'UU' + chId.slice(2);
  }

  // ---- DOM refs ----
  const playerDiv     = document.getElementById('player');
  const trackTitle    = document.getElementById('track-title');
  const btnPlay       = document.getElementById('btn-play');
  const btnPrev       = document.getElementById('btn-prev');
  const btnNext       = document.getElementById('btn-next');
  const btnRev        = document.getElementById('btn-rev');
  const btnFwd        = document.getElementById('btn-fwd');
  const playlistInput = document.getElementById('playlist-input');
  const btnLoad       = document.getElementById('btn-load-playlist');
  const statusLine    = document.getElementById('status-line');

  let player = null;
  let playerReady = false;

  // ---- Show placeholder if no channel configured ----
  if (!channelId) {
    playerDiv.innerHTML =
      '<div id="no-channel">' +
        '<span>No YouTube channel configured.</span>' +
        '<span class="hint">Open Settings (gear icon) and enter a Channel ID.</span>' +
      '</div>';
    setStatus('Enter a YouTube Channel ID in Settings to get started.');
  }

  // ---- Load YouTube IFrame API ----
  const tag = document.createElement('script');
  tag.src = 'https://www.youtube.com/iframe_api';
  document.head.appendChild(tag);

  // Called by the API when it is ready.
  window.onYouTubeIframeAPIReady = function () {
    if (!channelId) return; // nothing to load without a channel

    const listId = uploadsPlaylistId(channelId);

    player = new YT.Player('player', {
      width: '100%',
      height: '100%',
      playerVars: {
        listType:       'playlist',
        list:           listId,
        autoplay:       0,
        controls:       1,      // keep native controls for scrubbing / volume
        rel:            0,      // no unrelated recommendations at end
        modestbranding: 1,
        origin:         window.location.origin,
      },
      events: {
        onReady:       onPlayerReady,
        onStateChange: onPlayerStateChange,
        onError:       onPlayerError,
      },
    });
  };

  function onPlayerReady(event) {
    playerReady = true;
    playerDiv.classList.add('ready');
    setStatus('');
    updateTrackTitle();
  }

  function onPlayerStateChange(event) {
    const state = event.data;
    if (state === YT.PlayerState.PLAYING) {
      btnPlay.innerHTML = '&#9646;&#9646;'; // pause symbol
      btnPlay.classList.remove('paused');
      updateTrackTitle();
    } else {
      btnPlay.innerHTML = '&#9654;'; // play symbol
      if (state === YT.PlayerState.PAUSED || state === YT.PlayerState.ENDED) {
        btnPlay.classList.add('paused');
      }
    }
  }

  function onPlayerError(event) {
    setStatus('Playback error (' + event.data + '). The video may be unavailable or region-locked.');
  }

  // ---- Update track title from player metadata ----
  function updateTrackTitle() {
    if (!playerReady || !player) return;
    try {
      const data = player.getVideoData();
      if (data && data.title) {
        trackTitle.textContent = data.title;
      }
    } catch (_) {}
  }

  // Poll title every 2 s while playing (the API doesn't fire an event for title changes).
  setInterval(function () {
    if (playerReady && player && player.getPlayerState() === 1) {
      updateTrackTitle();
    }
  }, 2000);

  // ---- Control handlers ----

  btnPlay.addEventListener('click', function () {
    if (!playerReady || !player) return;
    const state = player.getPlayerState();
    if (state === YT.PlayerState.PLAYING) {
      player.pauseVideo();
    } else {
      player.playVideo();
    }
  });

  btnPrev.addEventListener('click', function () {
    if (!playerReady || !player) return;
    player.previousVideo();
    scheduleTrackUpdate();
  });

  btnNext.addEventListener('click', function () {
    if (!playerReady || !player) return;
    player.nextVideo();
    scheduleTrackUpdate();
  });

  btnRev.addEventListener('click', function () {
    if (!playerReady || !player) return;
    const current = player.getCurrentTime();
    player.seekTo(Math.max(0, current - 30), true);
  });

  btnFwd.addEventListener('click', function () {
    if (!playerReady || !player) return;
    const current  = player.getCurrentTime();
    const duration = player.getDuration();
    player.seekTo(Math.min(duration, current + 30), true);
  });

  // ---- Playlist switcher ----

  btnLoad.addEventListener('click', loadPlaylist);
  playlistInput.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') loadPlaylist();
  });

  function loadPlaylist() {
    if (!playerReady || !player) {
      setStatus('Player not ready yet.');
      return;
    }

    const raw = playlistInput.value.trim();
    if (!raw) return;

    const id = extractPlaylistId(raw);
    if (!id) {
      setStatus('Could not extract a playlist ID from that input.');
      return;
    }

    setStatus('Loading playlist…');
    player.loadPlaylist({ listType: 'playlist', list: id });
    playlistInput.value = '';
    scheduleTrackUpdate();
  }

  // Extract a playlist ID from a full YouTube URL or a bare ID.
  function extractPlaylistId(input) {
    // Already looks like a playlist ID (starts with PL, UU, LL, etc.)
    if (/^[A-Za-z0-9_-]{13,}$/.test(input)) return input;

    // Try parsing as a URL
    try {
      const url = new URL(input.startsWith('http') ? input : 'https://' + input);
      const list = url.searchParams.get('list');
      if (list) return list;
    } catch (_) {}

    return null;
  }

  // ---- Helpers ----

  function setStatus(msg) {
    statusLine.textContent = msg;
    statusLine.classList.toggle('hidden', !msg);
  }

  function scheduleTrackUpdate() {
    // Give the player a moment to switch tracks before reading the title.
    setTimeout(updateTrackTitle, 800);
    setTimeout(updateTrackTitle, 2000);
  }

})();
