using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class GitHubToolView : UserControl, IStatefulTool
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _autoRefresh = new() { Interval = TimeSpan.FromMinutes(2) };
    private CancellationTokenSource? _cts;

    private List<NotificationItem> _allNotifications = [];
    private List<AssignedItem> _allPrs = [];
    private List<AssignedItem> _allIssues = [];
    private List<CommentItem> _allComments = [];

    private string _selectedOrg = "";
    private string _selectedRepo = "";
    private bool _suppressFilterEvent;

    public GitHubToolView(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _autoRefresh.Tick += async (_, _) => await RefreshDataAsync();

        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _autoRefresh.Stop();
            _cts?.Cancel();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_settings.GitHubToken))
        {
            TokenBox.Password = _settings.GitHubToken;
            await ConnectAsync();
        }
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        var token = TokenBox.Password.Trim();
        if (string.IsNullOrEmpty(token))
        {
            StatusText.Text = "Enter a personal access token.";
            return;
        }

        StatusText.Text = "Connecting...";
        BtnConnect.IsEnabled = false;

        try
        {
            GitHubService.Configure(token);
            var username = await GitHubService.ValidateAsync();

            _settings.GitHubToken = token;
            _settings.Save();

            StatusText.Text = $"Connected as {username}";
            BtnConnect.Visibility = Visibility.Collapsed;
            BtnRefresh.Visibility = Visibility.Visible;
            BtnDisconnect.Visibility = Visibility.Visible;
            FilterBar.Visibility = Visibility.Visible;
            TokenBox.IsEnabled = false;

            _autoRefresh.Start();
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            GitHubService.Disconnect();
            StatusText.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            BtnConnect.IsEnabled = true;
        }
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        _autoRefresh.Stop();
        _cts?.Cancel();
        GitHubService.Disconnect();

        _settings.GitHubToken = "";
        _settings.Save();

        StatusText.Text = "Disconnected.";
        BtnConnect.Visibility = Visibility.Visible;
        BtnRefresh.Visibility = Visibility.Collapsed;
        BtnDisconnect.Visibility = Visibility.Collapsed;
        FilterBar.Visibility = Visibility.Collapsed;
        TokenBox.IsEnabled = true;
        TokenBox.Password = "";

        _allNotifications.Clear();
        _allPrs.Clear();
        _allIssues.Clear();
        _allComments.Clear();
        ClearLists();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshDataAsync();

    private async void OnMarkAllReadClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await GitHubService.MarkAllNotificationsReadAsync();
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private async Task RefreshDataAsync()
    {
        if (!GitHubService.IsConfigured) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        LoadingText.Visibility = Visibility.Visible;

        try
        {
            var notifTask = GitHubService.GetNotificationsAsync(ct);
            var prTask = GitHubService.GetAssignedPRsAsync(ct);
            var issueTask = GitHubService.GetAssignedIssuesAsync(ct);
            var commentTask = GitHubService.GetMentionedCommentsAsync(ct);

            await Task.WhenAll(notifTask, prTask, issueTask, commentTask);

            if (ct.IsCancellationRequested) return;

            _allNotifications = notifTask.Result;
            _allPrs = prTask.Result;
            _allIssues = issueTask.Result;
            _allComments = commentTask.Result;

            RebuildFilterDropdowns();
            ApplyFiltersAndRender();
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            LoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private void RebuildFilterDropdowns()
    {
        var repos = new HashSet<string>();
        foreach (var n in _allNotifications) repos.Add(n.RepoName);
        foreach (var p in _allPrs) repos.Add(p.RepoName);
        foreach (var i in _allIssues) repos.Add(i.RepoName);
        foreach (var c in _allComments) repos.Add(c.RepoName);

        var orgs = repos
            .Select(r => r.Contains('/') ? r.Split('/')[0] : r)
            .Distinct()
            .OrderBy(o => o)
            .ToList();

        _suppressFilterEvent = true;

        OrgFilter.Items.Clear();
        OrgFilter.Items.Add("All orgs");
        foreach (var org in orgs) OrgFilter.Items.Add(org);
        OrgFilter.SelectedIndex = string.IsNullOrEmpty(_selectedOrg)
            ? 0
            : Math.Max(0, orgs.IndexOf(_selectedOrg) + 1);

        RebuildRepoDropdown(repos.OrderBy(r => r).ToList());

        _suppressFilterEvent = false;
    }

    private void RebuildRepoDropdown(List<string> repos)
    {
        var filtered = string.IsNullOrEmpty(_selectedOrg)
            ? repos
            : repos.Where(r => r.StartsWith(_selectedOrg + "/", StringComparison.OrdinalIgnoreCase)).ToList();

        RepoFilter.Items.Clear();
        RepoFilter.Items.Add("All repos");
        foreach (var repo in filtered) RepoFilter.Items.Add(repo);
        RepoFilter.SelectedIndex = string.IsNullOrEmpty(_selectedRepo)
            ? 0
            : Math.Max(0, filtered.IndexOf(_selectedRepo) + 1);
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvent) return;

        if (sender == OrgFilter)
        {
            _selectedOrg = OrgFilter.SelectedIndex <= 0 ? "" : OrgFilter.SelectedItem?.ToString() ?? "";
            _selectedRepo = "";

            var allRepos = new HashSet<string>();
            foreach (var n in _allNotifications) allRepos.Add(n.RepoName);
            foreach (var p in _allPrs) allRepos.Add(p.RepoName);
            foreach (var i in _allIssues) allRepos.Add(i.RepoName);
            foreach (var c in _allComments) allRepos.Add(c.RepoName);

            _suppressFilterEvent = true;
            RebuildRepoDropdown(allRepos.OrderBy(r => r).ToList());
            _suppressFilterEvent = false;
        }
        else if (sender == RepoFilter)
        {
            _selectedRepo = RepoFilter.SelectedIndex <= 0 ? "" : RepoFilter.SelectedItem?.ToString() ?? "";
        }

        ApplyFiltersAndRender();
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        _selectedOrg = "";
        _selectedRepo = "";
        _suppressFilterEvent = true;
        OrgFilter.SelectedIndex = 0;
        RepoFilter.SelectedIndex = 0;
        _suppressFilterEvent = false;
        ApplyFiltersAndRender();
    }

    private void OnInnerTabChanged(object sender, SelectionChangedEventArgs e) { }

    private bool MatchesFilter(string repoName)
    {
        if (!string.IsNullOrEmpty(_selectedRepo))
            return repoName.Equals(_selectedRepo, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(_selectedOrg))
            return repoName.StartsWith(_selectedOrg + "/", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void ApplyFiltersAndRender()
    {
        var notifs = _allNotifications.Where(n => MatchesFilter(n.RepoName)).ToList();
        var prs = _allPrs.Where(p => MatchesFilter(p.RepoName)).ToList();
        var issues = _allIssues.Where(i => MatchesFilter(i.RepoName)).ToList();
        var comments = _allComments.Where(c => MatchesFilter(c.RepoName)).ToList();

        RenderNotifications(notifs);
        RenderAssignedItems(PrList, PrHeader, "ASSIGNED PULL REQUESTS", prs, true);
        RenderAssignedItems(IssueList, IssueHeader, "ASSIGNED ISSUES", issues, false);
        RenderComments(comments);

        NotifTabHeader.Text = notifs.Count > 0 ? $"Notifications ({notifs.Count})" : "Notifications";
        PrTabHeader.Text = prs.Count > 0 ? $"Pull Requests ({prs.Count})" : "Pull Requests";
        IssueTabHeader.Text = issues.Count > 0 ? $"Issues ({issues.Count})" : "Issues";
        CommentTabHeader.Text = comments.Count > 0 ? $"Comments ({comments.Count})" : "Comments";
    }

    private void RenderNotifications(List<NotificationItem> items)
    {
        NotificationsList.Children.Clear();

        NotificationsHeader.Text = items.Count > 0
            ? $"UNREAD NOTIFICATIONS ({items.Count})"
            : "UNREAD NOTIFICATIONS";

        BtnMarkAllRead.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (items.Count == 0)
        {
            NotificationsList.Children.Add(MakeEmptyText("No unread notifications."));
            return;
        }

        foreach (var n in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };

            var timeText = new TextBlock
            {
                Text = TimeAgo(n.UpdatedAt),
                FontSize = 11,
                Foreground = FindBrush("TextDimmed"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(timeText, Dock.Right);
            row.Children.Add(timeText);

            var reason = new TextBlock
            {
                Text = ReasonIcon(n.Reason),
                FontSize = 12,
                Foreground = ReasonColor(n.Reason),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 28
            };
            DockPanel.SetDock(reason, Dock.Left);
            row.Children.Add(reason);

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = n.Title,
                FontSize = 13,
                Foreground = FindBrush("TextPrimary"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            content.Children.Add(new TextBlock
            {
                Text = n.RepoName,
                FontSize = 11,
                Foreground = FindBrush("TextDimmed")
            });
            row.Children.Add(content);

            NotificationsList.Children.Add(MakeClickableRow(row, n.HtmlUrl));
        }
    }

    private void RenderAssignedItems(StackPanel list, TextBlock header, string label,
        List<AssignedItem> items, bool isPr)
    {
        list.Children.Clear();
        header.Text = items.Count > 0 ? $"{label} ({items.Count})" : label;

        if (items.Count == 0)
        {
            list.Children.Add(MakeEmptyText(isPr ? "No assigned pull requests." : "No assigned issues."));
            return;
        }

        foreach (var item in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };

            var timeText = new TextBlock
            {
                Text = TimeAgo(item.UpdatedAt),
                FontSize = 11,
                Foreground = FindBrush("TextDimmed"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(timeText, Dock.Right);
            row.Children.Add(timeText);

            var repo = new TextBlock
            {
                Text = item.RepoName,
                FontSize = 11,
                Foreground = FindBrush("TextDimmed"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                MaxWidth = 200,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            DockPanel.SetDock(repo, Dock.Right);
            row.Children.Add(repo);

            var number = new TextBlock
            {
                Text = $"#{item.Number}",
                FontSize = 12,
                Foreground = FindBrush("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 50
            };
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(number);

            row.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 13,
                Foreground = FindBrush("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            list.Children.Add(MakeClickableRow(row, item.Url));
        }
    }

    private void RenderComments(List<CommentItem> items)
    {
        CommentList.Children.Clear();
        CommentHeader.Text = items.Count > 0
            ? $"MENTIONED COMMENTS ({items.Count})"
            : "MENTIONED COMMENTS";

        if (items.Count == 0)
        {
            CommentList.Children.Add(MakeEmptyText("No comments mentioning you."));
            return;
        }

        foreach (var c in items)
        {
            var card = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            var headerRow = new DockPanel();
            var timeText = new TextBlock
            {
                Text = TimeAgo(c.CreatedAt),
                FontSize = 11,
                Foreground = FindBrush("TextDimmed"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(timeText, Dock.Right);
            headerRow.Children.Add(timeText);

            var authorText = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            authorText.Inlines.Add(new System.Windows.Documents.Run(c.AuthorLogin)
            {
                Foreground = FindBrush("AccentBrush"),
                FontWeight = FontWeights.SemiBold
            });
            authorText.Inlines.Add(new System.Windows.Documents.Run($"  on  {c.RepoName}#{c.IssueNumber}")
            {
                Foreground = FindBrush("TextDimmed")
            });
            headerRow.Children.Add(authorText);
            card.Children.Add(headerRow);

            card.Children.Add(new TextBlock
            {
                Text = c.IssueTitle,
                FontSize = 12,
                Foreground = FindBrush("TextSecondary"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 4)
            });

            var bodyText = c.Body.Length > 500 ? c.Body[..500] + "..." : c.Body;
            var bodyBlock = new TextBlock
            {
                Text = bodyText,
                FontSize = 12,
                Foreground = FindBrush("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(8),
                Background = FindBrush("AppBackground")
            };
            var bodyBorder = new Border
            {
                Child = bodyBlock,
                CornerRadius = new CornerRadius(4),
                BorderBrush = FindBrush("SurfaceBorder"),
                BorderThickness = new Thickness(1)
            };
            card.Children.Add(bodyBorder);

            CommentList.Children.Add(MakeClickableRow(card, c.HtmlUrl, padding: new Thickness(8, 8, 8, 8)));
        }
    }

    private Border MakeClickableRow(UIElement content, string url,
        Thickness? padding = null)
    {
        var border = new Border
        {
            Child = content,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = padding ?? new Thickness(8, 4, 8, 4),
            Cursor = string.IsNullOrEmpty(url) ? null : Cursors.Hand,
            Tag = url
        };

        if (!string.IsNullOrEmpty(url))
        {
            border.MouseLeftButtonDown += (_, _) => OpenUrl(url);
            border.MouseEnter += (s, _) => ((Border)s).Background = FindBrush("TabHoverBackground");
            border.MouseLeave += (s, _) => ((Border)s).Background = Brushes.Transparent;
        }

        return border;
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void ClearLists()
    {
        NotificationsList.Children.Clear();
        NotificationsList.Children.Add(MakeEmptyText("Connect to GitHub to see notifications."));
        PrList.Children.Clear();
        PrList.Children.Add(MakeEmptyText("Connect to GitHub to see pull requests."));
        IssueList.Children.Clear();
        IssueList.Children.Add(MakeEmptyText("Connect to GitHub to see issues."));
        CommentList.Children.Clear();
        CommentList.Children.Add(MakeEmptyText("Connect to GitHub to see comments."));
        NotificationsHeader.Text = "UNREAD NOTIFICATIONS";
        PrHeader.Text = "ASSIGNED PULL REQUESTS";
        IssueHeader.Text = "ASSIGNED ISSUES";
        CommentHeader.Text = "MENTIONED COMMENTS";
        BtnMarkAllRead.Visibility = Visibility.Collapsed;
        NotifTabHeader.Text = "Notifications";
        PrTabHeader.Text = "Pull Requests";
        IssueTabHeader.Text = "Issues";
        CommentTabHeader.Text = "Comments";
    }

    private TextBlock MakeEmptyText(string text) => new()
    {
        Text = text,
        Foreground = FindBrush("TextDimmed"),
        FontSize = 12,
        Margin = new Thickness(0, 8, 0, 8)
    };

    private static string TimeAgo(DateTime dt)
    {
        var span = DateTime.UtcNow - dt.ToUniversalTime();
        if (span.TotalMinutes < 1) return "now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d";
        return dt.ToString("MMM dd");
    }

    private static string ReasonIcon(string reason) => reason switch
    {
        "mention" => "@",
        "review_requested" => "PR",
        "assign" => "=",
        "comment" => "//",
        "ci_activity" => "CI",
        "approval_requested" => "OK",
        _ => "*"
    };

    private Brush ReasonColor(string reason) => reason switch
    {
        "mention" => FindBrush("PayloadColor"),
        "review_requested" => FindBrush("AccentBrush"),
        _ => FindBrush("TextSecondary")
    };

    private Brush FindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.White;

    public Dictionary<string, string> SaveState()
    {
        var state = new Dictionary<string, string>
        {
            ["tab"] = InnerTabs.SelectedIndex.ToString()
        };
        if (!string.IsNullOrEmpty(_selectedOrg))
            state["org"] = _selectedOrg;
        if (!string.IsNullOrEmpty(_selectedRepo))
            state["repo"] = _selectedRepo;
        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("org", out var org))
            _selectedOrg = org;
        if (state.TryGetValue("repo", out var repo))
            _selectedRepo = repo;
        if (state.TryGetValue("tab", out var tab) && int.TryParse(tab, out var idx))
            InnerTabs.SelectedIndex = idx;
    }
}
