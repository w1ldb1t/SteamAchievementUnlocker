namespace Server;

internal static class Services
{
    private static readonly HttpClient httpClient = new() { BaseAddress = new("https://api.steampowered.com/") };

    public static Logger Logger { get; } = new(Console.Out);
    public static ThreadSafeCounter Counter { get; } = new();
    public static AchievementServiceImpl AchievementService { get; } = new(Logger);
    public static SteamService Steam { get; } = new(httpClient);
}
