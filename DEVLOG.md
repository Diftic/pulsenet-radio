# PulseNet Player — Dev Log

> Entertainment Division of The Exelus Corporation — "The 'Verse always has a soundtrack."

---

## 2026-04-18 — Session 6 — v0.3.1 — Natural video resolution + frame refit

### Video at natural 16:9, no crop
Prior versions used `transform: scale(1.055)` on the YouTube iframe to fill the 812×433 video container's pillarbox bars (container was 1.875:1, not true 16:9). The fill was cosmetic but chopped ~12px off the top and bottom of actual video content. Removed the scale transform entirely. Reshaped `#video-wrap` and `#station-preview` to 812×457 (true 16:9), vertically recentered at y=316.5 so all station button positions remain valid without recalculation.

### Frame refit: 1252×670
With the video rect now taller, the previous frame size (1222×656, offset -10/-5) was too small — the cutout clipped the expanded video. Scaled the frame PNG display to 1252×670 with offset (-25, -12) so the cutout aligns with the new 812×457 video area. Frame art is slightly non-uniformly stretched (1.041× wide, 1.037× tall relative to its native 1202×646 canvas ratio); not visually noticeable.

### Click-blocker — 50px → 60px
YouTube's end-card / share overlay extends slightly higher than the previous 50px absorber reached. Bumped `#click-blocker` height to 60px so the full bottom control strip is neutralised.

### Docs
README Installation section rewritten to describe both `PulseNet-Setup.msi` (per-user installer, recommended) and `PulseNet-Player.exe` (standalone launcher). Roadmap updated — auto-update, CI, and WiX installer now ticked off; remaining roadmap is volume control, Now Playing tooltip, Info dialog, and real playlist IDs.

### Release
Tagged `v0.3.1`, pushed. Build & Release workflow publishes both artifacts to the GitHub release page.

---

## 2026-04-17 — Session 5 — v0.3.0 — Discord CTA, click-blocker, tray icon cleanup

### Discord button in settings panel
Earlier iterations placed a "Join us on Discord" button above and then overlaying the video. Neither position felt right — centered above fought with the video centerline, and bottom-right overlayed YouTube's own controls. Solution: embedded the Discord wordmark button inside the settings panel, above the frame-lock row. Uses the existing `.settings-action-btn` class for identical dimensions (452×40), with the logo filling the button edge-to-edge via `object-fit: contain`. Clicks route through the existing `openUrl` WebView message → `OpenInDefaultBrowser`, opening <https://discord.com/invite/Vxn7kzzWGJ> in the user's default browser. Logo source (2260×200) resized to 904×80 (2x HiDPI) for crisp display.

### Click-blocker — YouTube overlay neutralised
Added a transparent `#click-blocker` div inside `#video-wrap`, positioned to cover the bottom 50px of the video rect (full width × 50px). No listeners, no visual feedback — just absorbs clicks so the user can't reach YouTube's "More videos" / "Share" / fullscreen overlay at the bottom. `z-index: 3` puts it above the iframe but below frame/stations/settings.

### Tray icon — dropped active-state green tint
`TrayIcon.cs` previously applied a 140-alpha green overlay (`Color.FromArgb(80, 200, 120)`) to signal "player open". Clashed with the brand cyan. Removed `_iconActive` + `TintIcon` helper + `SetActive` method entirely; removed the `OverlayShown`/`OverlayHidden` event subscriptions from `App.xaml.cs` that fed it. Tray icon now stays blue regardless of state.

