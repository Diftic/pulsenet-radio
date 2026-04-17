namespace pulsenet.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PInvoke;
using static PInvoke.AudioSessionInterop;

/// <summary>
/// Renames WebView2 audio sessions so they appear as "PulseNet Player" in Windows
/// Volume Mixer instead of "Microsoft Edge WebView2". Audio sessions are owned by
/// WebView2 helper processes whose PIDs are descendants of our main exe; we watch
/// every active render endpoint via WASAPI and apply SetDisplayName / SetIconPath
/// to each matching session. Third-party routers (SteelSeries Sonar, Wavelink,
/// Voicemeeter) typically label streams by process name instead of reading the
/// WASAPI DisplayName, so this fix is mainly visible in Volume Mixer.
/// </summary>
internal sealed class AudioSessionRenamer : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _displayName;
    private readonly string _iconPath;
    private readonly uint _ownPid;

    private IMMDeviceEnumerator? _deviceEnumerator;
    private DeviceNotificationHandler? _deviceHandler;

    private readonly object _lock = new();
    private readonly Dictionary<string, (IAudioSessionManager2 mgr, SessionNotificationHandler handler)> _managers = new();

    public AudioSessionRenamer(ILogger logger, string displayName, string iconPath)
    {
        _logger = logger;
        _displayName = displayName;
        _iconPath = iconPath;
        _ownPid = (uint)Process.GetCurrentProcess().Id;
    }

    public void Start()
    {
        try
        {
            _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            _deviceHandler = new DeviceNotificationHandler(this);
            _deviceEnumerator.RegisterEndpointNotificationCallback(_deviceHandler);
            RegisterOnAllRenderEndpoints();
            _logger.LogInformation("Audio session renamer started (pid {Pid})", _ownPid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio session renamer failed to start; sessions will show default WebView2 name");
        }
    }

    private void RegisterOnAllRenderEndpoints()
    {
        lock (_lock)
        {
            UnregisterAllManagers();

            if (_deviceEnumerator is null) return;

            if (_deviceEnumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out var collection) != 0
                || collection is null) return;

            if (collection.GetCount(out var count) != 0) return;

            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0 || device is null) continue;
                RegisterOnDevice(device);
            }
        }
    }

    private void RegisterOnDevice(IMMDevice device)
    {
        try
        {
            if (device.GetId(out var id) != 0 || string.IsNullOrEmpty(id)) return;
            if (_managers.ContainsKey(id)) return;

            var mgrGuid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref mgrGuid, CLSCTX_ALL, IntPtr.Zero, out var mgrObj) != 0 || mgrObj is null) return;

            var mgr = (IAudioSessionManager2)mgrObj;
            RenameExistingSessions(mgr);

            var handler = new SessionNotificationHandler(this);
            mgr.RegisterSessionNotification(handler);

            _managers[id] = (mgr, handler);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to register on audio endpoint");
        }
    }

    private void RenameExistingSessions(IAudioSessionManager2 mgr)
    {
        if (mgr.GetSessionEnumerator(out var enumerator) != 0 || enumerator is null) return;
        if (enumerator.GetCount(out var count) != 0) return;

        for (int i = 0; i < count; i++)
        {
            if (enumerator.GetSession(i, out var control) == 0 && control is not null)
                TryRename(control);
        }
    }

    internal void OnNewSession(IAudioSessionControl control) => TryRename(control);

    internal void OnDefaultDeviceChanged() => RegisterOnAllRenderEndpoints();
    internal void OnDeviceAddedOrRemoved() => RegisterOnAllRenderEndpoints();

    private void TryRename(IAudioSessionControl control)
    {
        try
        {
            if (control is not IAudioSessionControl2 control2) return;
            if (control2.GetProcessId(out var pid) != 0 || pid == 0) return;
            if (!IsDescendantOfOurs(pid)) return;

            var ctx = Guid.Empty;
            control2.SetDisplayName(_displayName, ref ctx);
            if (!string.IsNullOrEmpty(_iconPath))
                control2.SetIconPath(_iconPath, ref ctx);

            _logger.LogInformation("Renamed audio session for pid {Pid} → '{Name}'", pid, _displayName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to rename audio session");
        }
    }

    private bool IsDescendantOfOurs(uint pid)
    {
        uint current = pid;
        for (int depth = 0; depth < 8; depth++)
        {
            if (current == _ownPid) return true;
            if (current == 0) return false;
            if (!TryGetParentPid(current, out current)) return false;
        }
        return false;
    }

    private static bool TryGetParentPid(uint pid, out uint parentPid)
    {
        parentPid = 0;
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return false;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snapshot, ref entry)) return false;
            do
            {
                if (entry.th32ProcessID == pid)
                {
                    parentPid = entry.th32ParentProcessID;
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

    public void Dispose()
    {
        lock (_lock)
        {
            UnregisterAllManagers();

            if (_deviceEnumerator is not null && _deviceHandler is not null)
            {
                try { _deviceEnumerator.UnregisterEndpointNotificationCallback(_deviceHandler); }
                catch { }
            }
            _deviceHandler = null;
            _deviceEnumerator = null;
        }
    }

    private void UnregisterAllManagers()
    {
        foreach (var kv in _managers)
        {
            try { kv.Value.mgr.UnregisterSessionNotification(kv.Value.handler); }
            catch { }
        }
        _managers.Clear();
    }

    private sealed class SessionNotificationHandler : IAudioSessionNotification
    {
        private readonly AudioSessionRenamer _owner;
        public SessionNotificationHandler(AudioSessionRenamer owner) => _owner = owner;

        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            _owner.OnNewSession(newSession);
            return 0;
        }
    }

    private sealed class DeviceNotificationHandler : IMMNotificationClient
    {
        private readonly AudioSessionRenamer _owner;
        public DeviceNotificationHandler(AudioSessionRenamer owner) => _owner = owner;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
        {
            if (flow == EDataFlow.eRender) _owner.OnDefaultDeviceChanged();
            return 0;
        }

        public int OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState)
        {
            _owner.OnDeviceAddedOrRemoved();
            return 0;
        }
        public int OnDeviceAdded(string pwstrDeviceId) { _owner.OnDeviceAddedOrRemoved(); return 0; }
        public int OnDeviceRemoved(string pwstrDeviceId) { _owner.OnDeviceAddedOrRemoved(); return 0; }
        public int OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => 0;
    }
}
