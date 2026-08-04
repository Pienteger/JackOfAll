using System.Windows.Controls;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class UtilityToolView : UserControl, IStatefulTool
{
    public UtilityToolView()
    {
        InitializeComponent();
    }

    public Dictionary<string, string> SaveState()
    {
        var state = new Dictionary<string, string>
        {
            ["selectedTab"] = UtilityTabs.SelectedIndex.ToString()
        };

        AddPrefixedState(state, "generator.", GeneratorView.SaveState());
        AddPrefixedState(state, "hash.", HashView.SaveState());
        AddPrefixedState(state, "timestamp.", TimestampView.SaveState());
        AddPrefixedState(state, "files.", FileSplitterView.SaveState());
        AddPrefixedState(state, "images.", ImageConverterView.SaveState());
        AddPrefixedState(state, "textEncode.", TextEncodeView.SaveState());

        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        GeneratorView.LoadState(GetPrefixedState(state, "generator."));
        HashView.LoadState(GetPrefixedState(state, "hash."));
        TimestampView.LoadState(GetPrefixedState(state, "timestamp."));
        FileSplitterView.LoadState(GetPrefixedState(state, "files."));
        ImageConverterView.LoadState(GetPrefixedState(state, "images."));
        TextEncodeView.LoadState(GetPrefixedState(state, "textEncode."));

        if (state.TryGetValue("selectedTab", out var selectedTab) &&
            int.TryParse(selectedTab, out var tabIndex) &&
            tabIndex >= 0 &&
            tabIndex < UtilityTabs.Items.Count)
        {
            UtilityTabs.SelectedIndex = tabIndex;
        }
    }

    private static void AddPrefixedState(
        Dictionary<string, string> target,
        string prefix,
        Dictionary<string, string> source)
    {
        foreach (var (key, value) in source)
            target[prefix + key] = value;
    }

    private static Dictionary<string, string> GetPrefixedState(
        Dictionary<string, string> state,
        string prefix)
    {
        return state
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(
                pair => pair.Key[prefix.Length..],
                pair => pair.Value);
    }
}
