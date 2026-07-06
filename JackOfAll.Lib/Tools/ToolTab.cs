using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JackOfAll.Lib.Tools;

public partial class ToolTab : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _title = string.Empty;

    public string ToolId { get; init; } = string.Empty;

    [ObservableProperty]
    private UserControl _view = null!;

    public bool CanClose { get; init; } = true;
}
