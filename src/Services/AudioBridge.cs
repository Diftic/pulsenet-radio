namespace pulsenet.Services;

using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PInvoke;
using Settings;
using static PInvoke.AudioBridgeInterop;
using static PInvoke.AudioSessionInterop;

/// <summary>
/// Captures audio from our WebView2 child processes via WASAPI process-loopback
/// (Windows 10 v2004+) and re-emits it from <c>PulseNet-Player.exe</c>'s own audio
/// session, so OBS Window Capture's "Capture Audio (BETA)" picks it up.
///
/// Without this, the audio sessions live in <c>msedgewebview2.exe</c> helper PIDs
/// that are invisible to OBS's window-bound process-audio capture, and the
/// streamer's broadcast contains no music. The streamer's local listening
/// experience is unaffected — they still hear WebView2's direct path; the
/// re-emit is a *parallel* audio track. They control its presence in their
/// own headphones via OBS's per-source audio monitoring controls.
///
/// Lifecycle: starts at app startup, polls until WebView2 has spawned, then
/// runs the capture-and-render pump until cancellation. On any error the pump
/// returns and the outer loop retries with a fresh PID lookup.
/// </summary>
internal sealed class AudioBridge : IHostedService, IDisposable
{
    private readonly ILogger<AudioBridge> _logger;
    private readonly SettingsManager _settings;
    private readonly uint _ownPid;
    private CancellationTokenSource? _cts;
    private Thread? _pumpThread;

    public AudioBridge(SettingsManager settings, ILogger<AudioBridge> logger)
    {
        _settings = settings;
        _logger = logger;
        _ownPid = (uint)Environment.ProcessId;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _pumpThread = new Thread(RunPump) { IsBackground = true, Name = "AudioBridge" };
        _pumpThread.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _pumpThread?.Join(2000);
        _pumpThread = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _cts?.Dispose();

    // -------------------------------------------------------------------------
    // Pump
    // -------------------------------------------------------------------------

    private void RunPump()
    {
        var token = _cts!.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Streamer Mode gate. Default off so non-streaming users don't
                // hear doubled audio (WebView2 direct + AudioBridge re-emit).
                // Streamers enable it from the Streamer Info panel; disabling
                // breaks RunOnce out via the same setting check in its loop.
                if (!_settings.Current.StreamerModeEnabled)
                {
                    Thread.Sleep(500);
                    continue;
                }

                if (!TryFindWebView2RootPid(out var webView2Pid))
                {
                    Thread.Sleep(500);
                    continue;
                }

                _logger.LogInformation("AudioBridge: targeting WebView2 root pid {Pid}", webView2Pid);
                if (!RunOnce(webView2Pid, token) && !token.IsCancellationRequested)
                {
                    Thread.Sleep(1000);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioBridge pump crashed");
        }
    }

    /// <summary>
    /// One full run: open capture and render, pump until error or cancellation.
    /// Returns true on graceful exit, false on error so the outer loop retries.
    /// </summary>
    private bool RunOnce(uint webView2Pid, CancellationToken token)
    {
        IntPtr renderEvent = IntPtr.Zero;
        IntPtr captureEvent = IntPtr.Zero;
        IntPtr mmcssHandle = IntPtr.Zero;
        IntPtr mixFormat = IntPtr.Zero;
        IAudioClient? captureClient = null;
        IAudioClient? renderClient = null;
        IAudioCaptureClient? cap = null;
        IAudioRenderClient? rend = null;

        try
        {
            // 1. Default render endpoint + its mix format. We use the same format
            //    on both sides so no resampling is needed in the pump path.
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var renderDevice) != 0
                || renderDevice is null)
            {
                _logger.LogWarning("No default render endpoint");
                return false;
            }

            var iidAudioClient = IID_IAudioClient;
            if (renderDevice.Activate(ref iidAudioClient, CLSCTX_ALL, IntPtr.Zero, out var renderObj) != 0
                || renderObj is null)
            {
                _logger.LogWarning("Failed to activate IAudioClient on render endpoint");
                return false;
            }
            renderClient = (IAudioClient)renderObj;

            if (renderClient.GetMixFormat(out mixFormat) != 0 || mixFormat == IntPtr.Zero)
            {
                _logger.LogWarning("Failed to get render mix format");
                return false;
            }

            var wfx = Marshal.PtrToStructure<WAVEFORMATEX>(mixFormat);
            int frameSize = wfx.nBlockAlign;
            _logger.LogInformation(
                "AudioBridge mix format: tag=0x{Tag:X4} channels={Ch} rate={Rate} bits={Bits} blockAlign={BA}",
                wfx.wFormatTag, wfx.nChannels, wfx.nSamplesPerSec, wfx.wBitsPerSample, wfx.nBlockAlign);

            // 2. Declare audio category BEFORE Initialize so Sonar/Wavelink/etc.
            //    classify our session as media playback (MEDIA channel) rather
            //    than guessing GAME and then locking the controls because the
            //    classification couldn't be applied retroactively. Same QI
            //    object as IAudioClient — IAudioClient2 inherits it.
            if (renderObj is IAudioClient2 renderClient2)
            {
                var props = new AudioClientProperties
                {
                    cbSize     = (uint)Marshal.SizeOf<AudioClientProperties>(),
                    bIsOffload = false,
                    eCategory  = AudioStreamCategory.Media,
                    Options    = AudioStreamOptions.None,
                };
                var hrProps = renderClient2.SetClientProperties(ref props);
                if (hrProps != 0)
                    _logger.LogDebug("SetClientProperties returned 0x{Hr:X8}", hrProps);
            }

            // 3. Initialize render client (event-driven, shared mode). Passing
            //    bufferDuration = 0 in shared+event mode makes Windows pick the
            //    audio engine's minimum period (typically ~10ms vs 20ms+ if we
            //    requested explicitly). Half the latency = closer to in-phase
            //    with WebView2's direct path = inaudible doubling instead of
            //    audible echo on the streamer's headphones.
            renderEvent = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
            var hr = renderClient.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                0,
                0,
                mixFormat,
                IntPtr.Zero);
            if (hr != 0) { _logger.LogWarning("Render init failed: 0x{Hr:X8}", hr); return false; }
            renderClient.SetEventHandle(renderEvent);

