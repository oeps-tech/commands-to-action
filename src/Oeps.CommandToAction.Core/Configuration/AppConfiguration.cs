using System.Text.Json;
using System.Text.Json.Nodes;

namespace Oeps.CommandToAction.Core.Configuration;

public sealed class AppConfiguration
{
    public string GitHubOwner { get; set; } = "oeps-tech";
    public string GitHubRepository { get; set; } = "commands-to-action";
    public string PackagePrefix { get; set; } = "Oeps.CommandToAction";
    public int ReaderPort { get; set; } = 8766;

    public static AppConfiguration Load(string baseDirectory, string userDataRoot)
    {
        var merged = new JsonObject();
        foreach (var path in new[] { Path.Combine(baseDirectory, "appsettings.json"), Path.Combine(userDataRoot, "appsettings.json") }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            var values = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject ?? throw new InvalidDataException($"Configuration must be a JSON object: {path}");
            foreach (var pair in values)
            {
                var key = merged.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, pair.Key, StringComparison.OrdinalIgnoreCase)) ?? pair.Key;
                merged[key] = pair.Value?.DeepClone();
            }
        }
        var config = merged.Deserialize<AppConfiguration>(JsonStorage.Options) ?? new();
        if (config.ReaderPort is < 1 or > 65535) throw new InvalidDataException("readerPort must be between 1 and 65535.");
        return config;
    }
}

internal static class JsonStorage
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip
    };

    internal static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
