namespace Oeps.CommandToAction.Core.Configuration;

public sealed class AppPaths(string? userDataRoot = null)
{
    public static string DefaultUserDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OEPS", "CommandToAction");
    public string UserDataRoot { get; } = Path.GetFullPath(userDataRoot ?? DefaultUserDataRoot);
    public string SettingsFile => Path.Combine(UserDataRoot, "user-settings.json");
    public string ConfigurationFile => Path.Combine(UserDataRoot, "appsettings.json");
}
