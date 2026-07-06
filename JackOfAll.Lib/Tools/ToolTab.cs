using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JackOfAll.Lib.Tools;

public partial class ToolTab : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _title = string.Empty;

    public string ToolId { get; set; } = string.Empty;

    [ObservableProperty]
    private UserControl _view = null!;

    [ObservableProperty]
    private bool _canClose = true;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isRenaming;

    public string PinMenuText => IsPinned ? "Unpin" : "Pin";

    partial void OnIsPinnedChanged(bool value) => OnPropertyChanged(nameof(PinMenuText));
}
