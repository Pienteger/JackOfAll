using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace JackOfAll.UserControls.Helpers;

public static class JsonTreeBuilder
{
    private static readonly Brush KeyBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE));
    private static readonly Brush StringBrush = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78));
    private static readonly Brush NumberBrush = new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8));
    private static readonly Brush BoolBrush = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly Brush NullBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly Brush ObjectBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly Brush ArrayBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0x86, 0xC0));
    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));

    static JsonTreeBuilder()
    {
        KeyBrush.Freeze();
        StringBrush.Freeze();
        NumberBrush.Freeze();
        BoolBrush.Freeze();
        NullBrush.Freeze();
        ObjectBrush.Freeze();
        ArrayBrush.Freeze();
        DimBrush.Freeze();
    }

    public static bool TryBuild(string json, TreeView tree)
    {
        tree.Items.Clear();

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = BuildNode(null, doc.RootElement);
            tree.Items.Add(root);
            root.IsExpanded = true;
            return true;
        }
        catch
        {
            tree.Items.Add(new TreeViewItem
            {
                Header = MakeText("Invalid JSON", NullBrush, true),
                IsEnabled = false
            });
            return false;
        }
    }

    private static TreeViewItem BuildNode(string? key, JsonElement element)
    {
        var item = new TreeViewItem { IsExpanded = false };

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                if (key is not null)
                {
                    panel.Children.Add(MakeText(key, KeyBrush, false));
                    panel.Children.Add(MakeText(": ", DimBrush, false));
                }
                panel.Children.Add(MakeBadge("{}", ObjectBrush));
                var count = element.EnumerateObject().Count();
                panel.Children.Add(MakeText($" {count} {(count == 1 ? "key" : "keys")}", DimBrush, false));
                item.Header = panel;
                item.IsExpanded = true;

                foreach (var prop in element.EnumerateObject())
                    item.Items.Add(BuildNode(prop.Name, prop.Value));
                break;
            }
            case JsonValueKind.Array:
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                if (key is not null)
                {
                    panel.Children.Add(MakeText(key, KeyBrush, false));
                    panel.Children.Add(MakeText(": ", DimBrush, false));
                }
                panel.Children.Add(MakeBadge("[]", ArrayBrush));
                var count = element.GetArrayLength();
                panel.Children.Add(MakeText($" {count} {(count == 1 ? "item" : "items")}", DimBrush, false));
                item.Header = panel;
                item.IsExpanded = true;

                int idx = 0;
                foreach (var child in element.EnumerateArray())
                    item.Items.Add(BuildNode($"[{idx++}]", child));
                break;
            }
            default:
                item.Header = MakeLeaf(key, element);
                break;
        }

        return item;
    }

    private static StackPanel MakeLeaf(string? key, JsonElement element)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        if (key is not null)
        {
            panel.Children.Add(MakeText(key, KeyBrush, false));
            panel.Children.Add(MakeText(": ", DimBrush, false));
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var str = element.GetString() ?? "";
                var display = str.Length > 80 ? str[..77] + "..." : str;
                panel.Children.Add(MakeText($"\"{display}\"", StringBrush, false));
                break;
            case JsonValueKind.Number:
                panel.Children.Add(MakeText(element.GetRawText(), NumberBrush, false));
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                panel.Children.Add(MakeText(element.GetRawText(), BoolBrush, true));
                break;
            case JsonValueKind.Null:
                panel.Children.Add(MakeText("null", NullBrush, true));
                break;
        }

        return panel;
    }

    private static TextBlock MakeText(string text, Brush foreground, bool italic)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
            FontSize = 13,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
    }

    private static Border MakeBadge(string text, Brush color)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30,
                ((SolidColorBrush)color).Color.R,
                ((SolidColorBrush)color).Color.G,
                ((SolidColorBrush)color).Color.B)),
            BorderBrush = color,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = color,
                FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            }
        };
    }
}