### 1px video-frame gap fixed
`#video-wrap` and `#station-preview` had `top: 101px` (off by one from the file's header spec of `top=100`), producing a visible gap between the top of the video and the sci-fi frame overlay graphic. Reset to `top: 100px` to match the frame's video cutout.

---

## 2026-04-17 — Session 4 — README, sales page, station count correction

### README.md
Comprehensive project README written covering: project overview and lore, features list, requirements, installation, usage table, full 19-station lineup (both columns), architecture overview, adding stations / going live guide, roadmap, and build-from-source instructions.

### Sales page (docs/index.html)
Landing page built matching the SC-HUD design language — dark space theme, cyan accent, star field hero, animated "Coming Soon" badge. Sections: hero with screenshot, stats bar, features grid, full station lineup table, how-it-works steps, settings reference, notes, lore banner, CTA. GitHub references intentionally excluded (no download links, no repo links).

### Promotional screenshot
`src/Assets/PulseNet Player.png` added — used as the hero image in both the README (`docs/preview.png`) and the sales page.

### Station count corrected to 19
**PulseNet LIVE** (the home button, top-left) is the 19th station — plays all channels indiscriminately. Each column has 10 buttons (left: PulseNet LIVE + 9 stations; right: 9 stations + Info). All references updated across README, sales page, and station descriptions.

### SteelSeries Sonar audio session rename — investigated, not achievable
Attempted to rename the WebView2 audio session via `IAudioSessionControl::SetDisplayName` (CoreAudio COM interop) so Sonar would show "PulseNet Player" instead of "MSEDGEWEBVIEW2". Implementation was correct and works for standard Windows audio mixers (EarTrumpet, Windows Volume Mixer), but SteelSeries Sonar reads the process executable name directly and ignores the session display name. Root cause: audio is produced by `msedgewebview2.exe` child processes spawned by WebView2 — this is true regardless of how the host executable is named. No programmatic fix is possible. Code reverted cleanly.

---

## 2026-04-16 — Session 3 — Special buttons, offline states, mouse hook fix

### Special utility buttons
Two new buttons added outside the station columns:
- **PulseNet home button** (top-left): uses `pulsenet_icon.png`, plays the live stream `videoId: 'b-YcZMSKqeo'` directly in the player. Link moved here from l1 (Alternative Routes), which reverts to a standard playlist station.
- **Info button** (bottom-right): text-only ("Info", 8px Consolas), shows `info.png` on hover. Sends `{type:'about'}` to C# on click for future wiring.

### Button sizing and column alignment
- Station buttons reduced from 38×38px to 32×32px DOM (renders ~39×39px with scale 1.23).
- Columns re-centred: mid-gap between items 5 and 6 (counting special buttons as part of the column) aligned to video vertical centre (y=316.5px).
  - Left col: `top: 129px` (home button counts as item 1, stations as 2–10)
  - Right col: `top: 80px` (stations 1–9, info button as item 10)
  - Special buttons spaced one gap (17px) from the nearest station button.

### Station hover preview masked by frame
`#station-preview` z-index lowered from 4 → 1, placing it behind the frame overlay. Previously it rendered above the frame and bled outside the cutout. Now it's masked by the frame the same way the idle logo and video are.

### Offline station images
- `live` flag added to every station entry in `stations.js` (all currently `false`).
- Hover preview auto-derives `Offline_<filename>` path when `live: false`; uses regular icon when `live: true`.
- All 18 `Offline_` images created, deployed to `Renderer/assets/stations/`.
- To bring a station live: set `live: true` and replace the placeholder `playlistId`.

### Mouse hook — no longer blocks system scroll when overlay hidden
`WH_MOUSE_LL` was installed at app startup and ran for the entire process lifetime, adding latency to all mouse events globally even when the overlay was hidden. Refactored: hook is now installed in `ShowOverlay()` and uninstalled in `HideOverlay()`. Zero system-wide impact when the overlay is off.

### Minor layout tweaks
- Video window and preview rect shifted down 1px (`top: 101px`) for better frame alignment.
- Settings Menu button moved up 5px (`top: 581px`).

---

## 2026-04-14 — Session 2 — UI polish, drag overhaul, zoom, channel wired

### Drag system rebuilt (WH_MOUSE_LL → JS-initiated)
Previous drag implementation used a geometric exclusion zone in the C# hook to decide what was draggable. This broke at non-100% zoom because CSS zoom scaled visuals but not the WPF window, causing coordinate mismatches. Full rewrite:

- **JS is authoritative:** `mousedown` listener walks the DOM — if the click is not on a button, input, video area, or settings panel, it sends `{type:'startDrag'}` to C#.
- **C# hook tracks last cursor position** (volatile fields) so it can reconstruct the drag origin when the JS message arrives.
- **WH_MOUSE_LL handles MOUSEMOVE and LBUTTONUP** — moves window via `SetWindowPos(SWP_ASYNCWINDOWPOS)`, syncs WPF `Left`/`Top` on release.
- Result: entire frame is draggable except buttons, video area, and settings panel. Works correctly at all zoom levels.

### Zoom: window resizes with content
`ApplyZoom` now resizes the WPF window proportionally (`Width = FrameDisplayWidth * factor`, `Height = FrameDisplayHeight * factor`) and sets `WebView.ZoomFactor` in the same call. Previously CSS `zoom` was used, which caused blink and coordinate mismatches. Zoom range: 20–100%.

### Zoom controls replaced
Zoom slider removed. Replaced with: `-10% size` button · number input (20–100) · `+10% size` button. Eliminates blink issues the slider had.

### Settings panel doubled
Font sizes doubled (8→16px), panel width 250→500px, button heights 20→40px, border-radius 6→12px.

### Window position persistence
`HideOverlay` saves `WindowLeft`/`WindowTop` to settings. `ShowOverlay` restores them, falls back to center on first run.

### Idle logo
`main_logo.png` shown in video area when no station is active. Hidden when a station button is clicked. Restored on playlist end with no active station.

### Splash screen
Logo updated to `main_logo.png` (Pulsenet branding), resized to 225×225px. Layout anchored top with reduced margins so all content is visible.

### Scroll wheel fix
WH_MOUSE_LL scroll hook now checks if cursor is inside the overlay window rect before consuming the event. Previously captured scroll globally whenever the overlay was visible.

### Frame width trim
Frame width reduced from 1258px to 1202px (28px each side trimmed) to better fit the frame_base.png visual. All CSS x-coordinates and `Constants.FrameDisplayWidth` updated accordingly. Video area (812×433) unchanged.

### Video fill
YouTube iframe scaled via `transform: scale(1.055)` to fill pillarbox bars caused by the 812×433 container being wider than 16:9.

### Station playback
- `stations.js` updated: `videoId` property supported alongside `playlistId` for single-video stations.
- Top-left station (Alternative Routes) wired to test video `b-YcZMSKqeo`.
- All 18 station playlist IDs updated to real Pulsenet channel (`UCIMaIJsfJEMi5yJIe5nAb0g` → uploads playlist `UUIMaIJsfJEMi5yJIe5nAb0g`).
- Default `YoutubeChannelId` in settings updated to `UCIMaIJsfJEMi5yJIe5nAb0g`.

### Assets
- `icon.ico` replaced with Pulsenet branding — multi-resolution (16, 32, 48, 256px) generated from `PulseNetIcon 1024x1024.png`.
- `main_logo.png` replaced with resized (812×433) Pulsenet logo for idle state.
- Stale `logo.png` resource entry removed from `pulsenet.csproj`.

---

## 2026-04-14 — Modules 0-2 — Sci-fi frame overlay rebuild

**Architecture decision: stay WPF, not Python rewrite.**
The PulseNetPlan.md proposal to rewrite in PyQt6 was discarded, no technical justification, WebView2 is more reliable than QWebEngineView on Windows for YouTube, and the existing WPF infrastructure was already proven.

### Frame asset
- Source image: `src/Assets/pulsenet_background.png`, 2515×1292, transparent outside frame
- Video area cut to fully transparent by user
- Displayed at 50% → **1258×646** window (since trimmed to 1202×646)
- Video rect within display: `left=193 top=100 width=812 height=433`

### Renderer rebuild (everything visual lives in HTML/CSS/JS)
WPF airspace problem (WebView2 HWND always renders above WPF visuals) means the frame and buttons cannot be WPF elements. Solution: put everything in the renderer.

Layer stack (z-index):
1. YouTube iframe — `video-wrap` at video rect
2. `frame_base.png` — structural frame, `pointer-events:none`
3. `frame_glow.png` — glow overlay, CSS pulse animation, hidden until asset exists
4. Station buttons — `pointer-events:auto`, inside frame panel areas

### Station buttons
- 18 buttons: 9 left column + 9 right column
- Icon-based; hover shows full station icon preview over video area
- Defined in `Renderer/stations.js`
- Active button gets cyan glow border

### Window
- Fixed size: `Constants.FrameDisplayWidth × FrameDisplayHeight` (currently 1202×646)
- Centered on primary screen on first show; position persisted after that
- Drag handled via JS `startDrag` → C# WH_MOUSE_LL hook

---

## 2026-03-30 — v0.1.0 — Initial project setup

**Cloned from SC-HUD** and transitioned into a standalone YouTube overlay player.

### Architecture decisions

**Virtual host instead of `file://`**
YouTube IFrame API requires the host page to be served over HTTPS. WebView2's `SetVirtualHostNameToFolderMapping` maps `https://pulsenet.local/` to the local `Renderer/` folder, satisfying this requirement without a real server.

**Channel ID via URL query param**
The C# side navigates to `https://pulsenet.local/index.html?channelId=UCxxxxxx`. The JS reads `URLSearchParams` on load. When the user changes the channel ID in Settings, the app re-navigates with the new param.

**Uploads playlist auto-derivation**
Every YouTube channel has an auto-generated uploads playlist with ID `UU` + `channelId[2:]` (swap the `UC` prefix).

**OAuth/sign-in via existing popup mechanism**
Any `window.open()` from within the iframe goes through `OnNewWindowRequested`, which opens a shared-environment WebView2 popup. Auth cookies persist in `%APPDATA%\pulsenet-radio\WebView2Cache`.

### What was stripped from SC-HUD

| Removed | Reason |
|---------|--------|
| `ScAnchorService.cs` | SC-specific — no game to anchor to |
| `OverlayUrl` setting | Replaced by `YoutubeChannelId` |
| Environment toggle (Ctrl+Shift+D) | Was a prod/staging scbridge.app switcher |
| SC-specific error messages | Irrelevant |

---

## In-Universe Lore

**Pulse Broadcasting Network (PulseNet)**
Entertainment Division of The Exelus Corporation

- Founded: 2905 (51+ years of operation as of story time)
- Scope: One of the UEE's largest entertainment broadcast networks
- Tagline: *"The 'Verse always has a soundtrack."*
