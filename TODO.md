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
- [x] "Now Playing" info — track title rendered inside the YouTube embed
- [x] Shuffle — configured per station on the source YouTube channel/playlist
- [x] Loop — configured per station on the source YouTube channel/playlist

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

## Known issues / notes

- **SteelSeries Sonar shows "MSEDGEWEBVIEW2"** — Audio is produced by WebView2 child processes (`msedgewebview2.exe`). `IAudioSessionControl::SetDisplayName` works for standard Windows mixers but Sonar reads the process executable name directly. No programmatic fix. Workaround: add a custom app entry in Sonar's mixer UI.



- `frame_glow.png` referenced in HTML but file not yet created — `onerror` hides it gracefully
- YouTube share/logo buttons cannot be removed — cross-origin iframe restriction + ToS
- YouTube IFrame API does not fire a title-change event — `getVideoData().title` is polled every 2s while playing
- WebView2 user data shared across all virtual-host pages — auth persists across restarts
- If Renderer folder missing at runtime, overlay shows a navigation error page with helpful message
- `SelfUpdateService.ApplyMsiAsync` references `pulsenet_update` temp dir — matches naming
