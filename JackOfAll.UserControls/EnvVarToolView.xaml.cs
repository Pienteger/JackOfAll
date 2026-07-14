using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class EnvVarToolView : UserControl, IStatefulTool
{
    private List<EnvVar> _allVars = [];
    private bool _loaded;

    public EnvVarToolView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _loaded = true;
            LoadVars();
        };
    }

    private void LoadVars()
    {
        try
        {
            _allVars = EnvVarService.GetAll();
        }
        catch
        {
            _allVars = [];
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (!_loaded) return;

        var filtered = EnvVarService.Search(_allVars, SearchBox.Text);

        var scope = (ScopeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (scope is not null and not "All Scopes")
        {
            if (Enum.TryParse<EnvironmentVariableTarget>(scope, out var target))
                filtered = filtered.Where(v => v.Target == target).ToList();
        }

        EnvGrid.ItemsSource = filtered;
        StatusText.Text = $"{filtered.Count} of {_allVars.Count} variables";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void OnScopeChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadVars();

    private void OnCopySelectedClick(object sender, RoutedEventArgs e)
    {
        if (EnvGrid.SelectedItem is EnvVar v)
            Clipboard.SetText($"{v.Name}={v.Value}");
    }

    public Dictionary<string, string> SaveState()
    {
        return new Dictionary<string, string>
        {
            ["search"] = SearchBox.Text,
            ["scope"] = ScopeFilter.SelectedIndex.ToString()
        };
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("search", out var search))
            SearchBox.Text = search;
        if (state.TryGetValue("scope", out var scope) && int.TryParse(scope, out var idx))
            ScopeFilter.SelectedIndex = idx;
    }
}