            var iidRender = IID_IAudioRenderClient;
            renderClient.GetService(ref iidRender, out var renderSvc);
            rend = (IAudioRenderClient)renderSvc;

            if (renderClient.GetBufferSize(out var renderBufferFrames) != 0)
            {
                _logger.LogWarning("Render GetBufferSize failed");
                return false;
            }

            // 3. Activate process-loopback capture targeting WebView2 + descendants.
            captureClient = ActivateProcessLoopback(webView2Pid);
            if (captureClient is null) return false;

            captureEvent = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
            hr = captureClient.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                0,
                0,
                mixFormat,
                IntPtr.Zero);
            if (hr != 0) { _logger.LogWarning("Capture init failed: 0x{Hr:X8}", hr); return false; }
            captureClient.SetEventHandle(captureEvent);

            var iidCapture = IID_IAudioCaptureClient;
            captureClient.GetService(ref iidCapture, out var captureSvc);
            cap = (IAudioCaptureClient)captureSvc;

            // 4. Bump thread priority for low-latency audio.
            uint mmcssIndex = 0;
            mmcssHandle = AvSetMmThreadCharacteristicsW("Pro Audio", ref mmcssIndex);

            // 5. Pre-fill render with one buffer of silence so it has something
            //    to play immediately when started.
            if (rend.GetBuffer(renderBufferFrames, out var prefillPtr) == 0 && prefillPtr != IntPtr.Zero)
            {
                rend.ReleaseBuffer(renderBufferFrames, AUDCLNT_BUFFERFLAGS_SILENT);
            }

            // 6. Start both clients.
            captureClient.Start();
            renderClient.Start();
            _logger.LogInformation("AudioBridge running");

            // 7. Pump loop: block on capture event, drain capture, copy into render.
            //    Also re-checks StreamerModeEnabled each iteration so toggling
            //    the setting off mid-playback tears the bridge down within ~200ms.
            while (!token.IsCancellationRequested && _settings.Current.StreamerModeEnabled)
            {
                var waitResult = WaitForSingleObject(captureEvent, 200);
                if (waitResult == WAIT_TIMEOUT) continue;
                if (waitResult != WAIT_OBJECT_0)
                {
                    _logger.LogDebug("Capture event wait failed: 0x{R:X}", waitResult);
                    return false;
                }

                while (cap.GetNextPacketSize(out var packetFrames) == 0 && packetFrames > 0)
                {
                    if (cap.GetBuffer(out var capPtr, out var numFrames, out var capFlags, out _, out _) != 0) break;
                    if (numFrames == 0) { cap.ReleaseBuffer(0); continue; }

                    if (renderClient.GetCurrentPadding(out var padding) != 0)
                    {
                        cap.ReleaseBuffer(numFrames);
                        break;
                    }
                    var renderFree = renderBufferFrames - padding;
                    var framesToWrite = Math.Min(numFrames, renderFree);

                    if (framesToWrite > 0
                        && rend.GetBuffer(framesToWrite, out var renderPtr) == 0
                        && renderPtr != IntPtr.Zero)
                    {
                        bool silent = (capFlags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                        if (silent || capPtr == IntPtr.Zero)
                        {
                            rend.ReleaseBuffer(framesToWrite, AUDCLNT_BUFFERFLAGS_SILENT);
                        }
                        else
                        {
                            unsafe
                            {
                                Buffer.MemoryCopy(
                                    (void*)capPtr,
                                    (void*)renderPtr,
                                    (long)framesToWrite * frameSize,
                                    (long)framesToWrite * frameSize);
                            }
                            rend.ReleaseBuffer(framesToWrite, 0);
                        }
                    }

                    // Always release the capture buffer with the full count we read,
                    // even if we couldn't fit it all into render — dropping rather
                    // than blocking keeps latency low under transient render stalls.
                    cap.ReleaseBuffer(numFrames);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioBridge run error");
            return false;
        }
        finally
        {
            try { captureClient?.Stop(); } catch { /* ignore */ }
            try { renderClient?.Stop(); } catch { /* ignore */ }
            if (cap is not null)            Marshal.ReleaseComObject(cap);
            if (rend is not null)           Marshal.ReleaseComObject(rend);
            if (captureClient is not null)  Marshal.ReleaseComObject(captureClient);
            if (renderClient is not null)   Marshal.ReleaseComObject(renderClient);
            if (mixFormat != IntPtr.Zero)   CoTaskMemFree(mixFormat);
            if (renderEvent != IntPtr.Zero) CloseHandle(renderEvent);
            if (captureEvent != IntPtr.Zero) CloseHandle(captureEvent);
            if (mmcssHandle != IntPtr.Zero) AvRevertMmThreadCharacteristics(mmcssHandle);
        }
    }

    private IAudioClient? ActivateProcessLoopback(uint targetPid)
    {
        var actParams = new AUDIOCLIENT_ACTIVATION_PARAMS
        {
            ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.ProcessLoopback,
            ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
            {
                TargetProcessId = targetPid,
                ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.IncludeTargetProcessTree,
            },
        };

        var blobSize = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
        IntPtr blobPtr = Marshal.AllocHGlobal(blobSize);
        try
        {
            Marshal.StructureToPtr(actParams, blobPtr, false);
            var prop = new PROPVARIANT
            {
                vt = VT_BLOB,
                blob = new BLOB
                {
                    cbSize = (uint)blobSize,
                    pBlobData = blobPtr,
                },
            };

            var iidAudioClient = IID_IAudioClient;
            var handler = new ActivationCompletionHandler();
            ActivateAudioInterfaceAsync(
                VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                ref iidAudioClient,
                ref prop,
                handler,
                out var op);

            if (!handler.Done.WaitOne(5000))
            {
                _logger.LogWarning("ActivateAudioInterfaceAsync timeout");
                return null;
            }

            int hr = op.GetActivateResult(out var actHr, out var unk);
            if (hr != 0 || actHr != 0)
            {
                _logger.LogWarning("Process-loopback activate failed: hr=0x{Hr:X8} actHr=0x{ActHr:X8}", hr, actHr);
                return null;
            }
            return (IAudioClient)unk;
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    private bool TryFindWebView2RootPid(out uint pid)
    {
        pid = 0;
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return false;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snapshot, ref entry)) return false;
            do
            {
                // Direct child of our process whose image is msedgewebview2.exe →
                // the WebView2 browser process. INCLUDE_TARGET_PROCESS_TREE then
                // sweeps in all of its helper renderer/audio processes.
                if (entry.th32ParentProcessID == _ownPid &&
                    string.Equals(entry.szExeFile, "msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
                {
                    pid = entry.th32ProcessID;
                    return true;
                }
            } while (Process32NextW(snapshot, ref entry));
            return false;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        public ManualResetEvent Done { get; } = new(false);

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            Done.Set();
            return 0;
        }
    }
}
