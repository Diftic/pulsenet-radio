namespace pulsenet.Services;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.Media.Core;
using Windows.Media.Playback;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

/// <summary>
/// Native audio playback for PulseNet's Option 2 architecture: the WebView2
/// iframe plays a muted YouTube embed for video, and this service plays the
/// same content's audio independently so the audio session is attributed to
/// PulseNet-Player.exe (not msedgewebview2.exe) in Sonar / Wave Link / etc.
///
/// Two playback paths:
///   - PlayVideoIdAsync: progressive audio-only stream for the 18 VOD stations.
///     YoutubeExplode picks the highest-bitrate audio-only track (typically AAC
///     128-256 kbps) and Windows.Media.Playback streams it via Media Foundation.
///   - PlayLiveAsync: HLS manifest URL for PulseNet LIVE. Same playback core,
///     different YoutubeExplode entry point.
///
/// Phase A scope: standalone playback only, no iframe sync yet. Phases B and C
/// wire the YT.Player onStateChange / seek / track-change events from the
/// renderer to this service so the muted iframe and the audible native player
/// stay in lockstep.
/// </summary>
public sealed class NativeAudioPlayer : IDisposable
{
    private readonly ILogger<NativeAudioPlayer> _logger;
    private readonly YoutubeClient _youtube = new();
    private readonly MediaPlayer _player;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _currentVideoId;
    private bool _isCurrentlyLive;

    // Phase F retry state. YouTube audio URLs expire ~6 hours after resolution;
    // on long listening sessions the buffer drains past the expiry and the
    // MediaPlayer fires MediaFailed with a NetworkError. Re-resolving via
    // YoutubeExplode and letting AutoPlay restart the stream recovers cleanly.
    // Host-side drift correction realigns position on the next time-update
    // tick (~2s) so no manual seek is needed in the recovery path.
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(60);
    private DateTime _firstFailureInWindow = DateTime.MinValue;
    private int _retryCount;

    public NativeAudioPlayer(ILogger<NativeAudioPlayer> logger)
    {
        _logger = logger;

        _player = new MediaPlayer
        {
            AutoPlay = true,
            Volume   = 1.0,
        };
        _player.MediaFailed += OnMediaFailed;
        _player.MediaOpened += (_, _) =>
            _logger.LogInformation("NativeAudioPlayer MediaOpened ({VideoId}, dur={Dur})",
                _currentVideoId, _player.PlaybackSession.NaturalDuration);
        _player.MediaEnded += (_, _) =>
            _logger.LogInformation("NativeAudioPlayer MediaEnded ({VideoId})", _currentVideoId);
    }

    private async void OnMediaFailed(MediaPlayer _, MediaPlayerFailedEventArgs e)
    {
        _logger.LogWarning("NativeAudioPlayer MediaFailed: {Msg} (Error={Err})",
            e.ErrorMessage, e.Error);
        await HandleMediaFailureAsync(e);
    }

    private async Task HandleMediaFailureAsync(MediaPlayerFailedEventArgs e)
    {
        var videoId = _currentVideoId;
        if (string.IsNullOrEmpty(videoId))
        {
            // No active playback - failure on a torn-down source, nothing to recover.
            return;
        }

        // Non-transient errors: codec missing, malformed manifest, region-blocked
        // content. Re-resolving won't help; surfacing the failure once is enough.
        if (e.Error == MediaPlayerError.DecodingError ||
            e.Error == MediaPlayerError.SourceNotSupported)
        {
            _logger.LogWarning("NativeAudioPlayer: non-transient error, not retrying ({Err})", e.Error);
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _firstFailureInWindow > RetryWindow)
        {
            _firstFailureInWindow = now;
            _retryCount = 0;
        }
        _retryCount++;

        if (_retryCount > MaxRetries)
        {
            _logger.LogError(
                "NativeAudioPlayer: {N} failures within {Win}s for {VideoId}, giving up",
                _retryCount, RetryWindow.TotalSeconds, videoId);
            return;
        }

        _logger.LogInformation(
            "NativeAudioPlayer: recovery attempt {N}/{Max} for {VideoId} (likely URL expired)",
            _retryCount, MaxRetries, videoId);

        if (_isCurrentlyLive)
            await PlayLiveAsync(videoId).ConfigureAwait(false);
        else
            await PlayVideoIdAsync(videoId).ConfigureAwait(false);
    }

    /// <summary>
    /// Plays the highest-bitrate audio-only stream for a VOD video. Replaces
    /// any currently-playing content. Returns when the Source has been set on
    /// the underlying MediaPlayer; actual playback begins asynchronously via
    /// the MediaOpened event.
    /// </summary>
    public async Task PlayVideoIdAsync(string videoId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("NativeAudioPlayer.PlayVideoIdAsync videoId={VideoId}", videoId);

            string audioUrl;
            try
            {
                var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, ct)
                    .ConfigureAwait(false);
                var audio = manifest.GetAudioOnlyStreams()
                    .OfType<AudioOnlyStreamInfo>()
                    .OrderByDescending(s => s.Bitrate.BitsPerSecond)
                    .FirstOrDefault();
                if (audio is null)
                {
                    _logger.LogWarning("NativeAudioPlayer: no audio-only streams for {VideoId}", videoId);
                    return;
                }
                audioUrl = audio.Url;
                _logger.LogDebug("NativeAudioPlayer: picked audio stream {Codec} {Bitrate} bps",
                    audio.AudioCodec, audio.Bitrate.BitsPerSecond);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NativeAudioPlayer: YoutubeExplode failed for {VideoId}", videoId);
                return;
            }

            _currentVideoId = videoId;
            _isCurrentlyLive = false;
            _player.Pause();
            _player.Source = MediaSource.CreateFromUri(new Uri(audioUrl));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Plays the live HLS manifest for a live broadcast videoId. Used by the
    /// PulseNet LIVE station (live broadcasts don't have progressive audio,
    /// only HLS).
    /// </summary>
    public async Task PlayLiveAsync(string videoId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("NativeAudioPlayer.PlayLiveAsync videoId={VideoId}", videoId);

            string hlsUrl;
            try
            {
                hlsUrl = await _youtube.Videos.Streams.GetHttpLiveStreamUrlAsync(videoId, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NativeAudioPlayer: HLS resolution failed for {VideoId}", videoId);
                return;
            }

            _currentVideoId = videoId;
            _isCurrentlyLive = true;
            _player.Pause();
            _player.Source = MediaSource.CreateFromUri(new Uri(hlsUrl));
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Pause()
    {
        try { _player.Pause(); }
        catch (Exception ex) { _logger.LogDebug("Pause threw: {Msg}", ex.Message); }
    }

    public void Resume()
    {
        try { _player.Play(); }
        catch (Exception ex) { _logger.LogDebug("Resume threw: {Msg}", ex.Message); }
    }

    public void Stop()
    {
        try
        {
            _player.Pause();
            _player.Source = null;
            _currentVideoId = null;
            _isCurrentlyLive = false;
            _retryCount = 0;
            _firstFailureInWindow = DateTime.MinValue;
            _logger.LogInformation("NativeAudioPlayer.Stop");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Stop threw: {Msg}", ex.Message);
        }
    }

    public void Seek(TimeSpan position)
    {
        try { _player.PlaybackSession.Position = position; }
        catch (Exception ex) { _logger.LogDebug("Seek threw: {Msg}", ex.Message); }
    }

    public TimeSpan Position => _player.PlaybackSession.Position;
    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0.0, 1.0);
    }

    public void Dispose()
    {
        try { _player.Pause(); } catch { }
        try { _player.Dispose(); } catch { }
        _lock.Dispose();
    }
}
