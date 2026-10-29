using System.Windows;
using System.Windows.Controls;

namespace JackOfAll.UserControls;

/// <summary>Right-hand pane showing the whole of the selected clip: full text, the image and its text, or the file list.</summary>
public partial class ClipPreview : UserControl
{
    public ClipPreview() => InitializeComponent();

    public ClipEntry? Entry
    {
        get => DataContext as ClipEntry;
        set
        {
            DataContext = value;
            Body.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            EmptyText.Visibility = value is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
