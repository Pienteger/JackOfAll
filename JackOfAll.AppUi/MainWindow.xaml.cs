using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls;

namespace JackOfAll.AppUi;

public partial class MainWindow : Window
{
    private readonly ToolRegistry _registry;
    private readonly IMessageBus _bus;
    private readonly ObservableCollection<ToolTab> _tabs = new();
    private IDisposable? _openToolSub;

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }

    public MainWindow(ToolRegistry registry, IMessageBus bus)
    {
        InitializeComponent();
        DataContext = this;
        _registry = registry;
        _bus = bus;

        TabList.ItemsSource = _tabs;

        NewTabCommand = new RelayCommand(AddNewTab);
        CloseTabCommand = new RelayCommand(CloseCurrentTab);

        _openToolSub = _bus.Subscribe<OpenToolMessage>(OnOpenTool);

        AddNewTab();
    }

    private void AddNewTab()
    {
        var view = new NewTabView(_registry);
        var tab = new ToolTab
        {
            Title = "New Tab",
            ToolId = "newtab",
            View = view,
            CanClose = _tabs.Count > 0
        };

        view.ToolSelected += toolId => OpenTool(tab, toolId);

        _tabs.Add(tab);
        TabList.SelectedItem = tab;
    }

    private void OpenTool(ToolTab tab, string toolId, string? payload = null)
    {
        var factory = _registry.Get(toolId);
        if (factory is null) return;

        var view = payload is not null && toolId == "jwt"
            ? new JwtToolView(_bus, payload)
            : factory.CreateView(_bus, msg => _bus.Publish(msg));

        tab.Title = factory.DisplayName;
        tab.View = view;

        // Force content refresh
        if (TabList.SelectedItem == tab)
            ContentArea.Content = view;
    }

    private void OnOpenTool(OpenToolMessage msg)
    {
        var factory = _registry.Get(msg.ToolId);
        if (factory is null) return;

        var view = msg.Payload is not null && msg.ToolId == "jwt"
            ? new JwtToolView(_bus, msg.Payload)
            : factory.CreateView(_bus, m => _bus.Publish(m));

        var tab = new ToolTab
        {
            Title = factory.DisplayName,
            ToolId = msg.ToolId,
            View = view
        };

        _tabs.Add(tab);
        TabList.SelectedItem = tab;
    }

    private void CloseCurrentTab()
    {
        if (TabList.SelectedItem is ToolTab tab && tab.CanClose)
            CloseTab(tab);
    }

    private void CloseTab(ToolTab tab)
    {
        if (tab.View is IDisposable disposable)
            disposable.Dispose();

        var idx = _tabs.IndexOf(tab);
        _tabs.Remove(tab);

        if (_tabs.Count == 0)
        {
            AddNewTab();
        }
        else if (TabList.SelectedItem is null)
        {
            TabList.SelectedIndex = Math.Min(idx, _tabs.Count - 1);
        }
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabList.SelectedItem is ToolTab tab)
            ContentArea.Content = tab.View;
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e)
    {
        AddNewTab();
    }

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var tab = _tabs.FirstOrDefault(t => t.Id == id);
            if (tab is not null && tab.CanClose)
                CloseTab(tab);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _openToolSub?.Dispose();
        foreach (var tab in _tabs)
        {
            if (tab.View is IDisposable disposable)
                disposable.Dispose();
        }
        base.OnClosed(e);
    }

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
