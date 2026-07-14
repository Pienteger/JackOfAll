namespace JackOfAll.Lib.Tools;

public sealed class ArchivedTab
{
    public string ToolId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime ArchivedAt { get; set; }
    public Dictionary<string, string> State { get; set; } = new();
}
