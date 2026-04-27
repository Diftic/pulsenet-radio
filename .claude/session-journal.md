# Session Journal

A living journal that persists across compactions. Captures decisions, progress, and context.

## Current State
- **Focus:** Implementing OBS Browser Source feature — exposes `/banner` and `/player` over a localhost HTTP server so streamers can add PulseNet to OBS without a Display Capture. Adds "Streamer Options" sub-panel in settings with configurable port.
- **Blocked:** Real playlist IDs for 18 stations not yet provided. `frame_glow.png` asset not yet created.

## Log

### 2026-04-27 15:06 — Plan + Started: OBS Browser Source feature
- Tester reported PulseNet Player invisible to OBS Window/Game Capture (only Display Capture works).
- Root cause confirmed in `src/UI/OverlayWindow.xaml.cs:171-175` — explicit `WS_EX_TOOLWINDOW` set in `OnSourceInitialized` "to hide from Alt+Tab". Combined with XAML `ShowInTaskbar="False"` (which already auto-applies the same flag) and the layered/transparent style, OBS's window enumerator filters it out. Banner has the same pattern.
- Decision: don't strip `WS_EX_TOOLWINDOW` (would break Alt+Tab hide UX). Instead, expose `/banner` and `/player` over an embedded localhost HTTP server so streamers paste a URL into OBS Browser Source. Sidesteps the window-capture problem entirely; also gives transparent compositing over game capture for free (Browser Source supports alpha).
- Architecture:
  - `Services/NowPlayingState.cs` — singleton title+station holder, `Changed` event. Replaces direct overlay→banner event chain.
  - `Services/BrowserSourceServer.cs` — `IHostedService` using `TcpListener` (chosen over `HttpListener` to avoid URL ACL elevation), bound to `127.0.0.1:<port>`. Routes: `/`, `/banner`, `/player`, `/events` (SSE), `/assets/*`. Rebinds when port changes.
  - `Renderer/obs/{banner,player}.{html,css,js}` — streamer-tailored variants, no chrome/settings/drag, transparent background. Banner consumes `/events` via `EventSource`. Player embeds the configured channel's `live_stream` iframe behind `frame_base.png` overlay.
  - `Renderer/index.html` + `style.css` + `player.js` — new "Streamer Options" sub-panel mirrors the Miniplayer Settings pattern (`#streamer-settings-panel`, Back button). Contents: port number input, banner URL + Copy, player URL + Copy, status indicator.
- Defaults: port 17328 (configurable), server on by default, bind 127.0.0.1 only.
- Known risk: YouTube `live_stream` embed may behave differently when loaded from `127.0.0.1` referrer vs `pulsenet.local`. Will start with plain HTTP and adapt if a real channel rejects.
- Out of scope this pass: TLS, on/off toggle, network exposure, current-track mirroring (player just shows the configured live channel; banner mirrors title via SSE).

### 2026-04-22 22:00 — Completed: Outer frame edge-clipping fix
- Session 6 (v0.3.1) had stretched the frame PNG (1252×670) at offset (-25, -12) so its inner cutout matched the widened video, with `overflow: hidden` on a 1202×646 `#app` clipping the overhang. The overhang turned out to contain visible bezel detail — corners/bolts/edges were being chopped on all 4 sides.
- Fix (option 1 — code-only, user chose this to preserve build integrity):
  - `src/Renderer/style.css`: `#app` 1202×646 → 1252×670. `#frame-base`/`#frame-glow` offsets `(-25,-12)` → `(0,0)`. All other absolute-positioned elements shifted `+25x, +12y`: `#video-wrap`, `#station-preview` (193,88)→(218,100); `.station-col` top 80→92; `#stations-left` (37,129)→(62,141); `#stations-right` left 966→991; `#pulsenet-home-btn` (118,80)→(143,92); `#about-btn` (1049,521)→(1074,533); `#settings-btn` (507,581)→(532,593); both settings panels left 348→373, bottom 62→74.
  - `src/Constants.cs`: `FrameDisplayWidth/Height` 1202/646 → 1252/670 so WPF window + WebView viewport grow to match (feeds `App.xaml.cs` off-screen parking and `OverlayWindow.ApplyZoom`).
- Testing gotcha: `dotnet run --no-build` hit the Mutex because a stale v0.3.1 binary from Apr 18 was running from `bin/x64/Debug/net9.0-windows/PulseNet-Player.exe` (no `win-x64/` subpath) — that's why the splash was showing "v0.4.0 available". Killed PID and rebuilt with `dotnet build -a x64`; fresh bits land in `bin/x64/Debug/net9.0-windows/win-x64/`. Launched that exe directly; user confirmed fix looks good.

