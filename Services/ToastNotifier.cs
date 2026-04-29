using System.Drawing;
using System.Windows.Forms;

namespace SI360.GateRunner.Services;

public sealed class ToastNotifier : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public ToastNotifier()
    {
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "SI360 Gate Runner",
            Visible = false
        };
    }

    public void Show(string title, string message, bool error)
    {
        if (_disposed) return;
        _icon.Visible = true;
        _icon.BalloonTipIcon = error ? ToolTipIcon.Error : ToolTipIcon.Info;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(5000);

        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
