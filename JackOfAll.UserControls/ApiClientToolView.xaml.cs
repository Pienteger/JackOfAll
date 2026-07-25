using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class ApiClientToolView : UserControl, IStatefulTool
{
    private List<RequestTab> _openTabs = [];
    private int _activeTabIndex = -1;
    private List<ApiRequest> _workspaceRequests = [];
    private ApiHistoryEntry? _selectedHistoryEntry;
    private ApiResponse? _lastResponse;
    private CancellationTokenSource? _cts;

    private string? ActiveWorkspace => EnvironmentContext.Current?.Name;

    public ApiClientToolView()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(UrlBox, OnUrlBoxPaste);
        Loaded += (_, _) =>
        {
            ApiHistoryDbService.EnsureDatabase();
            SyncWorkspaceFromEnvironment();
            LoadEnvironments();
            if (_openTabs.Count == 0)
                AddNewTab();
            else
            {
                ApplyTabState(_openTabs[_activeTabIndex]);
                RenderTabBar();
            }
        };
    }

    private void OnUrlBoxPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string))) return;
        var text = (string)e.DataObject.GetData(typeof(string));
        if (!CurlParser.IsCurl(text)) return;

        e.CancelCommand();
        var req = CurlParser.Parse(text);
        LoadRequestToUi(req);
    }

    // ── Request tab state ──────────────────────────────────────────

    private class RequestTab
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Title { get; set; } = "New Request";
        public string Method { get; set; } = "GET";
        public string Url { get; set; } = "";
        public string Body { get; set; } = "";
        public string BodyType { get; set; } = "none";
        public string RawContentType { get; set; } = "application/json";
        public string AuthType { get; set; } = "None";
        public string AuthValue { get; set; } = "";
        public List<KeyValueItem> Headers { get; set; } = [];
        public List<KeyValueItem> Params { get; set; } = [];
        public List<KeyValueItem> FormData { get; set; } = [];
        public List<ExtractionRule> ExtractionRules { get; set; } = [];
        public string? SavedName { get; set; }
        public string? ResponseBody { get; set; }
        public string? ResponseHeaders { get; set; }
        public int? StatusCode { get; set; }
        public string? StatusLine { get; set; }
        public string? Timing { get; set; }
        public int RequestTabIndex { get; set; }
    }

    // ── Tab management ─────────────────────────────────────────────

    private void AddNewTab()
    {
        var tab = new RequestTab();
        _openTabs.Add(tab);
        SwitchToTab(_openTabs.Count - 1);
    }

    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _openTabs.Count) return;

        if (_activeTabIndex >= 0 && _activeTabIndex < _openTabs.Count && IsLoaded)
            CaptureCurrentState(_openTabs[_activeTabIndex]);

        _activeTabIndex = index;

        if (IsLoaded)
        {
            ApplyTabState(_openTabs[index]);
            RenderTabBar();
        }
    }

    private void CloseTab(int index)
    {
        if (_openTabs.Count <= 1) return;

        if (index == _activeTabIndex && IsLoaded)
            CaptureCurrentState(_openTabs[index]);

        _openTabs.RemoveAt(index);

        if (_activeTabIndex >= _openTabs.Count)
            _activeTabIndex = _openTabs.Count - 1;
        else if (_activeTabIndex > index)
            _activeTabIndex--;

        if (IsLoaded)
        {
            ApplyTabState(_openTabs[_activeTabIndex]);
            RenderTabBar();
        }
    }

    private void CaptureCurrentState(RequestTab tab)
    {
        tab.Method = (MethodPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
        tab.Url = UrlBox.Text;
        tab.Body = BodyBox.Text;
        tab.BodyType = GetBodyType();
        tab.RawContentType = (RawContentType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "application/json";
        tab.AuthType = (AuthType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
        tab.AuthValue = AuthValueBox.Text;
        tab.Headers = CollectKeyValues(HeadersList);
        tab.Params = CollectKeyValues(ParamsList);
        tab.FormData = CollectKeyValues(FormDataList);
        tab.ExtractionRules = CollectExtractionRules();
        tab.ResponseBody = ResponseBody.Text;
        tab.RequestTabIndex = RequestTabs.SelectedIndex;

        if (StatusBadge.Visibility == Visibility.Visible)
        {
            tab.StatusLine = StatusText.Text;
            tab.Timing = TimingText.Text;
        }
        else
        {
            tab.StatusLine = null;
            tab.Timing = null;
        }

        tab.Title = ComputeTabTitle(tab);
    }

    private void ApplyTabState(RequestTab tab)
    {
        SelectComboByContent(MethodPicker, tab.Method);
        UrlBox.Text = tab.Url;
        BodyBox.Text = tab.Body;
        AuthValueBox.Text = tab.AuthValue;
        SetBodyType(tab.BodyType);
        SelectComboByContent(RawContentType, tab.RawContentType);
        SelectComboByContent(AuthType, tab.AuthType);

        HeadersList.Children.Clear();
        foreach (var h in tab.Headers)
            AddKeyValueRow(HeadersList, h.Key, h.Value, h.Enabled);

        ParamsList.Children.Clear();
        foreach (var p in tab.Params)
            AddKeyValueRow(ParamsList, p.Key, p.Value, p.Enabled);

        FormDataList.Children.Clear();
        foreach (var f in tab.FormData)
            AddKeyValueRow(FormDataList, f.Key, f.Value, f.Enabled);

        ExtractionRulesList.Children.Clear();
        foreach (var r in tab.ExtractionRules)
            AddExtractionRuleRow(r.Variable, r.Source, r.Expression);

        ResponseBody.Text = tab.ResponseBody ?? "Send a request to see the response.";
        ResponseHeadersList.Children.Clear();

        if (tab.StatusLine is not null)
        {
            StatusBadge.Visibility = Visibility.Visible;
            StatusText.Text = tab.StatusLine;
            TimingText.Text = tab.Timing ?? "";
            ColorizeStatusBadge(tab.StatusLine);
        }
        else
        {
            StatusBadge.Visibility = Visibility.Collapsed;
            TimingText.Text = "";
        }

        if (tab.RequestTabIndex >= 0 && tab.RequestTabIndex < RequestTabs.Items.Count)
            RequestTabs.SelectedIndex = tab.RequestTabIndex;

        RenderRequestHistory(ActiveWorkspace ?? "", tab.SavedName ?? "");
    }

    private void RenderTabBar()
    {
        RequestTabBar.Children.Clear();
        for (int i = 0; i < _openTabs.Count; i++)
        {
            var tab = _openTabs[i];
            var idx = i;

            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            var methodColor = GetMethodColor(tab.Method);
            sp.Children.Add(new TextBlock
            {
                Text = tab.Method,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(methodColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });

            sp.Children.Add(new TextBlock
            {
                Text = tab.Title.Length > 22 ? tab.Title[..22] + "..." : tab.Title,
                FontSize = 11,
                Foreground = (Brush)FindResource(i == _activeTabIndex ? "TextPrimary" : "TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (_openTabs.Count > 1)
            {
                var closeBtn = new Button
                {
                    Content = "×",
                    FontSize = 12,
                    Padding = new Thickness(0),
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = (Brush)FindResource("TextDimmed"),
                    Width = 16, Height = 16,
                    VerticalAlignment = VerticalAlignment.Center
                };
                closeBtn.Click += (_, _) => CloseTab(idx);
                sp.Children.Add(closeBtn);
            }

            var tabBtn = new Border
            {
                Child = sp,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 2, 0),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Background = i == _activeTabIndex ? (Brush)FindResource("SurfaceBackground") : Brushes.Transparent,
                BorderBrush = i == _activeTabIndex ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
                BorderThickness = new Thickness(0, 2, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            tabBtn.MouseLeftButtonDown += (_, _) => SwitchToTab(idx);
            RequestTabBar.Children.Add(tabBtn);
        }
    }

    private static string ComputeTabTitle(RequestTab tab)
    {
        if (!string.IsNullOrWhiteSpace(tab.SavedName))
            return tab.SavedName;
        if (!string.IsNullOrWhiteSpace(tab.Url))
        {
            var url = tab.Url;
            if (url.Length > 30) url = url[..30] + "...";
            return url;
        }
        return "New Request";
    }

    // ── Workspace (driven by global environment) ────────────────────

    private void SyncWorkspaceFromEnvironment()
    {
        var ws = ActiveWorkspace;
        if (ws is not null)
        {
            if (!ApiWorkspaceService.List().Contains(ws))
                ApiWorkspaceService.Create(ws);
            _workspaceRequests = ApiWorkspaceService.LoadRequests(ws);
        }
        else
        {
            _workspaceRequests = [];
        }
        RenderSaved();
        SidebarTitle.Text = ws is not null ? ws.ToUpperInvariant() : "REQUESTS";
    }

    // ── OpenAPI Import ────────────────────────────────────────────────

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var ctx = new ContextMenu();

        var fromFile = new MenuItem { Header = "From File..." };
        fromFile.Click += async (_, _) =>
        {
            if (!EnsureWorkspaceForImport()) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "OpenAPI files|*.json;*.yaml;*.yml|JSON files|*.json|All files|*.*",
                DefaultExt = ".json",
                Title = "Import OpenAPI Specification"
            };
            if (dlg.ShowDialog() != true) return;
            await DoImportAsync(() => OpenApiImporter.FromFileAsync(dlg.FileName));
        };
        ctx.Items.Add(fromFile);

        var fromUrl = new MenuItem { Header = "From URL..." };
        fromUrl.Click += async (_, _) =>
        {
            if (!EnsureWorkspaceForImport()) return;
            var dlg = new InputDialog("Import OpenAPI", "Enter OpenAPI spec URL:");
            var owner = Window.GetWindow(this);
            if (owner is not null) dlg.Owner = owner;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText))
                return;
            await DoImportAsync(() => OpenApiImporter.FromUrlAsync(dlg.ResultText.Trim()));
        };
        ctx.Items.Add(fromUrl);

        ctx.IsOpen = true;
    }

    private bool EnsureWorkspaceForImport()
    {
        if (ActiveWorkspace is not null) return true;

        MessageBox.Show(
            "Select an environment first to import requests into.",
            "No Environment", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private async Task DoImportAsync(Func<Task<List<ApiRequest>>> importFunc)
    {
        try
        {
            var requests = await importFunc();
            if (requests.Count == 0)
            {
                MessageBox.Show("No requests found in the specification.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var req in requests)
                _workspaceRequests.Add(req);

            ApiWorkspaceService.SaveRequests(ActiveWorkspace!, _workspaceRequests);
            RenderSaved();

            MessageBox.Show($"Imported {requests.Count} request(s).", "Import Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Send ────────────────────────────────────────────────────────

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        var request = BuildRequest();
        BtnSend.IsEnabled = false;
        SendButtonText.Text = "...";
        StatusBadge.Visibility = Visibility.Collapsed;
        TimingText.Text = "";
        ResponseBody.Text = "Sending...";
        ResponseHeadersList.Children.Clear();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            _lastResponse = await ApiClientService.SendAsync(request, _cts.Token);
            ShowResponse(_lastResponse);

            if (request.ExtractionRules.Count > 0 && !string.IsNullOrEmpty(ApiEnvironmentService.ActiveEnvironment))
            {
                ApiClientService.ApplyExtractionRules(_lastResponse, request.ExtractionRules);
                RenderVars();
                UpdateEnvVarCount();
            }

            if (_activeTabIndex >= 0 && _activeTabIndex < _openTabs.Count)
            {
                var tab = _openTabs[_activeTabIndex];
                tab.Title = ComputeTabTitle(tab);
                RenderTabBar();

                if (!string.IsNullOrWhiteSpace(tab.SavedName) && _lastResponse is not null)
                {
                    ApiHistoryDbService.Add(ActiveWorkspace ?? "", tab.SavedName, request, _lastResponse);
                    RenderRequestHistory(ActiveWorkspace ?? "", tab.SavedName);
                }
            }
        }
        catch (TaskCanceledException)
        {
            ResponseBody.Text = "Request cancelled.";
        }
        catch (Exception ex)
        {
            ResponseBody.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnSend.IsEnabled = true;
            SendButtonText.Text = "Send";
        }
    }

    private void ShowResponse(ApiResponse resp)
    {
        StatusBadge.Visibility = Visibility.Visible;
        StatusText.Text = $"{resp.StatusCode} {resp.StatusText}";
        ColorizeStatusBadge($"{resp.StatusCode}");
        StatusText.Foreground = Brushes.White;
        TimingText.Text = $"{resp.ElapsedMs} ms";

        var body = resp.Body;
        if (resp.Headers.TryGetValue("Content-Type", out var ct) && ct.Contains("json"))
            body = ApiClientService.FormatJson(body);
        ResponseBody.Text = body;

        ResponseHeadersList.Children.Clear();
        foreach (var h in resp.Headers)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = h.Key,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 12,
                Width = 200
            });
            row.Children.Add(new TextBlock
            {
                Text = h.Value,
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            ResponseHeadersList.Children.Add(row);
        }
    }

    private void ColorizeStatusBadge(string statusLine)
    {
        if (int.TryParse(statusLine.Split(' ')[0], out var code))
        {
            if (code >= 200 && code < 300)
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(34, 139, 34));
            else if (code >= 400)
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));
            else
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(200, 150, 0));
        }
    }

    // ── Body type switching ────────────────────────────────────────

    private string GetBodyType()
    {
        if (BodyFormData.IsChecked == true) return "form-data";
        if (BodyUrlEncoded.IsChecked == true) return "x-www-form-urlencoded";
        if (BodyRaw.IsChecked == true) return "raw";
        return "none";
    }

    private void SetBodyType(string type)
    {
        BodyNone.IsChecked = type == "none";
        BodyFormData.IsChecked = type == "form-data";
        BodyUrlEncoded.IsChecked = type == "x-www-form-urlencoded";
        BodyRaw.IsChecked = type == "raw";
        UpdateBodyPanels();
    }

    private void OnBodyTypeChanged(object sender, RoutedEventArgs e) => UpdateBodyPanels();

    private void UpdateBodyPanels()
    {
        if (BodyNonePanel is null) return;
        var type = GetBodyType();
        BodyNonePanel.Visibility = type == "none" ? Visibility.Visible : Visibility.Collapsed;
        BodyFormPanel.Visibility = type is "form-data" or "x-www-form-urlencoded" ? Visibility.Visible : Visibility.Collapsed;
        BodyRawPanel.Visibility = type == "raw" ? Visibility.Visible : Visibility.Collapsed;
        FormDataLabel.Text = type == "x-www-form-urlencoded" ? "FORM URL-ENCODED" : "FORM DATA";
    }

    private void OnAddFormDataClick(object sender, RoutedEventArgs e) =>
        AddKeyValueRow(FormDataList);

    // ── Build request from UI ───────────────────────────────────────

    private ApiRequest BuildRequest()
    {
        var method = (MethodPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
        var authType = (AuthType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
        var bodyType = GetBodyType();

        var request = new ApiRequest
        {
            Method = method,
            Url = UrlBox.Text.Trim(),
            Headers = CollectKeyValues(HeadersList),
            Params = CollectKeyValues(ParamsList),
            Body = bodyType == "raw" ? BodyBox.Text : "",
            BodyType = bodyType,
            FormData = bodyType is "form-data" or "x-www-form-urlencoded"
                ? CollectKeyValues(FormDataList) : [],
            AuthType = authType,
            AuthValue = AuthValueBox.Text.Trim(),
            ExtractionRules = CollectExtractionRules()
        };

        if (bodyType == "raw")
        {
            var ct = (RawContentType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "application/json";
            var hasContentType = request.Headers.Any(h =>
                h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            if (!hasContentType)
                request.Headers.Add(new KeyValueItem { Enabled = true, Key = "Content-Type", Value = ct });
        }

        return request;
    }

    private void LoadRequestToUi(ApiRequest req)
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _openTabs.Count)
        {
            var tab = _openTabs[_activeTabIndex];
            tab.SavedName = req.Name;
        }

        SelectComboByContent(MethodPicker, req.Method);
        UrlBox.Text = req.Url;
        BodyBox.Text = req.Body;
        AuthValueBox.Text = req.AuthValue;
        SetBodyType(req.BodyType);

        FormDataList.Children.Clear();
        foreach (var f in req.FormData)
            AddKeyValueRow(FormDataList, f.Key, f.Value, f.Enabled);

        SelectComboByContent(AuthType, req.AuthType);

        HeadersList.Children.Clear();
        foreach (var h in req.Headers)
            AddKeyValueRow(HeadersList, h.Key, h.Value, h.Enabled);

        ParamsList.Children.Clear();
        foreach (var p in req.Params)
            AddKeyValueRow(ParamsList, p.Key, p.Value, p.Enabled);

        ExtractionRulesList.Children.Clear();
        foreach (var rule in req.ExtractionRules)
            AddExtractionRuleRow(rule.Variable, rule.Source, rule.Expression);

        if (_activeTabIndex >= 0 && _activeTabIndex < _openTabs.Count && IsLoaded)
        {
            CaptureCurrentState(_openTabs[_activeTabIndex]);
            RenderTabBar();
        }
    }

    private void OpenRequestInNewTab(ApiRequest req)
    {
        var tab = new RequestTab
        {
            Method = req.Method,
            Url = req.Url,
            Body = req.Body,
            BodyType = req.BodyType,
            AuthType = req.AuthType,
            AuthValue = req.AuthValue,
            Headers = [.. req.Headers],
            Params = [.. req.Params],
            FormData = [.. req.FormData],
            ExtractionRules = [.. req.ExtractionRules],
            SavedName = req.Name,
            Title = req.Name
        };
        _openTabs.Add(tab);
        SwitchToTab(_openTabs.Count - 1);
    }

    // ── Key-value rows ──────────────────────────────────────────────

    private void OnAddHeaderClick(object sender, RoutedEventArgs e) =>
        AddKeyValueRow(HeadersList);

    private void OnAddParamClick(object sender, RoutedEventArgs e) =>
        AddKeyValueRow(ParamsList);

    private void AddKeyValueRow(StackPanel parent, string key = "", string value = "", bool enabled = true)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

        var removeBtn = new Button
        {
            Content = "×",
            Width = 22, Height = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextDimmed"),
            BorderThickness = new Thickness(0),
            FontSize = 14
        };
        DockPanel.SetDock(removeBtn, Dock.Right);
        removeBtn.Click += (_, _) => parent.Children.Remove(row);
        row.Children.Add(removeBtn);

        var cb = new CheckBox
        {
            IsChecked = enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        DockPanel.SetDock(cb, Dock.Left);
        row.Children.Add(cb);

        var keyBox = new TextBox
        {
            Text = key,
            Width = 150,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
            Background = (Brush)FindResource("SurfaceBackground"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("SurfaceBorder")
        };
        DockPanel.SetDock(keyBox, Dock.Left);
        row.Children.Add(keyBox);

        var valueBox = new TextBox
        {
            Text = value,
            Padding = new Thickness(4, 3, 4, 3),
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
            Background = (Brush)FindResource("SurfaceBackground"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("SurfaceBorder")
        };
        row.Children.Add(valueBox);

        parent.Children.Add(row);
    }

    private static List<KeyValueItem> CollectKeyValues(StackPanel panel)
    {
        var items = new List<KeyValueItem>();
        foreach (var child in panel.Children)
        {
            if (child is not DockPanel row) continue;
            var cb = row.Children.OfType<CheckBox>().FirstOrDefault();
            var textBoxes = row.Children.OfType<TextBox>().ToList();
            if (textBoxes.Count < 2) continue;
            items.Add(new KeyValueItem
            {
                Enabled = cb?.IsChecked ?? true,
                Key = textBoxes[0].Text,
                Value = textBoxes[1].Text
            });
        }
        return items;
    }

    // ── Auth ────────────────────────────────────────────────────────

    private void OnAuthTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AuthValuePanel is null) return;
        var type = (AuthType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
        AuthValuePanel.Visibility = type == "None" ? Visibility.Collapsed : Visibility.Visible;
        AuthHint.Text = type == "Basic" ? "Username:Password" : "Token:";
    }

    // ── Save / collection ───────────────────────────────────────────

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var request = BuildRequest();
        var url = request.Url;
        if (string.IsNullOrWhiteSpace(url)) return;

        var currentTab = _activeTabIndex >= 0 ? _openTabs[_activeTabIndex] : null;
        request.Name = currentTab?.SavedName ?? "";

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            var dlg = new InputDialog("Save Request", "Request name:");
            var owner = Window.GetWindow(this);
            if (owner is not null) dlg.Owner = owner;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText))
                return;
            request.Name = dlg.ResultText.Trim();
        }

        if (currentTab is not null)
            currentTab.SavedName = request.Name;

        if (ActiveWorkspace is not null)
        {
            var existing = _workspaceRequests.FindIndex(r => r.Name == request.Name);
            if (existing >= 0)
                _workspaceRequests[existing] = request;
            else
                _workspaceRequests.Add(request);
            ApiWorkspaceService.SaveRequests(ActiveWorkspace, _workspaceRequests);
        }
        else
        {
            var collection = ApiClientService.LoadCollection();
            var existing = collection.FindIndex(r => r.Name == request.Name);
            if (existing >= 0)
                collection[existing] = request;
            else
                collection.Add(request);
            ApiClientService.SaveCollection(collection);
        }

        RenderSaved();
        if (currentTab is not null && IsLoaded)
        {
            CaptureCurrentState(currentTab);
            RenderTabBar();
        }
    }

    private void OnNewRequestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new InputDialog("New Request", "Request name:");
            var owner = Window.GetWindow(this);
            if (owner is not null)
                dlg.Owner = owner;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText))
                return;

            var name = dlg.ResultText.Trim();
            var tab = new RequestTab
            {
                SavedName = name,
                Title = name
            };
            _openTabs.Add(tab);
            SwitchToTab(_openTabs.Count - 1);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create request tab: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Sidebar rendering ───────────────────────────────────────────

    private void RenderSaved()
    {
        SavedList.Children.Clear();
        var requests = ActiveWorkspace is not null
            ? _workspaceRequests
            : ApiClientService.LoadCollection();

        foreach (var req in requests)
        {
            var btn = MakeSidebarButton(req.Name, req.Method);
            var captured = req;

            btn.Click += (_, _) => LoadRequestToUi(captured);

            var ctx = new ContextMenu();

            var openInTab = new MenuItem { Header = "Open in New Tab" };
            openInTab.Click += (_, _) => OpenRequestInNewTab(captured);
            ctx.Items.Add(openInTab);

            ctx.Items.Add(new Separator());

            var del = new MenuItem { Header = "Delete" };
            del.Click += (_, _) =>
            {
                if (ActiveWorkspace is not null)
                {
                    _workspaceRequests.Remove(captured);
                    ApiWorkspaceService.SaveRequests(ActiveWorkspace, _workspaceRequests);
                }
                else
                {
                    var coll = ApiClientService.LoadCollection();
                    coll.RemoveAll(r => r.Name == captured.Name);
                    ApiClientService.SaveCollection(coll);
                }
                ApiHistoryDbService.DeleteForRequest(ActiveWorkspace ?? "", captured.Name);
                RenderSaved();
            };
            ctx.Items.Add(del);

            btn.ContextMenu = ctx;
            SavedList.Children.Add(btn);
        }

        if (requests.Count == 0)
        {
            SavedList.Children.Add(new TextBlock
            {
                Text = "No saved requests",
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextDimmed"),
                FontSize = 11,
                Margin = new Thickness(8, 4, 8, 4)
            });
        }
    }

    private static Color GetMethodColor(string method) => method switch
    {
        "GET" => Color.FromRgb(97, 175, 239),
        "POST" => Color.FromRgb(152, 195, 121),
        "PUT" => Color.FromRgb(229, 192, 123),
        "PATCH" => Color.FromRgb(209, 154, 102),
        "DELETE" => Color.FromRgb(224, 108, 117),
        _ => Color.FromRgb(150, 150, 150)
    };

    private Button MakeSidebarButton(string name, string method)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = method,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(GetMethodColor(method)),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 40
        });
        sp.Children.Add(new TextBlock
        {
            Text = name.Length > 25 ? name[..25] + "..." : name,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        return new Button
        {
            Content = sp,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 1, 4, 1),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    // ── Response actions ────────────────────────────────────────────

    private void OnCopyResponseClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ResponseBody.Text))
            Clipboard.SetText(ResponseBody.Text);
    }

    // ── Request history ─────────────────────────────────────────────

    private void RenderRequestHistory(string workspace, string requestName)
    {
        if (HistoryEntryList is null || HistoryDetailTabs is null) return;

        HistoryEntryList.Children.Clear();
        _selectedHistoryEntry = null;
        BtnFillFromHistory.Visibility = Visibility.Collapsed;
        BtnClearHistory.Visibility = Visibility.Collapsed;
        HistoryDetailTabs.Visibility = Visibility.Collapsed;
        HistoryDetailLabel.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(requestName))
        {
            HistoryTabLabel.Text = "REQUEST HISTORY";
            HistoryDetailLabel.Text = "Save and name this request to see history.";
            return;
        }

        HistoryTabLabel.Text = $"HISTORY — {requestName.ToUpperInvariant()}";
        var entries = ApiHistoryDbService.LoadForRequest(workspace, requestName);

        if (entries.Count == 0)
        {
            HistoryDetailLabel.Text = "No history yet. Send a request to start recording.";
            return;
        }

        BtnClearHistory.Visibility = Visibility.Visible;
        HistoryDetailLabel.Text = "Select an entry to view details.";

        foreach (var entry in entries)
        {
            var captured = entry;
            var btn = MakeHistoryEntryButton(entry);
            btn.Click += (_, _) => ShowHistoryDetail(captured);
            HistoryEntryList.Children.Add(btn);
        }
    }

    private Button MakeHistoryEntryButton(ApiHistoryEntry entry)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var statusColor = entry.StatusCode >= 200 && entry.StatusCode < 300
            ? Color.FromRgb(34, 139, 34)
            : entry.StatusCode >= 400
                ? Color.FromRgb(200, 50, 50)
                : Color.FromRgb(200, 150, 0);

        sp.Children.Add(new TextBlock
        {
            Text = entry.StatusCode.ToString(),
            FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 35
        });
        sp.Children.Add(new TextBlock
        {
            Text = entry.Timestamp.ToLocalTime().ToString("MM/dd HH:mm:ss"),
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(new TextBlock
        {
            Text = $" ({entry.ElapsedMs}ms)",
            FontSize = 9,
            Foreground = (Brush)FindResource("TextDimmed"),
            VerticalAlignment = VerticalAlignment.Center
        });

        return new Button
        {
            Content = sp,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 1, 0, 1),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    private void ShowHistoryDetail(ApiHistoryEntry entry)
    {
        if (HistoryDetailTabs is null || HistoryResponseBody is null) return;

        _selectedHistoryEntry = entry;
        HistoryDetailLabel.Visibility = Visibility.Collapsed;
        HistoryDetailTabs.Visibility = Visibility.Visible;
        BtnFillFromHistory.Visibility = Visibility.Visible;

        try
        {
            var response = JsonSerializer.Deserialize<ApiResponse>(entry.ResponseJson);
            var body = response?.Body ?? "";
            if (response?.Headers.TryGetValue("Content-Type", out var ct) == true
                && ct.Contains("json"))
                body = ApiClientService.FormatJson(body);
            HistoryResponseBody.Text = body;
        }
        catch { HistoryResponseBody.Text = entry.ResponseJson; }

        try
        {
            var request = JsonSerializer.Deserialize<ApiRequest>(entry.RequestJson);
            HistoryRequestBody.Text = JsonSerializer.Serialize(request,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch { HistoryRequestBody.Text = entry.RequestJson; }
    }

    private void OnFillFromHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_selectedHistoryEntry is null) return;
        try
        {
            var request = JsonSerializer.Deserialize<ApiRequest>(_selectedHistoryEntry.RequestJson);
            if (request is not null)
                LoadRequestToUi(request);
        }
        catch { }
    }

    private void OnClearRequestHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex < 0 || _activeTabIndex >= _openTabs.Count) return;
        var tab = _openTabs[_activeTabIndex];
        if (string.IsNullOrWhiteSpace(tab.SavedName)) return;

        var result = MessageBox.Show(
            $"Clear all history for \"{tab.SavedName}\"?",
            "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        ApiHistoryDbService.DeleteForRequest(ActiveWorkspace ?? "", tab.SavedName);
        RenderRequestHistory(ActiveWorkspace ?? "", tab.SavedName);
    }

    // ── Environment ─────────────────────────────────────────────────

    private void LoadEnvironments()
    {
        EnvPicker.Items.Clear();
        EnvPicker.Items.Add(new ComboBoxItem { Content = "No Environment", Tag = "" });

        foreach (var env in ApiEnvironmentService.ListEnvironments())
            EnvPicker.Items.Add(new ComboBoxItem { Content = env, Tag = env });

        var active = ApiEnvironmentService.ActiveEnvironment;
        for (int i = 0; i < EnvPicker.Items.Count; i++)
        {
            if ((EnvPicker.Items[i] as ComboBoxItem)?.Tag?.ToString() == active)
            {
                EnvPicker.SelectedIndex = i;
                break;
            }
        }
        if (EnvPicker.SelectedIndex < 0) EnvPicker.SelectedIndex = 0;

        UpdateEnvVarCount();
        RenderVars();
    }

    private void OnEnvChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnvPicker.SelectedItem is not ComboBoxItem item) return;
        var name = item.Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(name))
            ApiEnvironmentService.ClearActive();
        else
            ApiEnvironmentService.SetActive(name);

        UpdateEnvVarCount();
        RenderVars();
    }

    private void UpdateEnvVarCount()
    {
        var active = ApiEnvironmentService.ActiveEnvironment;
        EnvVarCount.Text = string.IsNullOrEmpty(active) ? "" : $"{ApiEnvironmentService.Variables.Count} var(s)";
    }

    private void OnManageEnvClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var newItem = new MenuItem { Header = "New Environment..." };
        newItem.Click += (_, _) =>
        {
            var dlg = new InputDialog("New Environment", "Environment name (e.g. dev, uat, prod):");
            var owner = Window.GetWindow(this);
            if (owner is not null) dlg.Owner = owner;
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
            {
                var name = dlg.ResultText.Trim().ToLowerInvariant().Replace(" ", "-");
                ApiEnvironmentService.CreateEnvironment(name);
                LoadEnvironments();
                SelectComboByTag(EnvPicker, name);
            }
        };
        menu.Items.Add(newItem);

        var envs = ApiEnvironmentService.ListEnvironments();
        if (envs.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var env in envs)
            {
                var delItem = new MenuItem { Header = $"Delete '{env}'" };
                var captured = env;
                delItem.Click += (_, _) =>
                {
                    ApiEnvironmentService.DeleteEnvironment(captured);
                    LoadEnvironments();
                };
                menu.Items.Add(delItem);
            }
        }

        menu.IsOpen = true;
    }

    // ── Vars tab ────────────────────────────────────────────────────

    private void RenderVars()
    {
        VarsList.Children.Clear();
        var active = ApiEnvironmentService.ActiveEnvironment;
        VarsTabLabel.Text = string.IsNullOrEmpty(active)
            ? "ENVIRONMENT VARIABLES"
            : $"VARIABLES — {active.ToUpperInvariant()}";

        if (string.IsNullOrEmpty(active))
        {
            VarsList.Children.Add(new TextBlock
            {
                Text = "Select an environment to view variables.",
                Foreground = (Brush)FindResource("TextDimmed"),
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0)
            });
            return;
        }

        foreach (var kv in ApiEnvironmentService.Variables.OrderBy(x => x.Key))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

            var removeBtn = new Button
            {
                Content = "×", Width = 22, Height = 22, Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("TextDimmed"),
                BorderThickness = new Thickness(0), FontSize = 14
            };
            var capturedKey = kv.Key;
            removeBtn.Click += (_, _) =>
            {
                ApiEnvironmentService.RemoveVariable(capturedKey);
                RenderVars();
                UpdateEnvVarCount();
            };
            DockPanel.SetDock(removeBtn, Dock.Right);
            row.Children.Add(removeBtn);

            var keyBlock = new TextBlock
            {
                Text = kv.Key,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 12, Width = 160,
                FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(keyBlock, Dock.Left);
            row.Children.Add(keyBlock);

            var valBox = new TextBox
            {
                Text = kv.Value,
                FontSize = 12, Padding = new Thickness(4, 3, 4, 3),
                FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
                Background = (Brush)FindResource("SurfaceBackground"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("SurfaceBorder")
            };
            var valKey = kv.Key;
            valBox.LostFocus += (_, _) => ApiEnvironmentService.SetVariable(valKey, valBox.Text);
            row.Children.Add(valBox);

            VarsList.Children.Add(row);
        }
    }

    private void OnAddManualVar(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ApiEnvironmentService.ActiveEnvironment)) return;
        var dlg = new InputDialog("Add Variable", "Variable name:");
        var owner = Window.GetWindow(this);
        if (owner is not null) dlg.Owner = owner;
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
        {
            ApiEnvironmentService.SetVariable(dlg.ResultText.Trim(), "");
            RenderVars();
            UpdateEnvVarCount();
        }
    }

    // ── Extraction rules ────────────────────────────────────────────

    private void OnAddExtractionRule(object sender, RoutedEventArgs e) => AddExtractionRuleRow();

    private void AddExtractionRuleRow(string variable = "", string source = "jsonpath", string expression = "")
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };

        var removeBtn = new Button
        {
            Content = "×", Width = 22, Height = 22, Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextDimmed"),
            BorderThickness = new Thickness(0), FontSize = 14
        };
        DockPanel.SetDock(removeBtn, Dock.Right);
        removeBtn.Click += (_, _) => ExtractionRulesList.Children.Remove(row);
        row.Children.Add(removeBtn);

        var varBox = new TextBox
        {
            Text = variable, Width = 100,
            Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
            Background = (Brush)FindResource("SurfaceBackground"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("SurfaceBorder"),
            ToolTip = "Variable name"
        };
        DockPanel.SetDock(varBox, Dock.Left);
        row.Children.Add(varBox);

        var sourceBox = new ComboBox
        {
            Width = 100, FontSize = 11, Margin = new Thickness(0, 0, 6, 0),
            Background = (Brush)FindResource("SurfaceBackground"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("SurfaceBorder")
        };
        sourceBox.Items.Add(new ComboBoxItem { Content = "jsonpath" });
        sourceBox.Items.Add(new ComboBoxItem { Content = "regex" });
        sourceBox.Items.Add(new ComboBoxItem { Content = "header" });
        sourceBox.SelectedIndex = source switch { "regex" => 1, "header" => 2, _ => 0 };
        DockPanel.SetDock(sourceBox, Dock.Left);
        row.Children.Add(sourceBox);

        var exprBox = new TextBox
        {
            Text = expression,
            Padding = new Thickness(4, 3, 4, 3),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
            Background = (Brush)FindResource("SurfaceBackground"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("SurfaceBorder"),
            ToolTip = "jsonpath: $.data.token | regex: \"token\":\"(.+?)\" | header: Authorization"
        };
        row.Children.Add(exprBox);

        ExtractionRulesList.Children.Add(row);
    }

    private List<ExtractionRule> CollectExtractionRules()
    {
        var rules = new List<ExtractionRule>();
        foreach (var child in ExtractionRulesList.Children)
        {
            if (child is not DockPanel row) continue;
            var textBoxes = row.Children.OfType<TextBox>().ToList();
            var comboBox = row.Children.OfType<ComboBox>().FirstOrDefault();
            if (textBoxes.Count < 2) continue;
            rules.Add(new ExtractionRule
            {
                Variable = textBoxes[0].Text,
                Source = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "jsonpath",
                Expression = textBoxes[1].Text
            });
        }
        return rules;
    }

    private void OnRunExtraction(object sender, RoutedEventArgs e)
    {
        if (_lastResponse is null || string.IsNullOrEmpty(ApiEnvironmentService.ActiveEnvironment))
            return;
        var rules = CollectExtractionRules();
        if (rules.Count == 0) return;
        ApiClientService.ApplyExtractionRules(_lastResponse, rules);
        RenderVars();
        UpdateEnvVarCount();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void SelectComboByContent(ComboBox combo, string value)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if ((combo.Items[i] as ComboBoxItem)?.Content?.ToString() == value)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if ((combo.Items[i] as ComboBoxItem)?.Tag?.ToString() == tag)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    // ── IStatefulTool ───────────────────────────────────────────────

    public Dictionary<string, string> SaveState()
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _openTabs.Count && IsLoaded)
            CaptureCurrentState(_openTabs[_activeTabIndex]);

        var state = new Dictionary<string, string>
        {
            ["tabCount"] = _openTabs.Count.ToString(),
            ["activeTab"] = _activeTabIndex.ToString(),
            ["activeEnv"] = ApiEnvironmentService.ActiveEnvironment
        };

        for (int t = 0; t < _openTabs.Count; t++)
        {
            var tab = _openTabs[t];
            var p = $"t{t}_";
            state[$"{p}method"] = tab.Method;
            state[$"{p}url"] = tab.Url;
            state[$"{p}body"] = tab.Body;
            state[$"{p}bodyType"] = tab.BodyType;
            state[$"{p}rawCt"] = tab.RawContentType;
            state[$"{p}authType"] = tab.AuthType;
            state[$"{p}authValue"] = tab.AuthValue;
            state[$"{p}savedName"] = tab.SavedName ?? "";
            state[$"{p}respBody"] = tab.ResponseBody ?? "";
            state[$"{p}statusLine"] = tab.StatusLine ?? "";
            state[$"{p}timing"] = tab.Timing ?? "";
            state[$"{p}reqTab"] = tab.RequestTabIndex.ToString();

            SerializeKvList(state, $"{p}h", tab.Headers);
            SerializeKvList(state, $"{p}p", tab.Params);
            SerializeKvList(state, $"{p}fd", tab.FormData);

            state[$"{p}exCount"] = tab.ExtractionRules.Count.ToString();
            for (int i = 0; i < tab.ExtractionRules.Count; i++)
            {
                state[$"{p}ex{i}_var"] = tab.ExtractionRules[i].Variable;
                state[$"{p}ex{i}_src"] = tab.ExtractionRules[i].Source;
                state[$"{p}ex{i}_expr"] = tab.ExtractionRules[i].Expression;
            }
        }

        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        SyncWorkspaceFromEnvironment();

        if (state.TryGetValue("activeEnv", out var envName) && !string.IsNullOrEmpty(envName))
        {
            ApiEnvironmentService.SetActive(envName);
            LoadEnvironments();
        }

        if (!state.TryGetValue("tabCount", out var tc) || !int.TryParse(tc, out var tabCount) || tabCount == 0)
        {
            AddNewTab();
            return;
        }

        _openTabs.Clear();
        for (int t = 0; t < tabCount; t++)
        {
            var p = $"t{t}_";
            var tab = new RequestTab
            {
                Method = state.GetValueOrDefault($"{p}method", "GET"),
                Url = state.GetValueOrDefault($"{p}url", ""),
                Body = state.GetValueOrDefault($"{p}body", ""),
                BodyType = state.GetValueOrDefault($"{p}bodyType", "none"),
                RawContentType = state.GetValueOrDefault($"{p}rawCt", "application/json"),
                AuthType = state.GetValueOrDefault($"{p}authType", "None"),
                AuthValue = state.GetValueOrDefault($"{p}authValue", ""),
                SavedName = state.GetValueOrDefault($"{p}savedName", "") is var sn && sn != "" ? sn : null,
                ResponseBody = state.GetValueOrDefault($"{p}respBody", "") is var rb && rb != "" ? rb : null,
                StatusLine = state.GetValueOrDefault($"{p}statusLine", "") is var sl && sl != "" ? sl : null,
                Timing = state.GetValueOrDefault($"{p}timing", "") is var tm && tm != "" ? tm : null,
            };

            if (state.TryGetValue($"{p}reqTab", out var rt) && int.TryParse(rt, out var rti))
                tab.RequestTabIndex = rti;

            tab.Headers = DeserializeKvList(state, $"{p}h");
            tab.Params = DeserializeKvList(state, $"{p}p");
            tab.FormData = DeserializeKvList(state, $"{p}fd");

            if (state.TryGetValue($"{p}exCount", out var exc) && int.TryParse(exc, out var exCount))
            {
                for (int i = 0; i < exCount; i++)
                {
                    tab.ExtractionRules.Add(new ExtractionRule
                    {
                        Variable = state.GetValueOrDefault($"{p}ex{i}_var", ""),
                        Source = state.GetValueOrDefault($"{p}ex{i}_src", "jsonpath"),
                        Expression = state.GetValueOrDefault($"{p}ex{i}_expr", "")
                    });
                }
            }

            tab.Title = ComputeTabTitle(tab);
            _openTabs.Add(tab);
        }

        var activeIdx = 0;
        if (state.TryGetValue("activeTab", out var at) && int.TryParse(at, out var ati))
            activeIdx = Math.Clamp(ati, 0, _openTabs.Count - 1);

        _activeTabIndex = -1;
        SwitchToTab(activeIdx);
    }

    private static void SerializeKvList(Dictionary<string, string> state, string prefix, List<KeyValueItem> items)
    {
        state[$"{prefix}Count"] = items.Count.ToString();
        for (int i = 0; i < items.Count; i++)
        {
            state[$"{prefix}{i}_k"] = items[i].Key;
            state[$"{prefix}{i}_v"] = items[i].Value;
            state[$"{prefix}{i}_e"] = items[i].Enabled.ToString();
        }
    }

    private static List<KeyValueItem> DeserializeKvList(Dictionary<string, string> state, string prefix)
    {
        var items = new List<KeyValueItem>();
        if (!state.TryGetValue($"{prefix}Count", out var c) || !int.TryParse(c, out var count)) return items;
        for (int i = 0; i < count; i++)
        {
            items.Add(new KeyValueItem
            {
                Key = state.GetValueOrDefault($"{prefix}{i}_k", ""),
                Value = state.GetValueOrDefault($"{prefix}{i}_v", ""),
                Enabled = state.GetValueOrDefault($"{prefix}{i}_e", "True") != "False"
            });
        }
        return items;
    }
}