### 2026-04-18 — Completed: v0.3.1 video + frame refit
- Removed `transform: scale(1.055)` from YouTube iframe → video plays uncropped
- `#video-wrap` reshaped to 812×457 (true 16:9), top=88, centered at y=316.5 so station buttons don't move
- Frame scaled to 1252×670, offset (-25, -12) — cutout aligns with new video rect (user confirmed "PERFECT")
- `#click-blocker` height 50px → 60px to fully cover YouTube end-card controls
- Bumped csproj Version 0.3.0 → 0.3.1 (Version / FileVersion / AssemblyVersion)
- Pushed `4768eb3` to master, tagged `v0.3.1`, workflow run id `24588476912` queued
- README install section rewritten (MSI + standalone exe); roadmap trimmed (auto-update / CI / WiX installer ticked off)
- DEVLOG.md got Session 6 entry; TODO.md updated to reflect completed distribution items

### 2026-04-17 — Completed: Rebrand Radio to Player
- Directive from PulseNet owner: never use the word "Radio" in any circumstance. Player = "PulseNet Player", service = "PulseNet Broadcasting", full corp = "Pulse Broadcasting Network", ticker = PLSN.
- Renamed binary `PulseNet-Broadcaster.exe` → `PulseNet-Player.exe` (csproj AssemblyName, workflow copy+upload, installer target, UpdateChecker asset lookup, SelfUpdateService paths)
- Renamed GitHub repo `Diftic/pulsenet-radio` → `Diftic/PulseNet-Player` (via `gh repo rename`); local git remote updated; UpdateChecker API URL + UA updated
- Scrubbed "Pulsenet Radio" → "PulseNet Player" across Constants, WPF titles, error strings, renderer title/comments, installer product/shortcut/dir/regkey/description, license copyright, README/DEVLOG/TODO titles and prose
- Scrubbed "Cargo Deck Radio" → "The Cargo Deck" in RP lore
- Renamed `RadioPlan.md` → `PulseNetPlan.md` and `src/Assets/radio_background.png` → `src/Assets/pulsenet_background.png`; scrubbed content references
- `%APPDATA%\pulsenet-radio\` folder intentionally preserved (exempted per user decision to avoid migrating beta testers' saved settings)
- Build green, pushed as commit `0a0ab81`; GitHub Pages will redeploy the sales page

### 2026-04-14 17:00 — Completed: Session 2 UI polish
- Drag rebuilt: JS-initiated `startDrag` → C# hook handles MOUSEMOVE/LBUTTONUP. Whole frame draggable.
- Zoom resizes WPF window + WebView.ZoomFactor in same call — no blink, correct hit areas at all zoom levels
- Zoom slider replaced with -10%/input/+10% buttons
- Settings panel doubled in size (font, padding, button heights)
- Window position persists between open/close cycles
- Scroll wheel fix: only captured when cursor is inside overlay window rect
- Frame width trimmed iteratively: 1258 → 1238 → 1218 → 1202px (28px each side)
- Video iframe scaled via transform: scale(1.055) to fill 16:9 pillarbox bars
- Station videoId support added; top-left station wired to test video b-YcZMSKqeo
- Real channel wired: UCIMaIJsfJEMi5yJIe5nAb0g (all 18 station playlists → UUIMaIJsfJEMi5yJIe5nAb0g)
- icon.ico replaced with Pulsenet branding (16/32/48/256px from PulseNetIcon 1024x1024.png)
- main_logo.png replaced with resized Pulsenet logo for idle state and splash
- DEVLOG.md and TODO.md updated

### 2026-04-14 15:00 — Completed: Button layout locked
- Pixel-tuned station buttons to final spec: 38×38px, scale(1.23), gap 17px, top 96px
- Center-anchored (justify-content: center) — button 5 of 9 is the prime anchor
- Left column x=45, right column x=987 (both inset from frame edges toward center)
- WebView2 disk cache disabled (--disk-cache-size=0) — no more manual cache wipe needed

### 2026-04-14 14:00 — Completed: Modules 0-2 sci-fi frame overlay rebuild
- Rebuilt renderer from scratch: frame_base.png overlay, YouTube iframe at video rect, 18 station buttons
- Window changed from fullscreen to fixed 1258×646 (frame canvas at 50% scale)
- All station buttons wired to @Mr_Xul test channel (UCDemStdcwUHbqhD2ePbKH6A) with placeholder icons
- Build: green — 0 errors, 0 warnings

### 2026-04-14 13:00 — Decision: stay WPF, discard Python rewrite
- PulseNetPlan.md (formerly RadioPlan.md) proposed PyQt6 rewrite, no technical justification found
- WPF prototype already has working WebView2, hotkeys, transparency, tray, settings
- All visual layers live in HTML/CSS/JS renderer to avoid WPF airspace problem
