using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls;

namespace JackOfAll.AppUi;

public partial class MainWindow : Window
{
    private readonly ToolRegistry _registry;
    private readonly IMessageBus _bus;
    private readonly AppSettings _settings;
    private readonly Action _applyTheme;
    private readonly ObservableCollection<ToolTab> _tabs = new();
    private readonly List<ArchivedTab> _archivedTabs = new();
    private IDisposable? _openToolSub;

    private List<AppEnvironment> _environments = [];
    private AppEnvironment? _activeEnvironment;
    private bool _suppressEnvChange;

    private Point _dragStart;
    private bool _isDragging;

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand RestoreTabCommand { get; }

    public MainWindow(ToolRegistry registry, IMessageBus bus, AppSettings settings, Action applyTheme)
    {
        InitializeComponent();
        DataContext = this;
        _registry = registry;
        _bus = bus;
        _settings = settings;
        _applyTheme = applyTheme;

        TabList.ItemsSource = _tabs;

        NewTabCommand = new RelayCommand(AddNewTab);
        CloseTabCommand = new RelayCommand(CloseCurrentTab);
        SettingsCommand = new RelayCommand(ShowSettings);
        RestoreTabCommand = new RelayCommand(RestoreLastTab);

        _openToolSub = _bus.Subscribe<OpenToolMessage>(OnOpenTool);

        _archivedTabs.AddRange(ArchiveStore.Load());
        UpdateArchivedButton();

        _environments = EnvironmentStore.Load();
        RefreshEnvCombo();
        var activeName = EnvironmentStore.LoadActiveName();
        if (activeName is not null)
        {
            var env = _environments.FirstOrDefault(e => e.Name == activeName);
            if (env is not null)
            {
                _suppressEnvChange = true;
                for (int i = 0; i < EnvCombo.Items.Count; i++)
                {
                    if ((EnvCombo.Items[i] as ComboBoxItem)?.Tag?.ToString() == env.Name)
                    {
                        EnvCombo.SelectedIndex = i;
                        break;
                    }
                }
                _suppressEnvChange = false;
                _activeEnvironment = env;
                EnvironmentContext.Set(env);
                RestoreEnvironmentTabs(env.Name);
            }
        }

        if (_tabs.Count == 0) AddNewTab();
    }

    // ── Tab lifecycle ───────────────────────────────────────────────

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

        var view = payload is not null ? toolId switch
        {
            "jwt" => (UserControl)new JwtToolView(_bus, payload),
            _ => factory.CreateView(_bus, msg => _bus.Publish(msg))
        } : factory.CreateView(_bus, msg => _bus.Publish(msg));

        tab.Title = factory.DisplayName;
        tab.ToolId = toolId;
        tab.View = view;

