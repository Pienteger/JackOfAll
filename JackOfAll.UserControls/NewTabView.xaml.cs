using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class NewTabView : UserControl
{
    public event Action<string>? ToolSelected;

    public NewTabView(ToolRegistry registry)
    {
        InitializeComponent();
        ToolGrid.ItemsSource = registry.Tools;
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string toolId })
            ToolSelected?.Invoke(toolId);
    }
}
