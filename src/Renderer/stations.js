// PulseNet Player — Station Configuration
// Replace playlistId values with real YouTube playlist IDs when the channel is live.
// icon: path relative to Renderer/ folder.

window.STATIONS = [
  // === LEFT COLUMN — slots 1-9, top to bottom ===
  // live: true  → hover shows the station icon as-is
  // live: false → hover shows Offline_<filename> instead
  { id: 'l1', label: 'Alternative Routes',   playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_Alternative_Routes_logo.png',        side: 'left',  slot: 1, live: false },
  { id: 'l2', label: 'CrossWind',            playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_CrossWind_station_logo.png',           side: 'left',  slot: 2, live: false },
  { id: 'l3', label: 'DeepSky Ambient',      playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_DeepSky_Amibent_station_logo.png',     side: 'left',  slot: 3, live: false },
  { id: 'l4', label: 'EchoVerse',            playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_EchoVerse_station_logo.png',            side: 'left',  slot: 4, live: false },
  { id: 'l5', label: 'FlowState',            playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_FlowState_logo.png',                   side: 'left',  slot: 5, live: false },
  { id: 'l6', label: 'Frontline Frequency',  playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_Frontline_Frequency_station_logo.png', side: 'left',  slot: 6, live: false },
  { id: 'l7', label: 'HoloStage LIVE',       playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_HoloStage_LIVE_station_logo.png',      side: 'left',  slot: 7, live: false },
  { id: 'l8', label: 'IronChord',            playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_IronChord_logo.png',                   side: 'left',  slot: 8, live: false },
  { id: 'l9', label: 'NovaBeat',             playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_NovaBeat_logo.png',                    side: 'left',  slot: 9, live: false },

  // === RIGHT COLUMN — slots 1-9, top to bottom ===
  { id: 'r1', label: 'Pulse Retro',          playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_Pulse_Retro_station_logo.png',         side: 'right', slot: 1, live: false },
  { id: 'r2', label: 'PulseVision Audio',    playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_PulseVision_Audio_station_logo.png',   side: 'right', slot: 2, live: false },
  { id: 'r3', label: 'Quantum Drive',        playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_Quantum_Drive_logo.png',                side: 'right', slot: 3, live: false },
  // Solaris Classical: Easter egg while the broadcaster spins the station up.
  // live:false keeps the Offline_ thumbnail showing on hover so the station
  // looks offline like its siblings - clicking it still plays the videoId,
  // which is the joke. Replace with the real playlistId when Solaris goes live.
  { id: 'r4', label: 'Solaris Classical',    videoId:    'dQw4w9WgXcQ',              icon: 'assets/stations/PulseNet_-_Solaris_Classical_station_logo.png',   side: 'right', slot: 4, live: false },
  { id: 'r5', label: 'Spectrum Beats',       playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_Spectrum_Beats_logo.png',               side: 'right', slot: 5, live: false },
  { id: 'r6', label: 'Starlight Lounge',     playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_Starlight_Lounge_station_logo.png',    side: 'right', slot: 6, live: false },
  { id: 'r7', label: 'The Cargo Deck',       playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_The_Cargo_Deck_station_logo.png',      side: 'right', slot: 7, live: false },
  { id: 'r8', label: 'The Foundry',          playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_The_Foundry_logo.png',                  side: 'right', slot: 8, live: false },
  { id: 'r9', label: 'Trailstar',            playlistId: 'UUIMaIJsfJEMi5yJIe5nAb0g', icon: 'assets/stations/PulseNet_-_Trailstar_logo.png',                    side: 'right', slot: 9, live: false },
];
