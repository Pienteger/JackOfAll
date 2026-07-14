using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class ApiClientToolView : UserControl, IStatefulTool
{
    private List<ApiRequest> _collection = [];
    private List<ApiRequest> _history = [];
    private ApiResponse? _lastResponse;
    private CancellationTokenSource? _cts;

    public ApiClientToolView()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSidebar();
    }

    private void LoadSidebar()
    {
        _collection = ApiClientService.LoadCollection();
        _history = ApiClientService.LoadHistory();
        RenderSaved();
        RenderHistory();
    }

    // ── Send ────────────────────────────────────────────────────────

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        var request = BuildRequest();
        BtnSend.IsEnabled = false;
        BtnSend.Content = "...";
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
            ApiClientService.AddToHistory(request);
            _history = ApiClientService.LoadHistory();
            RenderHistory();
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
            BtnSend.Content = "Send";
        }
    }

    private void ShowResponse(ApiResponse resp)
    {
        StatusBadge.Visibility = Visibility.Visible;
        StatusText.Text = $"{resp.StatusCode} {resp.StatusText}";

        if (resp.StatusCode >= 200 && resp.StatusCode < 300)
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(34, 139, 34));
        else if (resp.StatusCode >= 400)
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));
        else
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(200, 150, 0));

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
            AuthValue = AuthValueBox.Text.Trim()
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
        for (int i = 0; i < MethodPicker.Items.Count; i++)
        {
            if ((MethodPicker.Items[i] as ComboBoxItem)?.Content?.ToString() == req.Method)
            {
                MethodPicker.SelectedIndex = i;
                break;
            }
        }

        UrlBox.Text = req.Url;
        BodyBox.Text = req.Body;
        AuthValueBox.Text = req.AuthValue;

        SetBodyType(req.BodyType);

        FormDataList.Children.Clear();
        foreach (var f in req.FormData)
            AddKeyValueRow(FormDataList, f.Key, f.Value, f.Enabled);

        for (int i = 0; i < AuthType.Items.Count; i++)
        {
            if ((AuthType.Items[i] as ComboBoxItem)?.Content?.ToString() == req.AuthType)
            {
                AuthType.SelectedIndex = i;
                break;
            }
        }

        HeadersList.Children.Clear();
        foreach (var h in req.Headers)
            AddKeyValueRow(HeadersList, h.Key, h.Value, h.Enabled);

        ParamsList.Children.Clear();
        foreach (var p in req.Params)
            AddKeyValueRow(ParamsList, p.Key, p.Value, p.Enabled);
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

        request.Name = string.IsNullOrWhiteSpace(request.Name)
            ? $"{request.Method} {(url.Length > 40 ? url[..40] + "..." : url)}"
            : request.Name;

        var existing = _collection.FindIndex(r => r.Name == request.Name);
        if (existing >= 0)
            _collection[existing] = request;
        else
            _collection.Add(request);

        ApiClientService.SaveCollection(_collection);
        RenderSaved();
    }

    private void OnNewRequestClick(object sender, RoutedEventArgs e)
    {
        MethodPicker.SelectedIndex = 0;
        UrlBox.Text = "";
        BodyBox.Text = "";
        SetBodyType("none");
        FormDataList.Children.Clear();
        RawContentType.SelectedIndex = 0;
        AuthType.SelectedIndex = 0;
        AuthValueBox.Text = "";
        HeadersList.Children.Clear();
        ParamsList.Children.Clear();
        ResponseBody.Text = "Send a request to see the response.";
        ResponseHeadersList.Children.Clear();
        StatusBadge.Visibility = Visibility.Collapsed;
        TimingText.Text = "";
    }

    // ── Sidebar rendering ───────────────────────────────────────────

    private void RenderSaved()
    {
        SavedList.Children.Clear();
        foreach (var req in _collection)
        {
            var btn = MakeSidebarButton(req.Name, req.Method);
            var captured = req;
            btn.Click += (_, _) => LoadRequestToUi(captured);

            var ctx = new ContextMenu();
            var del = new MenuItem { Header = "Delete" };
            del.Click += (_, _) =>
            {
                _collection.Remove(captured);
                ApiClientService.SaveCollection(_collection);
                RenderSaved();
            };
            ctx.Items.Add(del);
            btn.ContextMenu = ctx;

            SavedList.Children.Add(btn);
        }
    }

    private void RenderHistory()
    {
        HistoryList.Children.Clear();
        foreach (var req in _history)
        {
            var btn = MakeSidebarButton(req.Name, req.Method);
            var captured = req;
            btn.Click += (_, _) => LoadRequestToUi(captured);
            HistoryList.Children.Add(btn);
        }
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        var historyPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "JackOfAll", "request-history.json");
        if (System.IO.File.Exists(historyPath))
            System.IO.File.Delete(historyPath);
        RenderHistory();
    }

    private Button MakeSidebarButton(string name, string method)
    {
        var methodColor = method switch
        {
            "GET" => Color.FromRgb(97, 175, 239),
            "POST" => Color.FromRgb(152, 195, 121),
            "PUT" => Color.FromRgb(229, 192, 123),
            "PATCH" => Color.FromRgb(209, 154, 102),
            "DELETE" => Color.FromRgb(224, 108, 117),
            _ => Color.FromRgb(150, 150, 150)
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = method,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(methodColor),
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

    // ── IStatefulTool ───────────────────────────────────────────────

    public Dictionary<string, string> SaveState()
    {
        var state = new Dictionary<string, string>
        {
            ["method"] = (MethodPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET",
            ["url"] = UrlBox.Text,
            ["body"] = BodyBox.Text,
            ["bodyType"] = GetBodyType(),
            ["rawContentType"] = (RawContentType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "application/json",
            ["authType"] = (AuthType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None",
            ["authValue"] = AuthValueBox.Text,
            ["requestTab"] = RequestTabs.SelectedIndex.ToString()
        };

        var formData = CollectKeyValues(FormDataList);
        state["formDataCount"] = formData.Count.ToString();
        for (int i = 0; i < formData.Count; i++)
        {
            state[$"fd_{i}_k"] = formData[i].Key;
            state[$"fd_{i}_v"] = formData[i].Value;
            state[$"fd_{i}_e"] = formData[i].Enabled.ToString();
        }

        var headers = CollectKeyValues(HeadersList);
        state["headerCount"] = headers.Count.ToString();
        for (int i = 0; i < headers.Count; i++)
        {
            state[$"h_{i}_k"] = headers[i].Key;
            state[$"h_{i}_v"] = headers[i].Value;
            state[$"h_{i}_e"] = headers[i].Enabled.ToString();
        }

        var parms = CollectKeyValues(ParamsList);
        state["paramCount"] = parms.Count.ToString();
        for (int i = 0; i < parms.Count; i++)
        {
            state[$"p_{i}_k"] = parms[i].Key;
            state[$"p_{i}_v"] = parms[i].Value;
            state[$"p_{i}_e"] = parms[i].Enabled.ToString();
        }

        if (_lastResponse is not null)
            state["lastResponseBody"] = ResponseBody.Text;

        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("method", out var method))
        {
            for (int i = 0; i < MethodPicker.Items.Count; i++)
            {
                if ((MethodPicker.Items[i] as ComboBoxItem)?.Content?.ToString() == method)
                {
                    MethodPicker.SelectedIndex = i;
                    break;
                }
            }
        }

        if (state.TryGetValue("url", out var url)) UrlBox.Text = url;
        if (state.TryGetValue("body", out var body)) BodyBox.Text = body;
        if (state.TryGetValue("authValue", out var av)) AuthValueBox.Text = av;

        if (state.TryGetValue("bodyType", out var bt))
            SetBodyType(bt);

        if (state.TryGetValue("rawContentType", out var rct))
        {
            for (int i = 0; i < RawContentType.Items.Count; i++)
            {
                if ((RawContentType.Items[i] as ComboBoxItem)?.Content?.ToString() == rct)
                {
                    RawContentType.SelectedIndex = i;
                    break;
                }
            }
        }

        FormDataList.Children.Clear();
        if (state.TryGetValue("formDataCount", out var fdc) && int.TryParse(fdc, out var fdCount))
        {
            for (int i = 0; i < fdCount; i++)
            {
                state.TryGetValue($"fd_{i}_k", out var fk);
                state.TryGetValue($"fd_{i}_v", out var fv);
                state.TryGetValue($"fd_{i}_e", out var fe);
                AddKeyValueRow(FormDataList, fk ?? "", fv ?? "", fe != "False");
            }
        }

        if (state.TryGetValue("authType", out var at))
        {
            for (int i = 0; i < AuthType.Items.Count; i++)
            {
                if ((AuthType.Items[i] as ComboBoxItem)?.Content?.ToString() == at)
                {
                    AuthType.SelectedIndex = i;
                    break;
                }
            }
        }

        if (state.TryGetValue("requestTab", out var rt) && int.TryParse(rt, out var rti))
            RequestTabs.SelectedIndex = rti;

        HeadersList.Children.Clear();
        if (state.TryGetValue("headerCount", out var hc) && int.TryParse(hc, out var hCount))
        {
            for (int i = 0; i < hCount; i++)
            {
                state.TryGetValue($"h_{i}_k", out var hk);
                state.TryGetValue($"h_{i}_v", out var hv);
                state.TryGetValue($"h_{i}_e", out var he);
                AddKeyValueRow(HeadersList, hk ?? "", hv ?? "", he != "False");
            }
        }

        ParamsList.Children.Clear();
        if (state.TryGetValue("paramCount", out var pc) && int.TryParse(pc, out var pCount))
        {
            for (int i = 0; i < pCount; i++)
            {
                state.TryGetValue($"p_{i}_k", out var pk);
                state.TryGetValue($"p_{i}_v", out var pv);
                state.TryGetValue($"p_{i}_e", out var pe);
                AddKeyValueRow(ParamsList, pk ?? "", pv ?? "", pe != "False");
            }
        }

        if (state.TryGetValue("lastResponseBody", out var lrb))
            ResponseBody.Text = lrb;
    }
}
