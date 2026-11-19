namespace GitIt.Tray;

internal sealed class TrayContext : ApplicationContext
{
    private readonly Icon _icon = AppIcon.Create();
    private readonly NotifyIcon _tray;
    private readonly HitsForm _hits;
    private RepoConfig? _config = SettingsStore.Load();

    public TrayContext()
    {
        _hits = new HitsForm(() => _config, _icon);
        _hits.SettingsRequested += (_, _) => OpenSettings();

        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = Startup.IsEnabled() };
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                Startup.SetEnabled(startup.Checked);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                _tray?.ShowBalloonTip(5000, "Git It!", $"Could not change startup setting: {e.Message}", ToolTipIcon.Warning);
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show my hits", null, (_, _) => _hits.ShowNearTray());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "Git It!",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _hits.Toggle();
        };

        if (_config is null)
        {
            // First run: there is nothing to show until a token is set.
            _tray.ShowBalloonTip(5000, "Git It!", "Set your GitHub token to see your hits.", ToolTipIcon.Info);
            OpenSettings();
        }
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_config, _icon);
        if (dialog.ShowDialog() != DialogResult.OK || dialog.Result is null) return;
        _config = dialog.Result;
        _hits.ShowNearTray();
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _hits.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
