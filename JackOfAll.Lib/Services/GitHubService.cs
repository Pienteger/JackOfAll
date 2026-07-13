using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace JackOfAll.Lib.Services;

public record NotificationItem(
    string Id, string Reason, string Title, string RepoName,
    string Type, DateTime UpdatedAt, string HtmlUrl);

public record AssignedItem(
    int Number, string Title, string RepoName,
    string Url, DateTime UpdatedAt);

public record CommentItem(
    string Body, string RepoName, string IssueTitle, int IssueNumber,
    string AuthorLogin, string AuthorAvatarUrl,
    DateTime CreatedAt, string HtmlUrl);

public static class GitHubService
{
    private static HttpClient? _client;
    private static string _username = "";

    public static bool IsConfigured => _client is not null;
    public static string Username => _username;

    public static void Configure(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JackOfAll", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _client = client;
    }

    public static void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        _username = "";
    }

    public static async Task<string> ValidateAsync(CancellationToken ct = default)
    {
        var json = await _client!.GetStringAsync("/user", ct);
        using var doc = JsonDocument.Parse(json);
        _username = doc.RootElement.GetProperty("login").GetString() ?? "";
        return _username;
    }

    public static async Task<List<NotificationItem>> GetNotificationsAsync(CancellationToken ct = default)
    {
        var json = await _client!.GetStringAsync("/notifications?all=false&per_page=50", ct);
        using var doc = JsonDocument.Parse(json);
        var items = new List<NotificationItem>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var subjectUrl = el.GetProperty("subject").TryGetProperty("url", out var u)
                ? u.GetString() ?? "" : "";

            items.Add(new NotificationItem(
                el.GetProperty("id").GetString()!,
                el.GetProperty("reason").GetString()!,
                el.GetProperty("subject").GetProperty("title").GetString()!,
                el.GetProperty("repository").GetProperty("full_name").GetString()!,
                el.GetProperty("subject").GetProperty("type").GetString()!,
                el.GetProperty("updated_at").GetDateTime(),
                ApiUrlToHtml(subjectUrl)));
        }

        return items;
    }

    public static async Task<List<AssignedItem>> GetAssignedPRsAsync(CancellationToken ct = default)
    {
        return await SearchIssuesAsync($"is:open+assignee:{_username}+is:pr", ct);
    }

    public static async Task<List<AssignedItem>> GetAssignedIssuesAsync(CancellationToken ct = default)
    {
        return await SearchIssuesAsync($"is:open+assignee:{_username}+is:issue", ct);
    }

    public static async Task MarkNotificationReadAsync(string threadId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"/notifications/threads/{threadId}");
        await _client!.SendAsync(req, ct);
    }

    public static async Task MarkAllNotificationsReadAsync(CancellationToken ct = default)
    {
        var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Put, "/notifications") { Content = body };
        await _client!.SendAsync(req, ct);
    }

    public static async Task<List<CommentItem>> GetMentionedCommentsAsync(CancellationToken ct = default)
    {
        var json = await _client!.GetStringAsync(
            $"/search/issues?q=is:open+mentions:{_username}&sort=updated&order=desc&per_page=30", ct);
        using var doc = JsonDocument.Parse(json);
        var items = new List<CommentItem>();
        var tasks = new List<Task>();

        foreach (var el in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var htmlUrl = el.GetProperty("html_url").GetString()!;
            var repoName = ExtractRepoFromUrl(htmlUrl);
            var issueNumber = el.GetProperty("number").GetInt32();
            var issueTitle = el.GetProperty("title").GetString()!;
            var commentsUrl = $"/repos/{repoName}/issues/{issueNumber}/comments?per_page=10&sort=updated&direction=desc";

            try
            {
                var commentsJson = await _client!.GetStringAsync(commentsUrl, ct);
                using var commentsDoc = JsonDocument.Parse(commentsJson);
                foreach (var c in commentsDoc.RootElement.EnumerateArray())
                {
                    var authorLogin = c.GetProperty("user").GetProperty("login").GetString() ?? "";
                    if (!authorLogin.Equals(_username, StringComparison.OrdinalIgnoreCase) &&
                        (c.GetProperty("body").GetString() ?? "").Contains($"@{_username}", StringComparison.OrdinalIgnoreCase))
                    {
                        items.Add(new CommentItem(
                            c.GetProperty("body").GetString() ?? "",
                            repoName,
                            issueTitle,
                            issueNumber,
                            authorLogin,
                            c.GetProperty("user").GetProperty("avatar_url").GetString() ?? "",
                            c.GetProperty("created_at").GetDateTime(),
                            c.GetProperty("html_url").GetString() ?? ""));
                    }
                }
            }
            catch { }
        }

        return items.OrderByDescending(c => c.CreatedAt).ToList();
    }

    private static async Task<List<AssignedItem>> SearchIssuesAsync(string query, CancellationToken ct)
    {
        var json = await _client!.GetStringAsync(
            $"/search/issues?q={query}&sort=updated&order=desc&per_page=30", ct);
        using var doc = JsonDocument.Parse(json);
        var items = new List<AssignedItem>();

        foreach (var el in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var htmlUrl = el.GetProperty("html_url").GetString()!;
            var repoName = ExtractRepoFromUrl(htmlUrl);

            items.Add(new AssignedItem(
                el.GetProperty("number").GetInt32(),
                el.GetProperty("title").GetString()!,
                repoName,
                htmlUrl,
                el.GetProperty("updated_at").GetDateTime()));
        }

        return items;
    }

    private static string ApiUrlToHtml(string apiUrl)
    {
        if (string.IsNullOrEmpty(apiUrl)) return "";
        return apiUrl
            .Replace("api.github.com/repos", "github.com")
            .Replace("/pulls/", "/pull/");
    }

    private static string ExtractRepoFromUrl(string htmlUrl)
    {
        // https://github.com/owner/repo/issues/42 → owner/repo
        var uri = new Uri(htmlUrl);
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : "";
    }
}