        if (TabList.SelectedItem == tab)
            ContentArea.Content = view;
    }

    private void OnOpenTool(OpenToolMessage msg)
    {
        var factory = _registry.Get(msg.ToolId);
        if (factory is null) return;

        var view = msg.Payload is not null ? msg.ToolId switch
        {
            "jwt" => (UserControl)new JwtToolView(_bus, msg.Payload),
            _ => factory.CreateView(_bus, m => _bus.Publish(m))
        } : factory.CreateView(_bus, m => _bus.Publish(m));

        var tab = new ToolTab
        {
            Title = factory.DisplayName,
            ToolId = msg.ToolId,
            View = view
        };

        _tabs.Add(tab);
        TabList.SelectedItem = tab;
    }

    // ── Close ───────────────────────────────────────────────────────

    private void CloseCurrentTab()
    {
        if (TabList.SelectedItem is ToolTab tab && tab.CanClose && !tab.IsPinned)
            CloseTab(tab);
    }

    private void CloseTab(ToolTab tab)
    {
        if (tab.IsPinned) return;

        if (tab.View is SplitPaneHost splitHost)
            splitHost.Dispose();
        else if (tab.View is IDisposable disposable)
            disposable.Dispose();

        var idx = _tabs.IndexOf(tab);
        _tabs.Remove(tab);

        if (_tabs.Count == 0)
            AddNewTab();
        else if (TabList.SelectedItem is null)
            TabList.SelectedIndex = Math.Min(idx, _tabs.Count - 1);
    }

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        var tab = FindTabFromSender(sender);
        if (tab is not null && tab.CanClose && !tab.IsPinned)
            CloseTab(tab);
    }

    private void OnCloseOtherTabs(object sender, RoutedEventArgs e)
    {
        var keep = FindTabFromSender(sender);
        if (keep is null) return;

        var toClose = _tabs.Where(t => t != keep && t.CanClose && !t.IsPinned).ToList();
        foreach (var t in toClose)
            CloseTab(t);
    }

    private void OnCloseTabsRight(object sender, RoutedEventArgs e)
    {
        var anchor = FindTabFromSender(sender);
        if (anchor is null) return;

        var idx = _tabs.IndexOf(anchor);
        var toClose = _tabs.Skip(idx + 1).Where(t => t.CanClose && !t.IsPinned).ToList();
        foreach (var t in toClose)
            CloseTab(t);
    }

    // ── Archive / Restore ───────────────────────────────────────────

    private void OnArchiveTab(object sender, RoutedEventArgs e)
    {
        var tab = FindTabFromSender(sender);
        if (tab is null || !tab.CanClose || tab.ToolId == "newtab") return;

        var state = new Dictionary<string, string>();
        if (tab.View is IStatefulTool stateful)
            state = stateful.SaveState();

        ContentArea.Content = null;
        _archivedTabs.Add(new ArchivedTab
        {
            ToolId = tab.ToolId,
            Title = tab.Title,
            ArchivedAt = DateTime.Now,
            State = state
        });

        if (tab.View is SplitPaneHost splitHost)
            splitHost.Dispose();
        else if (tab.View is IDisposable disposable)
            disposable.Dispose();
        _tabs.Remove(tab);

        ArchiveStore.Save(_archivedTabs);

        if (_tabs.Count == 0) AddNewTab();
        else if (TabList.SelectedItem == null) TabList.SelectedItem = _tabs[^1];
        UpdateArchivedButton();
    }

    private void RestoreLastTab()
    {
        if (_archivedTabs.Count == 0) return;

        var last = _archivedTabs[^1];
        _archivedTabs.RemoveAt(_archivedTabs.Count - 1);
        RestoreArchivedTab(last);
        ArchiveStore.Save(_archivedTabs);
        UpdateArchivedButton();
    }

    private void RestoreArchivedTab(ArchivedTab archived)
    {
        var factory = _registry.Get(archived.ToolId);
        if (factory is null) return;

        var view = factory.CreateView(_bus, m => _bus.Publish(m));
        if (view is IStatefulTool stateful && archived.State.Count > 0)
            stateful.LoadState(archived.State);

        var tab = new ToolTab
        {
            Title = archived.Title,
            ToolId = archived.ToolId,
            View = view
        };

        _tabs.Add(tab);
        TabList.SelectedItem = tab;
    }

    private void OnArchivedClick(object sender, RoutedEventArgs e)
    {
        ArchivedMenu.Items.Clear();

        if (_archivedTabs.Count == 0)
        {
            ArchivedMenu.Items.Add(new MenuItem
            {
                Header = "No archived tabs",
                IsEnabled = false
            });
        }
        else
        {
            for (var i = _archivedTabs.Count - 1; i >= 0; i--)
            {
                var archived = _archivedTabs[i];
                var idx = i;
                var item = new MenuItem
                {
                    Header = archived.ArchivedAt.Date == DateTime.Today
                        ? $"{archived.Title}  —  {archived.ArchivedAt:HH:mm}"
                        : $"{archived.Title}  —  {archived.ArchivedAt:MMM dd, HH:mm}"
                };
                item.Click += (_, _) =>
                {
                    _archivedTabs.RemoveAt(idx);
                    RestoreArchivedTab(archived);
                    ArchiveStore.Save(_archivedTabs);
                    UpdateArchivedButton();
                };
                ArchivedMenu.Items.Add(item);
            }

            ArchivedMenu.Items.Add(new Separator());
            var clearAll = new MenuItem { Header = "Clear All" };
            clearAll.Click += (_, _) =>
            {
                _archivedTabs.Clear();
                ArchiveStore.Save(_archivedTabs);
                UpdateArchivedButton();
            };
            ArchivedMenu.Items.Add(clearAll);
        }

        ArchivedMenu.IsOpen = true;
    }

    private void UpdateArchivedButton()
    {
        ArchivedBtn.Visibility = _archivedTabs.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ── Environment ────────────────────────────────────────────────

    private void RefreshEnvCombo()
    {
        _suppressEnvChange = true;
        EnvCombo.Items.Clear();

        var addItem = new ComboBoxItem
        {
            Content = "+ Add Environment",
            Tag = "__add__",
            Foreground = (Brush)FindResource("AccentBrush"),
            FontWeight = FontWeights.SemiBold
        };
        EnvCombo.Items.Add(addItem);

        var noneItem = new ComboBoxItem { Content = "(No environment)", Tag = "" };
        EnvCombo.Items.Add(noneItem);

        foreach (var env in _environments)
            EnvCombo.Items.Add(new ComboBoxItem { Content = env.Name, Tag = env.Name });

        if (_activeEnvironment is not null)
        {
            for (int i = 0; i < EnvCombo.Items.Count; i++)
            {
                if ((EnvCombo.Items[i] as ComboBoxItem)?.Tag?.ToString() == _activeEnvironment.Name)
                {
                    EnvCombo.SelectedIndex = i;
                    _suppressEnvChange = false;
                    return;
                }
            }
        }

        EnvCombo.SelectedIndex = 1;
        _suppressEnvChange = false;
    }

    private void OnEnvironmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEnvChange) return;
        if (EnvCombo.SelectedItem is not ComboBoxItem item) return;

        var tag = item.Tag?.ToString() ?? "";

        if (tag == "__add__")
        {
            _suppressEnvChange = true;
            if (_activeEnvironment is not null)
            {
                for (int i = 0; i < EnvCombo.Items.Count; i++)
                {
                    if ((EnvCombo.Items[i] as ComboBoxItem)?.Tag?.ToString() == _activeEnvironment.Name)
                    { EnvCombo.SelectedIndex = i; break; }
                }
            }
            else
            {
                EnvCombo.SelectedIndex = 1;
            }
            _suppressEnvChange = false;

            AddEnvironment();
            return;
        }

        var outgoing = _activeEnvironment;

        if (string.IsNullOrEmpty(tag))
        {
            if (outgoing is not null) ArchiveEnvironmentTabs(outgoing.Name);
            _activeEnvironment = null;
            EnvironmentContext.Set(null);
            EnvironmentStore.SaveActiveName(null);
            if (_tabs.Count == 0) AddNewTab();
            return;
        }

        var env = _environments.FirstOrDefault(e => e.Name == tag);
        if (env is null) return;

        if (outgoing is not null) ArchiveEnvironmentTabs(outgoing.Name);
        else ArchiveEnvironmentTabs(null);

        _activeEnvironment = env;
        EnvironmentContext.Set(env);
        EnvironmentStore.SaveActiveName(env.Name);

        RestoreEnvironmentTabs(env.Name);
        if (_tabs.Count == 0) AddNewTab();
    }

    private void AddEnvironment()
    {
        var dlg = new InputDialog("New Environment", "Environment name:");
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText)) return;

        var name = dlg.ResultText.Trim();
        if (_environments.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Environment '{name}' already exists.", "Duplicate",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderDlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select environment folder" };
        if (folderDlg.ShowDialog() != true) return;

        var env = new AppEnvironment(name, folderDlg.FolderName);
        _environments.Add(env);
        EnvironmentStore.Save(_environments);

        var outgoing = _activeEnvironment;
        if (outgoing is not null) ArchiveEnvironmentTabs(outgoing.Name);
        else ArchiveEnvironmentTabs(null);

        _activeEnvironment = env;
        EnvironmentContext.Set(env);
        EnvironmentStore.SaveActiveName(env.Name);
        RefreshEnvCombo();

        if (_tabs.Count == 0) AddNewTab();
    }

    private void ArchiveEnvironmentTabs(string? envName)
    {
        var toArchive = _tabs.Where(t => t.ToolId != "newtab").ToList();
        foreach (var tab in toArchive)
        {
            var state = new Dictionary<string, string>();
            if (tab.View is IStatefulTool stateful)
                state = stateful.SaveState();

            if (tab.View is SplitPaneHost splitHost)
                splitHost.Dispose();
            else if (tab.View is IDisposable disposable)
                disposable.Dispose();

            _archivedTabs.Add(new ArchivedTab
            {
                ToolId = tab.ToolId,
                Title = tab.Title,
                ArchivedAt = DateTime.Now,
                State = state,
                Environment = envName
            });
        }

        ContentArea.Content = null;
        _tabs.Clear();

        ArchiveStore.Save(_archivedTabs);
        UpdateArchivedButton();
    }

    private void RestoreEnvironmentTabs(string envName)
    {
        var toRestore = _archivedTabs.Where(a => a.Environment == envName).ToList();
        foreach (var archived in toRestore)
        {
            _archivedTabs.Remove(archived);
            var factory = _registry.Get(archived.ToolId);
            if (factory is null) continue;

            var view = factory.CreateView(_bus, m => _bus.Publish(m));
            if (view is IStatefulTool stateful && archived.State.Count > 0)
                stateful.LoadState(archived.State);

            var tab = new ToolTab
            {
                Title = archived.Title,
                ToolId = archived.ToolId,
                View = view
            };
            _tabs.Add(tab);
        }

        if (_tabs.Count > 0)
            TabList.SelectedItem = _tabs[0];

        ArchiveStore.Save(_archivedTabs);
        UpdateArchivedButton();
    }

    // ── Pin ─────────────────────────────────────────────────────────

    private void OnPinTab(object sender, RoutedEventArgs e)
    {
        var tab = FindTabFromSender(sender);
        if (tab is null) return;

        tab.IsPinned = !tab.IsPinned;
        tab.CanClose = !tab.IsPinned;

        if (tab.IsPinned)
        {
            var currentIdx = _tabs.IndexOf(tab);
            var pinnedCount = _tabs.Count(t => t.IsPinned && t != tab);
            if (currentIdx > pinnedCount)
                _tabs.Move(currentIdx, pinnedCount);
        }
    }

    // ── Duplicate ───────────────────────────────────────────────────

    private void OnDuplicateTab(object sender, RoutedEventArgs e)
    {
        var tab = FindTabFromSender(sender);
        if (tab is null || tab.ToolId == "newtab") return;

        var factory = _registry.Get(tab.ToolId);
        if (factory is null) return;

        var view = factory.CreateView(_bus, m => _bus.Publish(m));
        var newTab = new ToolTab
        {
            Title = tab.Title,
            ToolId = tab.ToolId,
            View = view
        };

        var idx = _tabs.IndexOf(tab);
        _tabs.Insert(idx + 1, newTab);
        TabList.SelectedItem = newTab;
    }

    // ── Split ───────────────────────────────────────────────────────

    private void OnSplitRight(object sender, RoutedEventArgs e)
    {
        var tab = FindTabFromSender(sender);
        if (tab is not null && tab.ToolId != "newtab")
            SplitTab(tab, Orientation.Horizontal);
    }

    private void OnSplitDown(object sender, RoutedEventArgs e)
    {
        var tab = FindTabFromSender(sender);
        if (tab is not null && tab.ToolId != "newtab")
            SplitTab(tab, Orientation.Vertical);
    }

    private void SplitTab(ToolTab tab, Orientation orientation)
    {
        if (tab.View is SplitPaneHost host)
        {
            host.Split(orientation);
        }
        else
        {
            if (TabList.SelectedItem == tab)
                ContentArea.Content = null;

            host = new SplitPaneHost(_registry, _bus);
            host.SetLeafContent(tab.View, tab.Title, tab.ToolId);
            host.Split(orientation);
            tab.View = host;

            if (TabList.SelectedItem == tab)
                ContentArea.Content = tab.View;
        }
    }

    // ── Rename ──────────────────────────────────────────────────────

    private void OnTabHeaderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement el && el.DataContext is ToolTab tab)
        {
            tab.IsRenaming = true;
            e.Handled = true;
        }
    }

    private void OnRenameTab(object sender, RoutedEventArgs e)
    {
        var tab = FindTabFromSender(sender);
        if (tab is not null)
            tab.IsRenaming = true;
    }

    private void OnRenameBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        CommitRename(sender);
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename(sender);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (sender is TextBox tb && tb.DataContext is ToolTab tab)
                tab.IsRenaming = false;
            e.Handled = true;
        }
    }

    private static void CommitRename(object sender)
    {
        if (sender is TextBox tb && tb.DataContext is ToolTab tab)
        {
            if (string.IsNullOrWhiteSpace(tb.Text))
                tb.Text = tab.Title;
            tab.Title = tb.Text.Trim();
            tab.IsRenaming = false;
        }
    }

    // ── Drag and drop reorder ───────────────────────────────────────

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(TabList);
        _isDragging = false;
    }

    private void OnTabMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(TabList);
        var diff = _dragStart - pos;

        if (!_isDragging && (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                             Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
        {
            var item = FindListBoxItemAt(e.GetPosition(TabList));
            if (item?.DataContext is ToolTab tab)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(item, tab, DragDropEffects.Move);
                _isDragging = false;
            }
        }
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ToolTab))) return;

        var dragged = (ToolTab)e.Data.GetData(typeof(ToolTab))!;
        var target = FindListBoxItemAt(e.GetPosition(TabList))?.DataContext as ToolTab;

        if (target is null || dragged == target) return;

        var oldIdx = _tabs.IndexOf(dragged);
        var newIdx = _tabs.IndexOf(target);

        if (dragged.IsPinned != target.IsPinned) return;

        if (oldIdx >= 0 && newIdx >= 0)
            _tabs.Move(oldIdx, newIdx);
    }

    private ListBoxItem? FindListBoxItemAt(Point pos)
    {
        var hit = VisualTreeHelper.HitTest(TabList, pos);
        var dep = hit?.VisualHit;
        while (dep is not null and not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);
        return dep as ListBoxItem;
    }

    // ── Selection / navigation ──────────────────────────────────────

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabList.SelectedItem is ToolTab tab)
            ContentArea.Content = tab.View;
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e) => AddNewTab();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowSettings()
    {
        var dialog = new SettingsWindow(_settings, _applyTheme) { Owner = this };
        dialog.ShowDialog();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private ToolTab? FindTabFromSender(object sender)
    {
        Guid? id = sender switch
        {
            Button { Tag: Guid g } => g,
            MenuItem { Tag: Guid g } => g,
            _ => null
        };
        return id.HasValue ? _tabs.FirstOrDefault(t => t.Id == id.Value) : null;
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
