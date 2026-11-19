using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitIt.Tray;

internal sealed class HitsForm : Form
{
    private const int RepoCol = 0, DescCol = 1, AddedCol = 2, CopyCol = 3;

    private readonly Func<RepoConfig?> _getConfig;
    private readonly TextBox _search = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "Filter repos…",
        AccessibleName = "Filter repos",
    };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right };
    private readonly Button _settings = new() { Text = "Settings…", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };

    private IReadOnlyList<Hit> _all = [];
    private List<Hit> _view = [];
    private int _requestId;
    private CancellationTokenSource? _cts;
    private DateTime _lastHiddenUtc = DateTime.MinValue;

    public event EventHandler? SettingsRequested;

    public HitsForm(Func<RepoConfig?> getConfig, Icon icon)
    {
        _getConfig = getConfig;
        SuspendLayout();
        // Sizes below are logical pixels at 96 DPI; Font autoscaling grows them on scaled displays.
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "Git It! — my hits";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(640, 480);
        MinimumSize = new Size(420, 260);

        BuildGrid();

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, AutoSize = true };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(_search, 0, 0);
        top.Controls.Add(_refresh, 1, 0);
        top.Controls.Add(_settings, 2, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(_status, 0, 2);
        Controls.Add(layout);

        _search.TextChanged += (_, _) => ApplyFilter();
        _search.KeyDown += OnSearchKeyDown;
        _refresh.Click += async (_, _) => await RefreshAsync();
        _settings.Click += (_, _) =>
        {
            Hide();
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        };
        ResumeLayout(false);
        PerformLayout();
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.VirtualMode = true;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AccessibleName = "My hits";
        _grid.StandardTab = true;

        _grid.Columns.Add(new DataGridViewLinkColumn
        {
            HeaderText = "Repository",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 34,
            TrackVisitedState = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = "Open in browser",
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Description",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 42,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Added",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 12,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        _grid.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "",
            Text = "Copy link",
            UseColumnTextForButtonValue = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 12,
            ToolTipText = "Copy the repo link",
        });

        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.CellContentClick += OnCellContentClick;
        _grid.KeyDown += OnGridKeyDown;
    }

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
        var hit = _view[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            RepoCol => hit.FullName,
            DescCol => hit.Description,
            AddedCol => Dates.ToLocalDate(hit.AddedAt),
            _ => null,
        };
    }

    private void OnCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
        if (e.ColumnIndex == RepoCol) Open(_view[e.RowIndex]);
        else if (e.ColumnIndex == CopyCol) Copy(_view[e.RowIndex]);
    }

    private Hit? SelectedHit =>
        _grid.CurrentRow is { Index: var i } && i >= 0 && i < _view.Count ? _view[i] : null;

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (SelectedHit is not { } hit) return;
        if (e.KeyCode == Keys.Enter)
        {
            Open(hit);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.C)
        {
            Copy(hit);
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && _view.Count > 0)
        {
            _grid.Focus();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && _view.Count > 0)
        {
            Open(_view[0]);
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void ApplyFilter()
    {
        var q = _search.Text.Trim();
        _view = q.Length == 0
            ? [.. _all]
            : [.. _all.Where(h =>
                h.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                h.Description.Contains(q, StringComparison.OrdinalIgnoreCase))];
        _grid.RowCount = 0;
        _grid.RowCount = _view.Count;
        _grid.Invalidate();
    }

    private void Open(Hit hit)
    {
        try
        {
            Process.Start(new ProcessStartInfo(hit.Url) { UseShellExecute = true });
            Hide();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _status.Text = $"Could not open the browser: {e.Message}";
        }
    }

    private void Copy(Hit hit)
    {
        try
        {
            Clipboard.SetText(hit.Url);
            _status.Text = $"Copied {hit.Url}";
        }
        catch (ExternalException)
        {
            _status.Text = "The clipboard is busy. Try again.";
        }
    }

    public async Task RefreshAsync()
    {
        var cfg = _getConfig();
        if (cfg is null)
        {
            _status.Text = "Not set up yet. Click Settings…";
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var id = ++_requestId;
        _status.Text = $"Loading {cfg.Owner}/{cfg.Repo}…";
        _refresh.Enabled = false;
        try
        {
            var hits = await GitHubClient.GetHitsAsync(cfg, ct);
            if (id != _requestId) return; // a newer refresh superseded this one
            _all = hits;
            ApplyFilter();
            _status.Text = $"{hits.Count} repo{(hits.Count == 1 ? "" : "s")} · updated {Dates.LocalTimeNow()}";
        }
        catch (OperationCanceledException)
        {
            // superseded or closing
        }
        catch (GitHubException e)
        {
            if (id == _requestId) _status.Text = e.Message;
        }
        finally
        {
            if (id == _requestId) _refresh.Enabled = true;
        }
    }

    public void Toggle()
    {
        // A tray click first deactivates (and hides) the window; don't reopen it immediately.
        if (Visible) Hide();
        else if ((DateTime.UtcNow - _lastHiddenUtc).TotalMilliseconds > 300) ShowNearTray();
    }

    public void ShowNearTray()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 8, area.Bottom - Height - 8);
        Show();
        Activate();
        _search.Focus();
        _search.SelectAll();
        _ = RefreshAsync();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _lastHiddenUtc = DateTime.UtcNow;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
