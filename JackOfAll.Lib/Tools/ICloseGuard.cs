namespace JackOfAll.Lib.Tools;

/// <summary>Lets a tool cancel a destructive close while it has unsaved work.</summary>
public interface ICloseGuard
{
    bool ConfirmClose();
}
