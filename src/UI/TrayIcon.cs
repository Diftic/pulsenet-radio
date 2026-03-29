namespace pulsenet.UI;

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

/// <summary>
///     System tray icon with state-based icons and a context menu.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _iconDefault;
    private readonly Icon _iconActive;

    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _iconDefault = LoadEmbeddedIcon();
        _iconActive  = TintIcon(_iconDefault, Color.FromArgb(80, 200, 120)); // green tint when player open

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconDefault,
            Text = Constants.ApplicationName,
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _notifyIcon.ContextMenuStrip = menu;
    }

    public void SetActive(bool active)
        => _notifyIcon.Icon = active ? _iconActive : _iconDefault;

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        => _notifyIcon.ShowBalloonTip(3000, title, message, icon);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _iconDefault.Dispose();
        _iconActive.Dispose();
    }

    // -------------------------------------------------------------------------
    // Icon loading
    // -------------------------------------------------------------------------

    private static Icon LoadEmbeddedIcon()
    {
        var asm    = Assembly.GetExecutingAssembly();
        var stream = asm.GetManifestResourceStream("pulsenet.Assets.icon.ico");
        if (stream is null)
            return CreateFallbackIcon(Color.FromArgb(34, 211, 238)); // cyan fallback
        return new Icon(stream, 16, 16);
    }

    // Creates a version of the icon with a coloured overlay — used for the active state.
    private static Icon TintIcon(Icon source, Color tint)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawIcon(source, 0, 0);
            using var brush = new SolidBrush(Color.FromArgb(140, tint));
            g.FillRectangle(brush, 0, 0, 16, 16);
        }

        var hIcon = bmp.GetHicon();
        var icon  = Icon.FromHandle(hIcon);
        var clone = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        return clone;
    }

    private static Icon CreateFallbackIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 14, 14);
        }

        var hIcon = bmp.GetHicon();
        var icon  = Icon.FromHandle(hIcon);
        var clone = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        return clone;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);
}
