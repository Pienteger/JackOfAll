using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class ApiEnvironmentManagerWindow : Window
{
    private readonly ObservableCollection<VariableRow> _variables = [];
    private bool _loading;

    public string SelectedEnvironment { get; private set; } = "";

    public ApiEnvironmentManagerWindow()
    {
        InitializeComponent();
        VariableGrid.ItemsSource = _variables;
        Loaded += (_, _) => LoadEnvironments(ApiEnvironmentService.ActiveEnvironment);
    }

    private void LoadEnvironments(string preferred)
    {
        _loading = true;
        EnvironmentPicker.Items.Clear();
        foreach (var env in ApiEnvironmentService.ListEnvironments())
            EnvironmentPicker.Items.Add(new ComboBoxItem { Content = env, Tag = env });

        if (EnvironmentPicker.Items.Count == 0)
        {
            _variables.Clear();
            SelectedEnvironment = "";
            VariableHeader.Text = "VARIABLES";
            StatusText.Text = "Create an environment to start adding variables.";
            _loading = false;
            return;
        }

        var selected = 0;
        for (var i = 0; i < EnvironmentPicker.Items.Count; i++)
        {
            if ((EnvironmentPicker.Items[i] as ComboBoxItem)?.Tag?.ToString() == preferred)
            {
                selected = i;
                break;
            }
        }
        EnvironmentPicker.SelectedIndex = selected;
        _loading = false;
        LoadVariablesForSelectedEnvironment();
    }

    private void OnEnvironmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        LoadVariablesForSelectedEnvironment();
    }

    private void LoadVariablesForSelectedEnvironment()
    {
        SelectedEnvironment = (EnvironmentPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        _variables.Clear();

        if (string.IsNullOrEmpty(SelectedEnvironment))
        {
            VariableHeader.Text = "VARIABLES";
            StatusText.Text = "No environment selected.";
            return;
        }

        foreach (var kv in ApiEnvironmentService.Load(SelectedEnvironment).OrderBy(x => x.Key))
            _variables.Add(new VariableRow { Key = kv.Key, Value = kv.Value });

        VariableHeader.Text = $"VARIABLES - {SelectedEnvironment.ToUpperInvariant()}";
        StatusText.Text = $"{_variables.Count} variable(s)";
    }

    private void OnNewEnvironment(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("New Environment", "Environment name (e.g. dev, uat, prod):") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText)) return;

        var name = ApiEnvironmentService.NormalizeEnvironmentName(dlg.ResultText);
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "Use letters, numbers, dots, underscores, or hyphens for environment names.",
                "Invalid Environment", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApiEnvironmentService.CreateEnvironment(name);
        ApiEnvironmentService.SetActive(name);
        LoadEnvironments(name);
    }

    private void OnDeleteEnvironment(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedEnvironment)) return;
        var result = MessageBox.Show(this, $"Delete environment '{SelectedEnvironment}'?",
            "Delete Environment", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        ApiEnvironmentService.DeleteEnvironment(SelectedEnvironment);
        LoadEnvironments("");
    }

    private void OnAddVariable(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedEnvironment))
        {
            StatusText.Text = "Create or select an environment first.";
            return;
        }

        _variables.Add(new VariableRow { Key = "new_variable", Value = "" });
        VariableGrid.SelectedIndex = _variables.Count - 1;
        VariableGrid.ScrollIntoView(VariableGrid.SelectedItem);
        StatusText.Text = "Edit the variable name and value, then save.";
    }

    private void OnRemoveVariable(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VariableRow row)
            _variables.Remove(row);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedEnvironment))
        {
            DialogResult = true;
            Close();
            return;
        }

        var clean = new Dictionary<string, string>();
        foreach (var row in _variables)
        {
            var key = row.Key.Trim();
            if (string.IsNullOrEmpty(key)) continue;
            if (!ApiEnvironmentService.IsValidVariableName(key))
            {
                MessageBox.Show(this, $"'{key}' is not a valid variable name.",
                    "Invalid Variable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            clean[key] = row.Value;
        }

        ApiEnvironmentService.Save(SelectedEnvironment, clean);
        ApiEnvironmentService.SetActive(SelectedEnvironment);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public sealed class VariableRow
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}