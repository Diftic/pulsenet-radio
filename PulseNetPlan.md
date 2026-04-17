# PulseNet Player Overlay, System Plan
**Project:** SC PulseNet Overlay Player  
**Author:** Mallachi  
**Date:** 2026-04-14  
**Status:** Discarded (PyQt6 rewrite did not proceed; WPF build retained)  
**Incorporates:** PulseNet Player (C# WPF) learnings + sci_fi_overlay_animation_spec.md

---

## Step 1 — System Architecture Diagram

```mermaid
graph TD
    subgraph APP["PyQt6 Application"]

        subgraph WINDOW["MainWindow"]
            MAIN["Frameless | AlwaysOnTop | Qt.Tool\nWin32 WS_EX_LAYERED on outer region\nFixed size: 960×540"]
        end

        subgraph LAYERS["Visual Layers — Z-order"]
            L0["Layer 0: frame_base QLabel\nStatic background PNG\nNo glow elements"]
            L1["Layer 1: QWebEngineView\nPositioned at video rect: 80,45 → 880,495\n800×450 px (16:9)"]buttons 
            L2["Layer 2: frame_glow QLabel\nPNG with only light elements\nTransparent background\nAnimated via QGraphicsOpacityEffect"]
            L3["Layer 3: Logo QLabel\nVisible when PlayState == IDLE\nCentered over video rect"]
            L4["Layer 4: Playlist Buttons\nLeft column + Right column\nQPushButton styled cyan/dark"]
        end

        subgraph ANIM["Glow Animation Engine"]
            PULSE["Pulse Animator\nQTimer 60fps\nbrightness = 0.6 + 0.4 × sin(t)\nPeriod: 4s"]
            TRAVEL["Traveling Highlight (Phase 2)\nSoft glow moves along edges\nLoop: ~8s"]
            FLICKER["Micro Flicker (Phase 2)\nInterval: 5–15s random\n±5–10% intensity variation"]
            CONSTRAINT["Motion Constraints\nMax 1–2 types simultaneously\nNo fast blink / hard cuts\nAnimation must not be noticeable in gameplay"]
        end

        subgraph MASK["Click-Through & Hotkey"]
            CLICKTHRU["setMask() polygon\nVideo rect only is interactive\nFrame border: click-through"]
            HOTKEY["Win32 RegisterHotKey\nToggle window show/hide\nDefault: F9"]
        end

        subgraph SCHEME["Local HTTPS Scheme Handler"]
            SCHEME_H["QWebEngineUrlSchemeHandler\nServes Renderer/ folder\nas pulsenet-local://\nRequired for YouTube IFrame API\n(must be served over HTTPS-equiv scheme)"]
        end

        subgraph YOUTUBE["YouTube IFrame Bridge"]
            IFRAME["IFrame API\ncontrols=1, rel=0\nmodestbranding=1, iv_load_policy=3\nautoplay=0"]
            CHANNEL["Channel ID — hardcoded in config.py"]
            PLAYLISTS["Playlist Map — hardcoded in config.py\nList of dicts: label + playlist_id"]
            NAV["NavigationRequestedHandler\nAllow: pulsenet-local://, youtube.com\nBlock: everything else\nKills end-screen redirects"]
            UPLOAD["Auto-uploads playlist\nUU + channelId[2:]\nDefault playlist on launch"]
            JSBRIDGE["QWebChannel\nPython → JS: loadPlaylist(id)\nJS → Python: onStateChange(state)"]
            TITLEPOLL["Track title poll\nsetInterval 2s while PLAYING\nplayer.getVideoData().title"]
        end

        subgraph STATE["State Management"]
            PLAYSTATE["PlayState enum\nIDLE | PLAYING | PAUSED | ENDED"]
            LOGOVIS["Logo visibility\nShow: IDLE\nHide: PLAYING | PAUSED"]
            GLOWSTATE["Glow intensity tied to PlayState\nIDLE: slower pulse\nPLAYING: normal pulse"]
        end

        subgraph CONFIG["config.py"]
            CFG["CHANNEL_ID\nPLAYLISTS list\nHOTKEY\nWINDOW_POSITION\nVIDEO_RECT = 80,45,800,450"]
        end

    end

    MAIN --> LAYERS
    MAIN --> MASK
    MAIN --> SCHEME
    L4 -->|playlist_id| JSBRIDGE
    JSBRIDGE -->|loadPlaylist| IFRAME
    IFRAME -->|onStateChange| PLAYSTATE
    PLAYSTATE --> LOGOVIS
    PLAYSTATE --> GLOWSTATE
    GLOWSTATE --> PULSE
    NAV --> IFRAME
    CHANNEL --> NAV
    CHANNEL --> UPLOAD
    UPLOAD --> IFRAME
    SCHEME_H --> IFRAME
    HOTKEY --> MAIN
    PULSE --> L2
    CFG --> CHANNEL
    CFG --> PLAYLISTS
    CFG --> HOTKEY
```

---

## Step 2 — Full Specification

### 2.1 Window
| Property | Value |
|---|---|
| Framework | PyQt6 + PyQtWebEngine |
| Window flags | `Qt.FramelessWindowHint \| Qt.WindowStaysOnTopHint \| Qt.Tool` |
| Size | Fixed 960×540 px (matches animation spec canvas) |
| Click-through | `setMask()` polygon — frame border click-through, video rect interactive |
| Position | Saved/restored between sessions via `config.py` |
| Hotkey | F9 — toggles window show/hide (Win32 `RegisterHotKey`) |

### 2.2 Visual Layer Stack

#### Layer 0 — frame_base
- `frame_base.png`: full frame with all structural elements, **no glow**
- Displayed as `QLabel`, pinned to window

#### Layer 1 — QWebEngineView (video)
- Position: `x=80, y=45, w=800, h=450` (matches animation spec exactly)
- Loaded via custom scheme: `pulsenet-local://index.html?channelId=...`
- Custom scheme handler (`QWebEngineUrlSchemeHandler`) serves `Renderer/` folder
- This satisfies YouTube IFrame API's HTTPS requirement without a real server
- Navigation guard blocks all non-whitelisted origins

#### Layer 2 — frame_glow (animated)
- `frame_glow.png`: only the cyan light elements on transparent background
- `QLabel` with `QGraphicsOpacityEffect`
- Animated by `GlowAnimator` (60fps `QTimer`, sine curve)
- Light zones matching background image: Top strip (L/C/R), Bottom strip (L/C/R), Left vertical, Right vertical
- Phase 1: unified pulse only
- Phase 2: traveling highlight + micro flicker

#### Layer 3 — Logo
- `QLabel` with logo PNG, centered over video rect
- Visible when `PlayState == IDLE`, hidden on PLAYING/PAUSED

#### Layer 4 — Playlist Buttons
- `QPushButton` styled: dark background, cyan border, cyan text on hover
- Left column + right column flanking the video area
- Button count determined by PLAYLISTS list in `config.py`
- Each maps to one `playlist_id`

### 2.3 Glow Animation

**Pulse (Phase 1 — required)**
```python
import math, time
brightness = 0.6 + 0.4 * math.sin(time.time() * (2 * math.pi / 4.0))
# Period: 4s | Range: 0.6–1.0 | Curve: sine
```
Applied to `QGraphicsOpacityEffect.setOpacity(brightness)`

**Traveling Highlight (Phase 2)**
- Soft gradient moves: top L→R, bottom R→L, sides top→bottom
- Loop: ~8s, peak 1.2× base glow, width 10–20px soft

**Micro Flicker (Phase 2)**
- Random trigger every 5–15s, ±5–10% intensity variation
- Must not be consciously noticeable

**Motion constraints (hard rules from animation spec)**
- Never fast-blink, never hard on/off transition
- Max 1–2 animation types simultaneously
- Video content = highest priority; animation is tertiary
- If noticeable during gameplay → too strong

### 2.4 YouTube IFrame (Renderer)

Ported directly from PulseNet Player `Renderer/` with these changes:

| Change | Reason |
|---|---|
| Remove free-input playlist row | Playlists are hardcoded buttons in Qt layer |
| Channel ID still via URL query param | Clean Python/JS boundary — same pattern as PulseNet |
| Uploads playlist auto-derivation (`UU` + `channelId[2:]`) | Keep — solid pattern proven in PulseNet |
| Track title polling every 2s | Keep — IFrame API fires no title-change event |
| `controls=1` | Keep native YT controls |
| `rel=0`, `modestbranding=1` | Keep |

### 2.5 Navigation Guard
```python
def navigationRequested(self, info):
    url = info.requestedUrl().toString()
    allowed = ["pulsenet-local://", "youtube.com", "ytimg.com", "googlevideo.com"]
    if any(a in url for a in allowed):
        info.accept()
    else:
        info.reject()  # kills end-screen redirects and off-channel links
```

### 2.6 Custom Scheme Handler
PyQtWebEngine equivalent of WebView2's `SetVirtualHostNameToFolderMapping`:
```python
class LocalSchemeHandler(QWebEngineUrlSchemeHandler):
    def requestStarted(self, job):
        # serve files from Renderer/ folder
        # maps pulsenet-local:// → ./Renderer/
```
Scheme registered as `pulsenet-local` at app startup before any profile is created.

### 2.7 Config (`config.py`)
```python
CHANNEL_ID = "UCxxxxxxxxxxxxxxxxxx"

PLAYLISTS = [
    {"label": "Label 1", "playlist_id": "PLxxxxxxxxxx"},
    {"label": "Label 2", "playlist_id": "PLxxxxxxxxxx"},
]

HOTKEY        = "F9"
WINDOW_POS    = (100, 100)
VIDEO_RECT    = (80, 45, 800, 450)   # x, y, w, h — matches animation spec
GLOW_PERIOD   = 4.0                  # seconds per pulse cycle
GLOW_MIN      = 0.6
GLOW_MAX      = 1.0
```

### 2.8 Assets Required
| Asset | Status |
|---|---|
| `frame_base.png` | Derived from `pulsenet_background.png` — glow elements removed |
| `frame_glow.png` | Derived from `pulsenet_background.png` — only light elements, transparent bg |
| `logo.png` | Required — displayed over video rect when IDLE |
| `Renderer/index.html` | Port from PulseNet — strip free-input playlist row |
| `Renderer/player.js` | Port from PulseNet — minor changes |
| `Renderer/style.css` | Port from PulseNet — adapt to fit Qt shell |

---

## Step 3 — Development Modules

### Module 0 — Project Scaffold
- Folder structure
- `config.py` (all hardcoded values + version)
- `clean.py` (removes `__pycache__/`, `*.pyc`, `*.pyo`, `build/`, `dist/`)
- `DEVLOG.md`
- `requirements.txt` (`PyQt6`, `PyQtWebEngine`, `pywin32`)

### Module 1 — Window & Background
- Frameless always-on-top PyQt6 window, fixed 960×540
- `frame_base.png` as `QLabel` background
- Position save/restore from config

### Module 2 — Click-Through Mask & Hotkey
- `setMask()` polygon: interactive region = VIDEO_RECT only
- Win32 `RegisterHotKey` for F9 toggle
- Frame border click-through

### Module 3 — Custom Scheme Handler & WebView
- Register `pulsenet-local://` scheme before profile creation
- `QWebEngineUrlSchemeHandler` serving `Renderer/` folder
- `QWebEngineView` positioned at VIDEO_RECT
- Navigate to `pulsenet-local://index.html?channelId=...`

### Module 4 — YouTube Renderer (HTML/JS/CSS)
- Port `index.html`, `player.js`, `style.css` from PulseNet
- Strip free-input playlist row from HTML/JS
- Validate: uploads auto-load, track title polling, state events working

### Module 5 — Navigation Guard & JS Bridge
- `navigationRequested` handler (whitelist)
- `QWebChannel` setup
- Python → JS: `loadPlaylist(id)`
- JS → Python: `onStateChange(state)` → `PlayState` enum update

### Module 6 — Glow Animation Engine
- `frame_glow.png` as `QLabel` above video layer
- `QGraphicsOpacityEffect` on glow label
- `GlowAnimator` class: 60fps `QTimer`, sine-based brightness
- `PlayState` modulates glow speed/intensity
- Phase 1: pulse only

### Module 7 — Logo Overlay
- Logo `QLabel` centered over VIDEO_RECT
- Wired to `PlayState`: show IDLE, hide PLAYING/PAUSED

### Module 8 — Playlist Buttons
- Left/right `QPushButton` columns
- Styled: dark + cyan glow + hover states
- Each wired to `playlist_id` from config
- Click → `loadPlaylist(id)` via JS bridge

### Module 9 — Polish & Integration
- Full integration test (overlay over game)
- Glow intensity tuning against real video
- Version bump in `config.py`
- DEVLOG sync

---

## Open Items Before Module 0

- [ ] Channel ID
- [ ] Playlist IDs + labels + count
- [ ] Logo asset
- [ ] Confirm `pulsenet_background.png` is final (need to derive base/glow PNGs)
- [ ] Hotkey confirmation (default: F9)
- [ ] Project folder path (suggest `C:\Users\larse\PycharmProjects\sc-pulsenet-overlay`)

---

## Key Learnings from PulseNet Player

| Learning | Applied How |
|---|---|
| YouTube IFrame needs HTTPS — use virtual host | `QWebEngineUrlSchemeHandler` for `pulsenet-local://` |
| `UU` prefix trick for uploads playlist auto-derivation | Carried over to `player.js` |
| Track title API fires no event — poll every 2s | Carried over to `player.js` |
| Navigation lock to virtual host + popup for OAuth | `navigationRequested` whitelist |
| Renderer (HTML/JS/CSS) fully decoupled from host | Direct port with minimal changes |
| Click-through via transparency + mask | `setMask()` polygon approach |
| Win32 hotkey hook architecture | `RegisterHotKey` pattern kept |
| `controls=1` native YT controls sufficient | Keep as-is |
| State change drives UI icon swap | Ported to Qt signal from JS bridge |
