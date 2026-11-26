using System.Windows;
using System.Windows.Forms;
using BreadBoard.App.Services;
using BreadBoard.Core.Config;

namespace BreadBoard.App.Shell;

/// <summary>
/// Bread Board's notification-area icon. Click: the popup above it; double-click: the main window;
/// right-click: Open / Quit. It lives for the whole run, so closing the window leaves Bread Board working.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly TrayFlyout _flyout;
    private readonly ConfigService _config;

    public TrayIcon(ConfigService config, SoundboardController sb, Action open, Action quit)
    {
        _config = config;
        _flyout = new TrayFlyout(sb, open, quit);
        var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/BreadBoard.ico"))!.Stream;
        _icon = new NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream, SystemInformation.SmallIconSize),
            Text = "Bread Board",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Items.Add("Open Bread Board", null, (_, _) => open());
        _icon.ContextMenuStrip.Items.Add("Quit", null, (_, _) => quit());
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _flyout.Toggle(); };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { _flyout.Hide(); open(); } };
    }

    /// <summary>Tells the user once that closing the window kept Bread Board running here.</summary>
    public void ShowStillRunningHint()
    {
        if (_config.Current.Ui.TrayHintShown) return;
        _config.Update(c => c.Ui.TrayHintShown = true);
        _icon.ShowBalloonTip(5000, "Bread Board is still running",
            "Sounds, shortcuts and the Bread Board mic keep working. Click this icon for controls; right-click to quit.", ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _flyout.Close();
    }
}
