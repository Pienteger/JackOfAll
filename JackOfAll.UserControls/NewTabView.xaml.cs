using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class NewTabView : UserControl
{
    public event Action<string>? ToolSelected;

    private static readonly Color[] CardPalette =
    [
        Color.FromRgb(0x63, 0x66, 0xf1), // indigo
        Color.FromRgb(0x10, 0xb9, 0x81), // emerald
        Color.FromRgb(0xf5, 0x9e, 0x0b), // amber
        Color.FromRgb(0xf4, 0x3f, 0x5e), // rose
        Color.FromRgb(0x06, 0xb6, 0xd4), // cyan
        Color.FromRgb(0xa8, 0x55, 0xf7), // purple
        Color.FromRgb(0xf9, 0x73, 0x16), // orange
        Color.FromRgb(0x14, 0xb8, 0xa6), // teal
        Color.FromRgb(0xec, 0x48, 0x99), // pink
        Color.FromRgb(0x3b, 0x82, 0xf6), // blue
        Color.FromRgb(0x22, 0xc5, 0x5e), // green
        Color.FromRgb(0xef, 0x44, 0x44), // red
        Color.FromRgb(0x8b, 0x5c, 0xf6), // violet
        Color.FromRgb(0x0e, 0xa5, 0xe9), // sky
        Color.FromRgb(0x84, 0xcc, 0x16), // lime
    ];

    public NewTabView(ToolRegistry registry)
    {
        InitializeComponent();
        var cards = registry.Tools.Select((t, i) => new ToolCard(
            t.ToolId,
            t.DisplayName,
            t.IconGlyph,
            new SolidColorBrush(CardPalette[i % CardPalette.Length])
        )).ToList();
        ToolGrid.ItemsSource = cards;
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string toolId })
            ToolSelected?.Invoke(toolId);
    }

    private record ToolCard(string ToolId, string DisplayName, string IconGlyph, SolidColorBrush AccentColor);
}
