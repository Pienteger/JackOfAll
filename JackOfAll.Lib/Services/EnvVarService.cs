using System.Collections;

namespace JackOfAll.Lib.Services;

public sealed record EnvVar(string Name, string Value, EnvironmentVariableTarget Target);

public static class EnvVarService
{
    public static List<EnvVar> GetAll()
    {
        var results = new List<EnvVar>();
        foreach (EnvironmentVariableTarget target in Enum.GetValues<EnvironmentVariableTarget>())
        {
            IDictionary? vars;
            try
            {
                vars = Environment.GetEnvironmentVariables(target);
            }
            catch
            {
                continue;
            }

            foreach (DictionaryEntry entry in vars)
            {
                var key = entry.Key?.ToString();
                if (key is null) continue;
                results.Add(new EnvVar(key, entry.Value?.ToString() ?? string.Empty, target));
            }
        }

        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    public static List<EnvVar> Search(List<EnvVar> all, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return all;

        return all.Where(v =>
            v.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            v.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
