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
- [x] v0.4.1 — outer frame bezel no longer clipped; `#app` grown 1202×646 → 1252×670, frame offset zeroed, all other elements shifted +25x/+12y, WPF constants bumped to match
- [x] v1.4.1 — OBS streaming via Window Capture (WGC). Cleared `WS_EX_TOOLWINDOW` from overlay so OBS lists it; F9 now parks off-screen instead of collapsing visibility so DWM keeps rendering for capture; new `AudioBridge` (WASAPI process-loopback) re-emits WebView2 audio from `PulseNet-Player.exe` so OBS Capture Audio (BETA) sees us as the audio source. New `#click-blocker-br` over YouTube fullscreen icon, `.station-col` made `pointer-events:none` to free the leftmost/rightmost ~30px of the video. "Streamer Options" panel replaced with "Streamer Info" instructions panel.
- [x] v1.4.2 — AudioCategory_Media via `IAudioClient2.SetClientProperties` so SteelSeries Sonar / Voicemeeter / Wavelink classify our session into the MEDIA channel (clean DSP) instead of GAME (degraded). AudioSessionRenamer permanently removed — diagnostic confirmed it wasn't the cause of Sonar's grouping behaviour and with AudioBridge present it would just create duplicate "PulseNet Player" entries in Volume Mixer. Streamer Info panel got the OBS Monitor Off step + the Sonar AUX-mute guidance.
- [x] v1.5.0 — `StreamerModeEnabled` setting (default off) gates AudioBridge so non-streamers don't get the doubled-audio regression v1.4.x shipped. UI toggle at top of Streamer Info panel; setting persists; pump polls every iteration so toggling reacts within ~200-500ms. Plus two sub-panel UX fixes — Streamer Info now closes on click-outside (was only watching the main settings panel) and the Settings Menu button now folds Streamer Info before opening (was stacking behind).
- [x] v1.6.0 — Manual "Check for updates" button + version banner in main settings panel. New `case "checkForUpdates"` web-message handler on `OverlayWindow` calls `UpdateChecker.CheckAsync`, prompts via MessageBox, and reuses `SelfUpdateService.ApplyAsync` for the in-place restart path. `window.__pulsenetVersion` now defined via `AddScriptToExecuteOnDocumentCreatedAsync` so the button shows the real version on first render (was falling back to "0.0.0"). Removed dead `frame_glow.png` references from `index.html`, `style.css`, and TODO.
- [x] v1.6.3 — Capture-client `AudioCategory.Media` (matches the v1.4.2 render-client fix), preventing the AUX leak on Sonar / Voicemeeter / Wavelink at startup. Streamer Info copy rewritten to name the specific session entries (`MSEDGEWEBVIEW2` to mute, `PULSENET-PLAYER` is the broadcast-clean re-emit), warn against dragging entries between channels (creates phantom locked duplicates), and point at the channel-level volume slider as the first place to check if audio sounds quiet. Diagnostic harness `tools/audio-probe/` added (untracked) for future audio investigations: `--pid` mode tests loopback-vs-mute behaviour, `--list` enumerates every render endpoint plus its sessions.

## Stations

- [ ] Wire real playlist IDs for all 18 stations — set `live: true` and replace placeholder `playlistId` per station as they go live
- [x] Confirm station icons are final and correctly matched to stations

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

## Code health

- [x] **Run a red-team review on the codebase** — Six parallel attack subagents (Opus) per surface: web→native bridge, auto-update path, native interop, settings/filesystem, installer/CI, process lifecycle. Full findings at `audits/2026-04-30-red-team.md`. Filtered for the OSS-desktop threat model: same-user local attacks accepted as out of scope; remote / supply-chain / network-surface findings are the actionable set. Cleared list confirmed: no telemetry, no keystroke leaks, JSON deserializer safe, virtual host path traversal defended.
- [ ] **Block-public-release tier (supply chain + remote)** — sign MSI in CI (Authenticode, gated behind a GitHub `environment:` with required reviewers) + verify signature on client before `msiexec`, closes CRITICAL-1; pin every GitHub Action (`actions/checkout`, `actions/setup-dotnet`, `softprops/action-gh-release`) by full commit SHA, closes HIGH-3; pin `wix --version 5.0.2` exactly, closes HIGH-4; replace `<Files Include="stage\**" />` in `installer/installer.wxs:42` with explicit `<File>` list + CI allowlist check, closes HIGH-5. Block public distribution until done.
- [ ] **Defense-in-depth tier** — `e.Source` validation at the top of both `WebMessageReceived` handlers (`OverlayWindow.xaml.cs:378`, `MiniBannerWindow.xaml.cs:233`), closes MEDIUM-1; host allow-list in `OpenInDefaultBrowser` (`OverlayWindow.xaml.cs:1201`), closes MEDIUM-3; add `dependabot.yml` for `nuget` + `github-actions` and a root `NuGet.config` with `packageSourceMapping` pinned to `nuget.org`, closes MEDIUM-10; add `<Deterministic>true</Deterministic>` + `actions/attest-build-provenance` so releases are independently verifiable, closes L-13.

## Pending verification

- [ ] **Hover-to-control hotkeys on API stations** — Space, M, ←/→, ↑/↓ wired via global keyboard hook + YT IFrame API; gated on `_cursorOverVideo` (mouseenter on `#video-wrap`) and `_canForwardKeys` (API player ready, not live-stream). Not testable until a non-live playlist station is wired. Live stream (`PulseNet LIVE`) has no IFrame API surface so it intentionally falls through to the click-then-keys flow.
- [ ] **Hover-to-control on live streams** — would require either swapping the raw live-stream `<iframe>` for a `YT.Player`-wrapped equivalent, or synthesising focus/click via SendInput. Punted until base hover-to-control is verified on an API station.

## Known issues / notes

- **SteelSeries Sonar shows "MSEDGEWEBVIEW2"** — Audio is produced by WebView2 child processes (`msedgewebview2.exe`). `IAudioSessionControl::SetDisplayName` works for standard Windows mixers but Sonar reads the process executable name directly. No programmatic fix. Workaround: add a custom app entry in Sonar's mixer UI.



- YouTube share/logo buttons can't be removed (cross-origin iframe + ToS), but `#click-blocker` (60px transparent div over the bottom of the video rect) absorbs the clicks so they can't be interacted with
- YouTube IFrame API does not fire a title-change event — `getVideoData().title` is polled every 2s while playing
- WebView2 user data shared across all virtual-host pages — auth persists across restarts
- If Renderer folder missing at runtime, overlay shows a navigation error page with helpful message
- `SelfUpdateService.ApplyMsiAsync` references `pulsenet_update` temp dir — matches naming
