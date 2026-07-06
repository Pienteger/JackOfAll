using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class EnvVarToolView : UserControl
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
}
