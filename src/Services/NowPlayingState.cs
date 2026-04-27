namespace pulsenet.Services;

/// <summary>
/// Single source of truth for the current track + station label. Producers
/// (overlay's YouTube postMessage handler) write here; consumers (mini banner
/// window, Browser Source SSE stream) subscribe to <see cref="Changed"/>.
/// </summary>
public sealed class NowPlayingState
{
    private readonly object _lock = new();
    private string _title = string.Empty;
    private string _station = "PulseNet Player";
    private bool _isPlaying;

    public event Action<NowPlayingSnapshot>? Changed;

    public NowPlayingSnapshot Current
    {
        get
        {
            lock (_lock) return new NowPlayingSnapshot(_title, _station, _isPlaying);
        }
    }

    public void SetTitle(string? title)
    {
        var value = title ?? string.Empty;
        NowPlayingSnapshot snapshot;
        lock (_lock)
        {
            if (_title == value) return;
            _title = value;
            snapshot = new NowPlayingSnapshot(_title, _station, _isPlaying);
        }
        Changed?.Invoke(snapshot);
    }

    public void SetStation(string? station)
    {
        var value = string.IsNullOrWhiteSpace(station) ? "PulseNet Player" : station!;
        NowPlayingSnapshot snapshot;
        lock (_lock)
        {
            if (_station == value) return;
            _station = value;
            snapshot = new NowPlayingSnapshot(_title, _station, _isPlaying);
        }
        Changed?.Invoke(snapshot);
    }

    public void SetContentPlaying(bool playing)
    {
        NowPlayingSnapshot snapshot;
        lock (_lock)
        {
            if (_isPlaying == playing) return;
            _isPlaying = playing;
            snapshot = new NowPlayingSnapshot(_title, _station, _isPlaying);
        }
        Changed?.Invoke(snapshot);
    }
}

public readonly record struct NowPlayingSnapshot(string Title, string Station, bool IsPlaying);
