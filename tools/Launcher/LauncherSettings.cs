using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheWaningBorder.Launcher;

/// <summary>
/// Tester-local settings. The key is a per-tester token with no value beyond
/// downloading builds: it grants no access to the source repo, and revoking it
/// is a one-line change on the server that affects nobody else.
/// </summary>
internal sealed class LauncherSettings
{
    /// <summary>Overridable only so the Worker can be pointed at a local wrangler dev.</summary>
    public const string DefaultApiBase = "https://twb-updates.luis-resmart.workers.dev";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("apiBase")]
    public string ApiBase { get; set; } = DefaultApiBase;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(AppPaths.SettingsFile), Options);

                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.ApiBase)) loaded.ApiBase = DefaultApiBase;
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt settings file must never block the launcher. Falling
            // through just asks the tester for their key again.
        }

        return new LauncherSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Options));
    }
}
