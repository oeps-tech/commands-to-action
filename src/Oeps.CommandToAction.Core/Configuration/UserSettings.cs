using System.Text.Json;
using System.Text.Json.Serialization;
using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.Core.Configuration;

public sealed class UserSettings
{
    public bool CameraExternal { get; set; }
    public string CameraIp { get; set; } = "";
    public bool UseReader { get; set; } = true;
    public ActionTimings Timings { get; set; } = new();
    [JsonIgnore] public string? LoadError { get; private set; }

    public static UserSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), JsonStorage.Options) ?? new();
            settings.CameraIp ??= "";
            settings.Timings ??= new();
            settings.Timings.Validate();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new() { LoadError = $"Saved settings could not be read. Defaults are in use. {ex.Message}" };
        }
    }

    public void Save(string path)
    {
        Timings.Validate();
        JsonStorage.WriteAtomic(path, this);
    }
}
