namespace Server;

public class Settings
{
    public required string ApiKey { get; set; }
    public required string SteamId64 { get; set; }
    public List<string> AppId { get; set; } = [];
    public int? MinMinutes { get; set; }
    public int? MaxMinutes { get; set; }
    public string? ReferenceSteamId64 { get; set; }
}