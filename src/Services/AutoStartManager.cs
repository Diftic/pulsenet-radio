namespace pulsenet.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Settings;

public sealed class AutoStartManager : IHostedService
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryKeyName = Constants.ApplicationName;

    private static readonly string ExecutablePath =
        System.Windows.Forms.Application.ExecutablePath;

    private readonly SettingsManager _settings;
    private readonly ILogger<AutoStartManager> _logger;

    public AutoStartManager(SettingsManager settings, ILogger<AutoStartManager> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _settings.SettingsChanged += OnSettingsChanged;
        Apply(_settings.Current.AutoStartWithWindows);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, Models.PulsenetSettings settings)
        => Apply(settings.AutoStartWithWindows);

    private void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            if (key is null)
            {
                _logger.LogWarning("Could not open registry key for auto-start");
                return;
            }

            if (enable)
            {
                key.SetValue(RegistryKeyName, $"\"{ExecutablePath}\"");
                _logger.LogInformation("Auto-start enabled");
            }
            else
            {
                if (key.GetValue(RegistryKeyName) != null)
                    key.DeleteValue(RegistryKeyName);

                _logger.LogInformation("Auto-start disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update auto-start registry entry");
        }
    }
}
