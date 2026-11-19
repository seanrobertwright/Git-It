using System.Diagnostics;

namespace GitIt.Tray;

internal sealed class SettingsForm : Form
{
    private const string TokenUrl = "https://github.com/settings/personal-access-tokens/new";
    private const int ContentWidth = 420; // logical pixels at 96 DPI; AutoScale scales it

    private readonly RepoConfig? _existing;
    private readonly TextBox _token = new() { UseSystemPasswordChar = true, Width = ContentWidth };
    private readonly TextBox _owner = new() { Width = ContentWidth };
    private readonly TextBox _repo = new() { Width = ContentWidth };
    private readonly Button _save = new() { Text = "Test and save", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 2, 10, 2) };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.Cancel };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0) };

    public RepoConfig? Result { get; private set; }

    public SettingsForm(RepoConfig? existing, Icon icon)
    {
        _existing = existing;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "Git It! settings";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        AcceptButton = _save;
        CancelButton = _cancel;

        _owner.Text = existing?.Owner ?? "";
        _repo.Text = existing?.Repo ?? Names.DefaultRepo;
        _token.PlaceholderText = existing is null ? "github_pat_…" : "Saved. Leave blank to keep it.";

        var help = new LinkLabel
        {
            Text = "Create a fine-grained token with access to your bookmarks repo (Contents: read).",
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, 8),
        };
        help.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(TokenUrl) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                _status.Text = $"Could not open the browser: {e.Message}";
            }
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_save);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(12, 12),
        };
        layout.Controls.Add(help);
        AddField(layout, "&Token", _token);
        AddField(layout, "&Owner (leave blank to use the token's account)", _owner);
        AddField(layout, "&Bookmarks repo", _repo);
        layout.Controls.Add(_status);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        _save.Click += async (_, _) => await SaveAsync();
        ResumeLayout(false);
        PerformLayout();
    }

    private static void AddField(TableLayoutPanel layout, string label, TextBox box)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 6, 0, 2),
        });
        box.AccessibleName = label.Replace("&", "");
        box.Margin = new Padding(0, 0, 0, 4);
        layout.Controls.Add(box);
    }

    private async Task SaveAsync()
    {
        var token = _token.Text.Trim();
        if (token.Length == 0) token = _existing?.Token ?? "";
        var owner = _owner.Text.Trim();
        var repo = _repo.Text.Trim();
        if (repo.Length == 0) repo = Names.DefaultRepo;

        if (token.Length == 0 || token.Any(char.IsWhiteSpace)) { _status.Text = "Enter a GitHub token without spaces."; return; }
        if (owner.Length > 0 && !Names.IsOwner(owner)) { _status.Text = $"\"{owner}\" is not a valid GitHub owner."; return; }
        if (!Names.IsRepo(repo)) { _status.Text = $"\"{repo}\" is not a valid repository name."; return; }

        _save.Enabled = false;
        _status.Text = "Checking with GitHub…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (owner.Length == 0) owner = await GitHubClient.GetLoginAsync(token, cts.Token);
            var cfg = new RepoConfig(owner, repo, token);
            var hits = await GitHubClient.GetHitsAsync(cfg, cts.Token);
            SettingsStore.Save(cfg);
            Result = cfg;
            _status.Text = $"OK: {hits.Count} bookmarks found.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (GitHubException e)
        {
            _status.Text = e.Message;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "GitHub did not respond in time.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _status.Text = $"Could not save settings: {e.Message}";
        }
        finally
        {
            _save.Enabled = true;
        }
    }
}
