<p align="center">
  <img src="src/Renderer/assets/main_logo.png" alt="PulseNet Player" width="420">
</p>

<p align="center">
  <em>Entertainment Division of The Exelus Corporation</em><br><br>
  <strong>"The 'Verse always has a soundtrack."</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 9">
  <img src="https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square&logo=windows" alt="Windows">
  <img src="https://img.shields.io/badge/WebView2-Chromium-green?style=flat-square&logo=googlechrome" alt="WebView2">
  <img src="https://img.shields.io/github/license/Diftic/PulseNet-Player?style=flat-square" alt="License">
</p>

---

<p align="center">
  <img src="docs/preview.png" alt="PulseNet Player overlay in action" width="800">
</p>

---

**PulseNet Player** is an always-on-top Windows overlay that brings the Pulse Broadcasting Network directly into your Star Citizen session. A sci-fi framed YouTube music player that lives at the edge of your screen, 19 stations, one click away, never interrupting your game.

Tune in while you fly. Keep it on while you fight. Let it run while you haul. PulseNet is always broadcasting.

---

## Features

- **Sci-fi overlay frame** — Transparent, always-on-top window styled to fit the Star Citizen aesthetic
- **19 stations** — A full lineup spanning electronic, classical, industrial, country, hip-hop, ambient, live performances, and more
- **One-click playback** — Station selector buttons on both sides of the frame; click to tune in instantly
- **Station hover preview** — Hover over any station button to preview its cover art inside the video window
- **Adjustable opacity** — Fade the overlay to any level without losing interactivity
- **Resizable window** — Scale from 20% to 100% of the full frame size; window resizes with content
- **Draggable frame** — Reposition anywhere on screen by dragging the frame border
- **Position memory** — The window remembers exactly where you left it between sessions
- **Configurable hotkey** — Toggle the overlay on/off with any key combination you choose
- **Lock mode** — Lock the overlay in place to prevent accidental repositioning
- **Global scroll passthrough** — Scroll wheel is only consumed when the cursor is over the overlay
- **System tray integration** — Minimize to tray; toggle via hotkey or right-click tray menu

---

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) *(pre-installed on most Windows 11 machines)*
- An active internet connection (streams from YouTube)

---

## Installation

