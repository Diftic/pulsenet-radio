# Session Journal

A living journal that persists across compactions. Captures decisions, progress, and context.

## Current State
- **Focus:** v1.6.0 shipped — manual "Check for updates" button + live version banner in main settings panel. Reuses existing `UpdateChecker` + `SelfUpdateService` so the manual path matches the auto-check path. `window.__pulsenetVersion` injected on document creation so the label is correct on first render.
- **Blocked:** Real playlist IDs for 18 stations not yet provided.

## Log

### 2026-04-28 — Completed: v1.6.0 — manual update check + version banner
- **Gap.** Auto-update-check on startup existed since v1.4.x, but no in-app way to retrigger between launches and no in-app surface for the current version number. PyCharm crashed mid-session; recovered the work-in-progress diff (5 files modified) and finished it.
- **Fix.** New `version-btn` row at the top of main settings panel. JS posts `{type:'checkForUpdates'}`; new `case "checkForUpdates"` in `OverlayWindow.OnWebMessageReceived` fires `HandleCheckForUpdatesAsync` which calls `UpdateChecker.CheckAsync` off-thread, marshals back via `Dispatcher.InvokeAsync` for a Yes/No `MessageBox`, and on Yes reuses `SelfUpdateService.ApplyAsync` (same path the auto-check uses). C# calls `window.__pulsenetUpdateCheckDone()` after the modal closes to re-enable the button.
- **Version-injection timing fix.** First implementation injected `window.__pulsenetVersion` only via `BuildSyncScript` on `ShowOverlay`, so `player.js`'s initial `versionLabel()` call hit `undefined` and rendered "v0.0.0 — Check for updates" until the first overlay open. Moved injection to `WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync` during `InitializeWebViewAsync` — runs before any page script. `BuildSyncScript` still re-injects on every overlay show so a future in-place self-update would surface the new version without restart.
- **Cleanup.** Removed dead `frame_glow.png` references from `index.html`, `style.css` (rule + `glow-pulse` keyframes), and TODO. Asset was aspirational, never produced.
- **Files touched.** `src/UI/OverlayWindow.xaml.cs` (case + handler + injection), `src/Renderer/player.js` (button wiring + version refresh hook), `src/Renderer/index.html` (button row, removed glow img), `src/Renderer/style.css` (removed glow rule), `src/pulsenet.csproj` (1.5.0 → 1.6.0), `TODO.md`, `DEVLOG.md`.
- **Verified.** Clean build (0 warnings, 0 errors). Player launches, WebView2 ready, on-startup auto-check logged "current: 1.6.0, latest: 1.5.0" as expected (latest is still 1.5.0 until the v1.6.0 release publishes via the workflow).
- Tagged `v1.6.0`, pushed. Build & Release workflow producing the release.

