using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server;

public class Settings
{
    public Settings() { }

    public static Settings? Load(string path)
    {
        if (!File.Exists(path))
            return null;

        var settings = JsonSerializer.Deserialize<Settings>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            });

        if (settings is null || settings.MinMinutes.HasValue != settings.MaxMinutes.HasValue)
            return null;

        return settings;
    }

    public string ApiKey { get; init; } = string.Empty;
    public string SteamId64 { get; init; } = string.Empty;
    public List<string> AppId { get; set; } = [];
    public int? MinMinutes { get; set; }
    public int? MaxMinutes { get; set; }
    public string? ReferenceSteamId64 { get; set; }

    public UnlockMode UnlockMode => MinMinutes.HasValue ? UnlockMode.Continuous : UnlockMode.Single;
}