Two download options on the [Releases page](https://github.com/Diftic/PulseNet-Player/releases):

- **`PulseNet-Setup.msi`** — Per-user installer. Adds a Start Menu shortcut, a desktop icon, and wires up auto-update. Recommended.
- **`PulseNet-Player.exe`** — Standalone launcher. Run it directly from any folder, no installation.

Either way, launch the app and the overlay appears immediately. Default toggle hotkey is **F9**.

> Settings are stored in `%APPDATA%\pulsenet-radio\`. Uninstalling the MSI leaves your settings behind; delete the folder manually if you want a clean slate.

---

## Usage

| Action | How |
|--------|-----|
| Show / hide overlay | Default hotkey: **F9**, or right-click the tray icon |
| Switch station | Click any station button on the left or right column |
| Tune to live stream | Click the **PulseNet** button (top-left corner) |
| Move the window | Drag any part of the frame border |
| Resize | **Settings Menu** → `−10%` / `+10%` size buttons |
| Change opacity | **Settings Menu** → opacity slider |
| Change toggle hotkey | **Settings Menu** → hotkey field → press your combo |
| Lock position | **Settings Menu** → **Lock / Unlock** |
| Exit | Right-click tray icon → **Quit** |

---

## Station Lineup

PulseNet broadcasts **19 stations** across the full spectrum of the 'Verse. Each station has its own sound, its own DJ, and its own story.

### Left Column

| Station | Genre | DJ | Tagline |
|---------|-------|----|---------|
| **PulseNet LIVE** | All Channels · Mixed | — | *"Always broadcasting."* |
| **Alternative Routes** | Alt-Rock · Indie | Eon Lark | *"Find a different path."* |
| **CrossWind** | Cultural Fusion · World Music | Amara Kade | *"Where worlds meet in rhythm."* |
| **DeepSky Ambient** | Ambient · Chillwave | Sera Nyx | *"Drift beyond the noise."* |
| **EchoVerse** | Indie · Emerging Artists | Juno Mirai | *"New voices. New signals."* |
| **FlowState** | Hip-Hop · Rap · Trap | Vector Halden | *"Stay in the flow."* |
| **Frontline Frequency** | Military · Ceremonial | Cmdr. Elias Rourke | *"Honor in every signal."* |
| **HoloStage LIVE** | Live Performances · Concerts | — | *Broadcast live from the stage.* |
| **IronChord** | Rock · Industrial · Metal | Viktor Halden | *"Forged in Sound."* |
| **NovaBeat** | Electronic · Synthwave | Orion Vale | *"Feel the Pulse."* |

### Right Column

| Station | Genre | DJ | Tagline |
|---------|-------|----|---------|
| **Pulse Retro** | Historic Archives · Classics | Elias Varn | *"The past, still playing."* |
| **PulseVision Audio** | Soundtrack · Cinematic | Ren Blackfeather | *"Hear the story."* |
| **Quantum Drive** | High-Energy Electronic · Racing | Kade "Vectorline" Renn | *"Engage the rhythm."* |
| **Solaris Classical** | Classical · Orchestral | Aria Solenne | *"Timeless. Boundless."* |
| **Spectrum Beats** | Pop · Chart-Toppers | Cassian Vire | *"Trending across the 'Verse."* |
| **Starlight Lounge** | Jazz · Lounge | Liora Venn | *"Ease into the night."* |
| **The Cargo Deck** | Spacer Talk · Mixed | Luca Bardan | *"You're not flying solo."* |
| **The Foundry** | Industrial · Rhythmic | Marik Thorne | *"Keep the line moving."* |
| **Trailstar** | Frontier Country · Spacer Folk | Cassidy "Dustline" Rourke | *"Songs for the Long Run."* |

---

## Architecture

PulseNet Player is a **.NET 9 WPF** application. All visual elements, the sci-fi frame, station buttons, YouTube player, and settings panel, live inside a **WebView2** (Chromium) instance rendered as HTML/CSS/JS. This design sidesteps the WPF airspace problem where WebView2's HWND would otherwise always render above WPF visuals, making a native frame impossible.

### Key Components

| Component | Location | Purpose |
|-----------|----------|---------|
| Overlay window | `src/UI/OverlayWindow.xaml.cs` | WPF host, hotkeys, drag engine, tray icon |
| Renderer | `src/Renderer/` | HTML/CSS/JS UI — frame, buttons, player |
| Station config | `src/Renderer/stations.js` | All 19 stations: playlist IDs, icons, live flags |
| Player controller | `src/Renderer/player.js` | YouTube IFrame API, settings bridge to C# |
| Constants | `src/Constants.cs` | Frame dimensions, default values |
| Settings model | `src/Models/PulsenetSettings.cs` | Persisted user preferences (JSON) |

### How It Works

**Virtual host:** The renderer is served over `https://pulsenet.local/` via WebView2's `SetVirtualHostNameToFolderMapping`. This satisfies the YouTube IFrame API's HTTPS requirement without needing a real web server.

**Drag system:** JavaScript detects `mousedown` on non-interactive frame areas and sends a `startDrag` message to C# via `window.chrome.webview.postMessage`. A `WH_MOUSE_LL` low-level mouse hook handles `MOUSEMOVE` and `LBUTTONUP` to move the window. The hook is only installed while the overlay is visible — zero system-wide impact when hidden.

**Zoom:** The `ApplyZoom` method resizes the WPF window proportionally and sets `WebView.ZoomFactor` in the same call. CSS zoom is not used — this ensures correct hit areas and no visual blink at all zoom levels.

**Settings bridge:** JS posts JSON messages to C# (`{ type: 'opacity', value: 0.8 }`, `{ type: 'zoom', pct: 80 }`, etc.). C# handles each message type in `WebMessageReceived`.

---

## Adding a Station / Going Live

All station configuration is in `src/Renderer/stations.js`.

### Wiring a real playlist

```js
{
  id: 'l1',
  label: 'Alternative Routes',
  playlistId: 'YOUR_PLAYLIST_ID',         // ← replace with real playlist ID
  icon: 'assets/stations/PulseNet_-_Alternative_Routes_logo.png',
  side: 'left',
  slot: 1,
  live: true                              // ← set true when live
}
```

### Live flag behaviour

| Flag | Hover preview shown |
|------|---------------------|
| `live: false` | `Offline_<icon filename>` — greyed-out offline version |
| `live: true` | `<icon filename>` — full-colour station art |

### Station icon files

All icons live in `src/Renderer/assets/stations/`. Each station requires two files:

- `PulseNet_-_<StationName>_logo.png` — Full-colour (used when live)
- `Offline_PulseNet_-_<StationName>_logo.png` — Dimmed version (used when offline)

> **Important:** After editing files in `src/Assets/`, copy them to `src/Renderer/assets/` before rebuilding. The build process copies from `Renderer/` — not from `Assets/`.

### Adding a new station

1. Add an entry to `window.STATIONS` in `stations.js`
2. Place both icon files (`live` and `Offline_`) in `src/Renderer/assets/stations/`
3. Build and run

---

## Roadmap

- [ ] Wire real YouTube playlist IDs for all 19 stations
- [ ] Info / About dialog
- [x] Volume control — provided by the embedded YouTube player
- [x] "Now Playing" info — track title shown inside the YouTube embed
- [x] Auto-update mechanism (in-app check + self-install)
- [x] GitHub Actions CI — build + publish on tag push
- [x] WiX installer (`PulseNet-Setup.msi`) + portable `.exe`

---

## In-Universe Lore

**Pulse Broadcasting Network (PulseNet)** is the Entertainment Division of The Exelus Corporation — one of the United Empire of Earth's largest entertainment broadcast networks, founded in 2905. With over 50 years of operation spanning the full breadth of UEE space, PulseNet reaches listeners from the core worlds to the outermost frontier.

Every station in the lineup is staffed, curated, and voiced by characters with real histories rooted in the 'Verse. Eon Lark chases sound on Alternative Routes. Dustline remembers every cargo run on Trailstar. Marik Thorne keeps the line moving on The Foundry. Commander Elias Rourke honors the fallen on Frontline Frequency.

Whatever you're doing out there — PulseNet is with you.

> *"The 'Verse always has a soundtrack."*

---

## Building from Source

```bash
git clone https://github.com/Diftic/PulseNet-Player.git
cd PulseNet-Player
dotnet build src/pulsenet.csproj
dotnet run --project src/pulsenet.csproj
```

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).

---

## License

This project is released under the [MIT License](LICENSE).

*Star Citizen® is a registered trademark of Cloud Imperium Rights LLC. PulseNet Player is a fan-made project and is not affiliated with or endorsed by Cloud Imperium Games.*

---

<p align="center">
  Built by The Exelus Corporation &nbsp;·&nbsp; Broadcast across the 'Verse
</p>
