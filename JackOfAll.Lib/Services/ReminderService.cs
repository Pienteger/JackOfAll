using System.IO;
using System.Text.Json;

namespace JackOfAll.Lib.Services;

public sealed class ReminderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DueAt { get; set; }
    public bool IsCompleted { get; set; }
    public string Color { get; set; } = "#3b82f6";
}

public static class ReminderService
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "reminders.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<ReminderItem> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ReminderItem>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<ReminderItem> reminders)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(reminders, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
