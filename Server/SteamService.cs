using Common;

namespace Server;

using System.Text.Json;

public class SteamService(HttpClient httpClient)
{
    public async Task<bool> ValidateApiKeyAsync(string apiKey)
    {
        var url = $"ISteamWebAPIUtil/GetSupportedAPIList/v1?key={apiKey}";
        var response = await httpClient.GetAsync(url).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<JsonElement> FetchAchievementNamesForGameAsync(string apiKey, string appId)
    {
        var url = $"ISteamUserStats/GetSchemaForGame/v2/?key={apiKey}&appid={appId}&l=english&format=json";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("game")
            .GetProperty("availableGameStats")
            .GetProperty("achievements")
            .Clone();
    }

    public async Task<JsonElement> FetchAchievementPercentagesForGameAsync(string appId)
    {
        var url =
            $"ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId}&format=json";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("achievementpercentages")
            .GetProperty("achievements")
            .Clone();
    }

    public async Task<JsonElement> FetchAchievementStatsForGameAsync(string apiKey, string steamId64,
        string appId)
    {
        var url =
            $"ISteamUserStats/GetPlayerAchievements/v1/?key={apiKey}&steamid={steamId64}&appid={appId}&l=english&format=json";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("playerstats")
            .GetProperty("achievements")
            .Clone();
    }

    public async Task<List<Achievement>> GetAchievementListAsync(string apiKey, string steamId64, string appId,
        bool includePercentages = true)
    {
        var namesTask = FetchAchievementNamesForGameAsync(apiKey, appId);
        var statsTask = FetchAchievementStatsForGameAsync(apiKey, steamId64, appId);

        Task<JsonElement>? percentagesTask = null;
        if (includePercentages)
            percentagesTask = FetchAchievementPercentagesForGameAsync(appId);

        try
        {
            await Task.WhenAll(percentagesTask is not null
                ? new Task[] { namesTask, percentagesTask, statsTask }
                : new Task[] { namesTask, statsTask }).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var profileName = await GetPlayerNameAsync(apiKey, steamId64).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Cannot access achievements for {profileName ?? "Unknown"} (ID: {steamId64}). Ensure the profile's Game Details are set to Public in Steam's Privacy Settings.",
                ex, ex.StatusCode);
        }

        var percentLookup = new Dictionary<string, double>();
        if (percentagesTask is not null)
        {
            foreach (var tok in percentagesTask.Result.EnumerateArray())
                percentLookup[tok.GetProperty("name").GetString()!] = tok.GetProperty("percent").GetDouble();
        }

        var unlockedLookup = new Dictionary<string, (bool achieved, long unlockTime)>();
        foreach (var tok in statsTask.Result.EnumerateArray())
        {
            unlockedLookup[tok.GetProperty("apiname").GetString()!] =
                (tok.GetProperty("achieved").GetInt32() != 0, tok.GetProperty("unlocktime").GetInt64());
        }

        var results = new List<Achievement>();
        foreach (var tok in namesTask.Result.EnumerateArray())
        {
            var name = tok.GetProperty("name").GetString()!;
            var displayName = tok.GetProperty("displayName").GetString()!;
            percentLookup.TryGetValue(name, out double pct);
            unlockedLookup.TryGetValue(name, out var stats);
            results.Add(new Achievement(name, displayName, pct, stats.achieved, stats.unlockTime));
        }

        return results;
    }

    public async Task<string?> GetPlayerNameAsync(string apiKey, string steamId64)
    {
        var url = $"ISteamUser/GetPlayerSummaries/v0002/?key={apiKey}&steamids={steamId64}&format=json";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var players = doc.RootElement.GetProperty("response").GetProperty("players");
        return players.GetArrayLength() > 0
            ? players[0].GetProperty("personaname").GetString()
            : null;
    }

    public async Task<string?> GetGameNameAsync(string appId)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty(appId, out var app))
            return null;
        if (!app.TryGetProperty("success", out var success) || !success.GetBoolean())
            return null;

        return app.GetProperty("data").GetProperty("name").GetString();
    }
}