### 2026-04-27 21:10 — Completed: v1.5.0 — Streamer Mode toggle + sub-panel UX fixes
- **The bug we shipped in v1.4.2.** v1.4.2 declared the OBS streaming feature complete after testing on a Sonar-equipped streaming setup. What we missed: AudioBridge ran unconditionally on every launch, so the ~90% of users who are *just listening* to music — and don't have an app router — heard doubled audio from both paths hitting their default device with no available fix on their end. Tester surfaced this as soon as we had the discussion about the broader user base. Genuine regression vs pre-AudioBridge.
- **The fix.** New `StreamerModeEnabled` field on `PulsenetSettings`, default `false`. `AudioBridge.RunPump` checks `_settings.Current.StreamerModeEnabled` in its outer scheduling loop *and* in `RunOnce`'s inner sample-pump loop. When false: pump sleeps, no audio sessions created, non-streamers hear single-path audio. When toggled true: pump wakes within ~500ms, WebView2 PID lookup + WASAPI activate proceed as before. When toggled false mid-playback: inner loop notices within ~200ms (next capture-event wait timeout), `RunOnce` returns cleanly, WASAPI clients dispose, back to single-path. No `ManualResetEvent` plumbing needed — the pump's natural sleep cadence covers reaction time.
- **UI.** Checkbox row at the top of the Streamer Info sub-panel (deliberately not in the main settings panel — listeners shouldn't see it; streamers will see it as the natural first step before the OBS setup walkthrough). New web-message case `streamerMode` in `OverlayWindow.OnWebMessageReceived` persists the setting; `BuildSyncScript` reads current value and ticks the checkbox on every overlay show.
- **Sub-panel UX fixes (v1.4.2 missed these).** Two related issues with Streamer Info: (a) clicking outside the panel didn't close it because the document-level click-outside handler in `player.js` was watching only `#settings-panel`; (b) clicking the Settings Menu button while Streamer Info was open opened the main panel *behind* Streamer Info because the button handler folded only `#miniplayer-settings-panel` before toggling. Fix: extended the click-outside handler to also close `#streamer-settings-panel`, and extended the settings-button handler to also fold it before toggling. The miniplayer panel had already been getting both treatments correctly; we just hadn't replicated them when Streamer Info was added.
- Locally verified: streamer mode off → no second `PULSENET-PL` entry in Sonar, no doubling. Streamer mode on → bridge spins up, OBS captures, AUX-mute workflow works. Toggle off mid-playback → bridge tears down within ~200ms. Click-outside and settings-button transitions both work cleanly.
- Tagged `v1.5.0`, pushed. Build & Release workflow producing the release.

### 2026-04-27 20:20 — Completed: v1.4.2 — Sonar workflow + AudioSessionRenamer rip
- v1.4.1's AudioBridge worked for the broadcast but Sonar's per-session UI surfaced two new problems on the streamer's machine:
  1. Sonar parked our session in GAME channel (which applies game-oriented DSP that mangles music — "FAR lesser audio quality") and locked the controls because the classification couldn't be retroactively applied — `PulseNet Player didn't allow Sonar to change the audio settings`.
  2. WebView2's direct path was on a separate Sonar channel, so the streamer heard both paths simultaneously → echo on headphones.
- **Fix 1 — declare AudioCategory_Media before Initialize.** Added `IAudioClient2`, `AudioStreamCategory`, `AudioStreamOptions`, `AudioClientProperties` to `src/PInvoke/AudioBridgeInterop.cs`; in `AudioBridge.RunOnce` we QI the activated IAudioClient to IAudioClient2 and `SetClientProperties` with `eCategory = Media` *before* the `Initialize` call. Sonar now routes us to MEDIA channel with clean DSP. Tester confirmed: lock disappeared, audio quality matched WebView2 direct.
- **Fix 2 — workflow, not architecture.** Streamer mutes Sonar's AUX channel (where WebView2's direct path lives) — only MEDIA reaches their headphones, no echo, no quality degradation. Sonar's per-channel mute is local; it doesn't reach OBS's WASAPI process loopback, so the broadcast continues unaffected. Verified: muting any Sonar channel does not affect OBS Audio Mixer levels for our Window Capture source. Streamer Info panel got both this conditional step and the earlier OBS Monitor Off step.
- **AudioSessionRenamer killed.** Diagnostic (renamer disabled in App.xaml.cs) confirmed Sonar groups WebView2 helpers via process tree, not via display name or icon. Renamer was innocent of the binding; with AudioBridge present it's actively harmful in Volume Mixer (two same-named "PulseNet Player" entries side by side). `src/Services/AudioSessionRenamer.cs` deleted, `src/PInvoke/AudioSessionInterop.cs` trimmed from ~190 → ~95 lines (kept only MMDevice + toolhelp32 bits AudioBridge still uses), `_audioRenamer` field + Dispose call removed from `App.xaml.cs`.
- **Buffer durations dropped to 0** on capture+render Initialize calls (auto = engine period, ~10 ms each side). Tester confirmed this didn't perceptibly change the echo, which validates that the dominant cause was duplicate playback paths through Sonar, not latency. Kept the change anyway — slightly tighter sync is free.
- Tagged `v1.4.2`, pushed. Build & Release workflow producing the release.

### 2026-04-27 17:50 — Completed: AudioBridge (WASAPI process-loopback re-emit)
- Problem: OBS Window Capture's "Capture Audio (BETA)" binds to the captured window's process. PulseNet's window is `PulseNet-Player.exe` but its audio is generated entirely by `msedgewebview2.exe` helper PIDs (descendants), so OBS captured silence. Application Audio Capture didn't help either — `msedgewebview2.exe` doesn't appear in its picker on this machine.
- Solution: new `Services/AudioBridge.cs` (`IHostedService`) plus `PInvoke/AudioBridgeInterop.cs`. Pipeline:
  1. Polls for the WebView2 root browser child of our PID (image == `msedgewebview2.exe`, parent == self)
  2. Activates WASAPI process-loopback on that PID with `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE` via `ActivateAudioInterfaceAsync`(`VAD\Process_Loopback`, IAudioClient, PROPVARIANT(VT_BLOB→AUDIOCLIENT_ACTIVATION_PARAMS))
  3. Opens a render `IAudioClient` on the default render endpoint, takes its mix format and uses the *same* format for capture init so no resampling is needed in the pump path
  4. Pump thread (Pro Audio MMCSS priority): block on capture event, drain `IAudioCaptureClient.GetBuffer` packets, copy into `IAudioRenderClient.GetBuffer` slots; capture release happens unconditionally to keep latency low under transient render stalls
- Verified working: tester's machine in 8-channel 96 kHz 32-bit shared-mode rendering. AudioBridge logged "running" within seconds of launch; OBS Window Capture's Capture Audio (BETA) immediately registered audio levels from `PulseNet-Player.exe`.
- Trade-off accepted by user: streamer hears doubled audio locally (WebView2 direct + our re-emit). Streamer manages this via OBS per-source audio monitoring controls; viewers receive the clean re-emit only.
- Streamer Info panel left as-is — Capture Audio (BETA) instruction remains correct now that we actually emit on `PulseNet-Player.exe`.

### 2026-04-27 17:30 — Decision: pivot to Window Capture (solution 1.5) for OBS
- After the Browser Source approach hit progressively harder sync issues (live mode pause not propagating without YT.Player attach, position sync needing a polling channel, audio doubling between re-emit and direct path), realised the simpler answer: just remove `WS_EX_TOOLWINDOW` from the overlay so OBS lists it, and change F9 to *park off-screen* instead of `Visibility.Collapsed` so DWM keeps rendering it for WGC capture.
- Implementation in `src/UI/OverlayWindow.xaml.cs`: clear `WS_EX_TOOLWINDOW` in `OnSourceInitialized` (was previously *adding* it); `HideOverlay` now persists current Left/Top then sets `Left = -(FrameDisplayWidth + 100)` instead of `Visibility = Collapsed`. The hidden owner WPF auto-creates from `ShowInTaskbar="False"` keeps it out of Alt+Tab and the taskbar regardless.
- Streamer Info panel reworked from the old Browser Source URL config into setup steps: Window Capture, capture method = Windows 10 (1903+), Capture Audio (BETA) on, Capture Cursor off, Client Area on, F9 hint.
- Click-blocker fixes that landed in this pass:
  - New `#click-blocker-br` (80×50 at right=0 bottom=83px) over the YouTube fullscreen icon in the bottom-right
  - `#click-blocker` height 60→62 to fully clear hover affordances
  - `.station-col` got `pointer-events: none` — column boxes were swallowing clicks in the leftmost/rightmost ~37/39px of the video rect because they overlap the video horizontally; buttons inside still set `pointer-events: auto` so they remain clickable
- Browser Source server (`BrowserSourceServer`, `NowPlayingState`, `Renderer/obs/`) remains in the tree but is dead code — flagged for removal in a follow-up cleanup commit once Window Capture is proven over a few real streaming sessions.

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
