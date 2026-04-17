# Session Journal

A living journal that persists across compactions. Captures decisions, progress, and context.

## Current State
- **Focus:** v0.3.1 released. Video now renders at natural 16:9 (no crop), frame resized to 1252×670 to match, click-blocker bumped to 60px. README/DEVLOG/TODO refreshed. Build & Release workflow triggered by `v0.3.1` tag.
- **Blocked:** Real playlist IDs for 18 stations not yet provided. `frame_glow.png` asset not yet created. GitHub Pages not yet enabled for sales page.

## Log

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
