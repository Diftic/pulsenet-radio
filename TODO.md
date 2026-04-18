# PulseNet Player — TODO

---

## Done ✓

- [x] Replace `Assets/icon.ico` with Pulsenet branding (multi-res .ico from PulseNetIcon 1024x1024.png)
- [x] Replace splash screen logo with Pulsenet branding (`main_logo.png`)
- [x] Replace idle video logo with Pulsenet branding (`main_logo.png` 812×433)
- [x] Wire real Pulsenet channel ID (`UCIMaIJsfJEMi5yJIe5nAb0g`)
- [x] Rebuild drag system — JS-initiated via `startDrag`, whole frame draggable except buttons/video/settings
- [x] Zoom resizes window (not CSS zoom) — no blink, correct hit areas
- [x] Zoom slider replaced with -10%/input/+10% buttons
- [x] Settings panel doubled in size
- [x] Window position persists between open/close
- [x] Scroll wheel only captured when cursor is over overlay
- [x] Frame width trimmed to 1202px (28px each side) to better fit frame_base.png
- [x] Video iframe scaled (transform: scale(1.055)) to fill 16:9 pillarbox bars
- [x] Station `videoId` support alongside `playlistId` for single-video stations
- [x] Top-left station wired to test video for playback verification
- [x] PulseNet home button (top-left) — plays live stream, uses pulsenet_icon.png
- [x] Info button (bottom-right) — text label, shows info.png on hover, wired to `{type:'about'}`
- [x] Station buttons resized to 32×32px DOM
- [x] Button columns centred so mid-gap between items 5 and 6 aligns to video vertical centre
- [x] Station hover preview masked by frame (z-index 1, behind frame overlay)
- [x] Offline station images — `live` flag in stations.js, auto `Offline_` prefix on hover when offline
- [x] Mouse hook only active while overlay is visible — no global scroll impact when hidden
- [x] Rebrand from "PulseNet Radio" to "PulseNet Player" (binary, repo, UI, docs, lore)
- [x] v0.3.1 — video renders at natural 16:9 (812×457), no crop; frame resized to 1252×670 to fit; click-blocker bumped to 60px

## Stations

- [ ] Wire real playlist IDs for all 18 stations — set `live: true` and replace placeholder `playlistId` per station as they go live
- [x] Confirm station icons are final and correctly matched to stations
- [ ] Add `frame_glow.png` asset for glow animation layer

## Player features

- [x] Volume control — handled by the YouTube embed's own controls
- [ ] **Dedicated volume control** — tester feedback: YouTube embed controls aren't enough, users are reaching for the Windows mixer. Add an in-overlay volume slider/knob.
- [x] "Now Playing" info — track title rendered inside the YouTube embed
- [x] Shuffle — configured per station on the source YouTube channel/playlist
- [x] Loop — configured per station on the source YouTube channel/playlist

## Tester feedback (2026-04-18 beta)

- [x] **Recover from off-screen / tiny window** — tray menu now exposes "Reset window position" which restores zoom to 100%, recenters on the primary monitor, and saves. Hotkey-based reset still TBD if needed.
- [ ] **F9 toggle disappears the window when small** — pressing F9 sometimes makes it vanish entirely, especially at 20% zoom. Investigate visibility toggle interaction with very small window sizes.
- [ ] **Drag offset on high-DPI monitors** — on a 125% scaled monitor the window lands ~50px to the right of where it was dropped; works fine on a 100% monitor. DPI-awareness bug in the JS-initiated drag math.
- [x] **Click-blocker on PulseNet LIVE button** — added `#click-blocker-tl` (645×60) over the top-left of the video, neutralising YouTube's channel/title link so it can no longer spawn external browser players. Volume + settings controls (bottom-right of YouTube chrome) remain reachable.
- [ ] **Investigate F-key fullscreen toggle** — tester reports F toggles a maximized YouTube view; not reproducible on Mallachi's local setup. Confirm whether this is a YouTube embed shortcut leaking through, and decide whether to suppress it.

## Settings

- [x] Update `UpdateChecker.cs` GitHub URL — points at `Diftic/PulseNet-Player`

## Distribution

- [x] **Establish GitHub repo** — https://github.com/Diftic/PulseNet-Player
- [x] README.md — full project overview, 19-station lineup, architecture, developer guide
- [x] Sales page — `docs/index.html`, matches SC-HUD design, Coming Soon CTA, no GitHub links
- [x] **Auto-update feature** — `UpdateChecker` + `SelfUpdateService` ported from SC-HUD
- [x] GitHub Actions CI — `.github/workflows/build.yml` builds exe + MSI on `v*` tag push
- [x] WiX installer — `installer/installer.wxs`, publishes `PulseNet-Setup.msi`
- [x] Stable asset naming — `PulseNet-Player.exe` + `PulseNet-Setup.msi`
- [x] **Enable GitHub Pages** — live at https://diftic.github.io/PulseNet-Player/ (branch: master, folder: /docs)
- [ ] **External beta testing** — 60-day public beta starting 2026-04-18; bump to **v1.0.0** on/after 2026-06-17 for the stable, out-of-beta release

## Pending verification

- [ ] **Hover-to-control hotkeys on API stations** — Space, M, ←/→, ↑/↓ wired via global keyboard hook + YT IFrame API; gated on `_cursorOverVideo` (mouseenter on `#video-wrap`) and `_canForwardKeys` (API player ready, not live-stream). Not testable until a non-live playlist station is wired. Live stream (`PulseNet LIVE`) has no IFrame API surface so it intentionally falls through to the click-then-keys flow.
- [ ] **Hover-to-control on live streams** — would require either swapping the raw live-stream `<iframe>` for a `YT.Player`-wrapped equivalent, or synthesising focus/click via SendInput. Punted until base hover-to-control is verified on an API station.

## Known issues / notes

- **SteelSeries Sonar shows "MSEDGEWEBVIEW2"** — Audio is produced by WebView2 child processes (`msedgewebview2.exe`). `IAudioSessionControl::SetDisplayName` works for standard Windows mixers but Sonar reads the process executable name directly. No programmatic fix. Workaround: add a custom app entry in Sonar's mixer UI.



- `frame_glow.png` referenced in HTML but file not yet created — `onerror` hides it gracefully
- YouTube share/logo buttons can't be removed (cross-origin iframe + ToS), but `#click-blocker` (60px transparent div over the bottom of the video rect) absorbs the clicks so they can't be interacted with
- YouTube IFrame API does not fire a title-change event — `getVideoData().title` is polled every 2s while playing
- WebView2 user data shared across all virtual-host pages — auth persists across restarts
- If Renderer folder missing at runtime, overlay shows a navigation error page with helpful message
- `SelfUpdateService.ApplyMsiAsync` references `pulsenet_update` temp dir — matches naming
