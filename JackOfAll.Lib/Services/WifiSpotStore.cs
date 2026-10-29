using System.IO;
using System.Text.Json;

namespace JackOfAll.Lib.Services;

public sealed record WifiSpot(string Ssid, string Name, int Dbm);

/// <summary>Spots measured in the Wi-Fi hunt, kept per network so rooms can be compared over time.</summary>
public static class WifiSpotStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "wifi-spots.json");

    public static List<WifiSpot> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<WifiSpot>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<WifiSpot> spots)
    {
        try
        {
            AppPaths.EnsureRoot();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(spots.ToList()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // losing a spot list isn't worth interrupting the hunt
        }
    }
}
