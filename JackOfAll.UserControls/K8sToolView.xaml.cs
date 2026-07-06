using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class K8sToolView : UserControl, IDisposable
{
    private readonly KubectlService _kubectl = new();
    private readonly IMessageBus _bus;
    private readonly Action<OpenToolMessage> _openTool;
    private readonly ObservableCollection<string> _logLines = new();
    private readonly ConcurrentQueue<string> _logBuffer = new();
    private readonly DispatcherTimer _logDrainTimer;
    private readonly HashSet<string> _detectedTokens = new();
    private const int MaxLogLines = 10_000;
    private bool _autoScroll = true;
    private bool _disposed;

    public K8sToolView(IMessageBus bus, Action<OpenToolMessage> openTool)
    {
        InitializeComponent();
        _bus = bus;
        _openTool = openTool;
        LogList.ItemsSource = _logLines;

        _logDrainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _logDrainTimer.Tick += DrainLogBuffer;
        _logDrainTimer.Start();

        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (!await _kubectl.IsAvailableAsync())
        {
            AlertPanel.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;
            return;
        }

        StatusText.Text = "Loading namespaces...";
        var namespaces = await _kubectl.GetNamespacesAsync();
        NamespaceCombo.ItemsSource = namespaces;
        if (namespaces.Count > 0)
        {
            var defaultIdx = namespaces.IndexOf("default");
            NamespaceCombo.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
        }
        StatusText.Text = "";
    }

    private async void OnNamespaceChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadPodsAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await LoadPodsAsync();
    }

    private async Task LoadPodsAsync()
    {
        if (NamespaceCombo.SelectedItem is not string ns) return;

        StatusText.Text = "Loading pods...";
        RefreshBtn.IsEnabled = false;
        try
        {
            var pods = await _kubectl.GetPodsAsync(ns);
            PodGrid.ItemsSource = pods;
            StatusText.Text = $"{pods.Count} pods";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            RefreshBtn.IsEnabled = true;
        }
    }

    private void OnPodSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PodGrid.SelectedItem is not PodInfo pod) return;

        _logLines.Clear();
        _detectedTokens.Clear();
        LogTitle.Text = $"Logs: {pod.Name}";

        var ns = NamespaceCombo.SelectedItem as string ?? "default";
        _kubectl.StartLogStream(ns, pod.Name, line => _logBuffer.Enqueue(line));
    }

    private void DrainLogBuffer(object? sender, EventArgs e)
    {
        var count = 0;
        while (count < 100 && _logBuffer.TryDequeue(out var line))
        {
            _logLines.Add(line);
            count++;

            var jwt = JwtService.FindJwtInText(line);
            if (jwt is not null && _detectedTokens.Add(jwt))
                _bus.Publish(new TokenDetectedMessage(jwt, "kubernetes"));
        }

        while (_logLines.Count > MaxLogLines)
            _logLines.RemoveAt(0);

        LogLineCount.Text = $"{_logLines.Count} lines";

        if (_autoScroll && _logLines.Count > 0)
            LogList.ScrollIntoView(_logLines[^1]);
    }

    private void OnLogScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
        {
            _autoScroll = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 10;
            JumpToBottomBtn.Visibility = _autoScroll ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void OnJumpToBottom(object sender, RoutedEventArgs e)
    {
        _autoScroll = true;
        JumpToBottomBtn.Visibility = Visibility.Collapsed;
        if (_logLines.Count > 0)
            LogList.ScrollIntoView(_logLines[^1]);
    }

    private async void OnRestartClick(object sender, RoutedEventArgs e)
    {
        if (PodGrid.SelectedItem is not PodInfo pod) return;
        if (NamespaceCombo.SelectedItem is not string ns) return;

        var result = MessageBox.Show(
            $"Restart deployment for {pod.Name}?",
            "Confirm Restart",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        StatusText.Text = "Restarting...";
        var cliResult = await _kubectl.RestartDeploymentAsync(ns, pod.Name);
        StatusText.Text = cliResult.Success ? "Restarted" : $"Error: {cliResult.Error}";

        if (cliResult.Success)
            await LoadPodsAsync();
    }

    private void OnCopyPodName(object sender, RoutedEventArgs e)
    {
        if (PodGrid.SelectedItem is PodInfo pod)
            Clipboard.SetText(pod.Name);
    }

    private void OnDecodeJwt(object sender, RoutedEventArgs e)
    {
        if (LogList.SelectedItem is not string line) return;
        var jwt = JwtService.FindJwtInText(line);
        if (jwt is null)
        {
            MessageBox.Show("No JWT token found in this line.", "No JWT", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _openTool(new OpenToolMessage("jwt", jwt));
    }

    private void OnCopyLogLine(object sender, RoutedEventArgs e)
    {
        if (LogList.SelectedItem is string line)
            Clipboard.SetText(line);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logDrainTimer.Stop();
        _kubectl.StopLogStream();
    }
}
