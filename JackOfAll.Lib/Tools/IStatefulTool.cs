namespace JackOfAll.Lib.Tools;

public interface IStatefulTool
{
    Dictionary<string, string> SaveState();
    void LoadState(Dictionary<string, string> state);
}
