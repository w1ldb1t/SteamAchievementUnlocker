using Common;

namespace Server;

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class SteamService(HttpClient httpClient)
{
    public async Task<IEnumerable<JToken>> FetchAchievementNamesForGameAsync(string apiKey, string appId)
    {
        var url = $"ISteamUserStats/GetSchemaForGame/v2/?key={apiKey}&appid={appId}&l=english&format=json";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        var jo = JObject.Parse(json);
        return jo["game"]?["availableGameStats"]?["achievements"]?.Children()
               ?? Enumerable.Empty<JToken>();
    }

    public async Task<IEnumerable<JToken>> FetchAchievementPercentagesForGameAsync(string apiKey, string appId)
    {
        var url =
            $"ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?key={apiKey}&gameid={appId}&l=english&format=json";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        var jo = JObject.Parse(json);
        return jo["achievementpercentages"]?["achievements"]?.Children()
               ?? Enumerable.Empty<JToken>();
    }

    public async Task<IEnumerable<JToken>> FetchAchievementStatsForGameAsync(string apiKey, string steamId64,
        string appId)
    {
        var url =
            $"ISteamUserStats/GetPlayerAchievements/v1/?key={apiKey}&steamid={steamId64}&appid={appId}&l=english&format=json";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        var jo = JObject.Parse(json);
        return jo["playerstats"]?["achievements"]?.Children()
               ?? Enumerable.Empty<JToken>();
    }

    public async Task<List<Achievement>> GetAchievementListAsync(string apiKey, string steamId64, string appId)
    {
        var namesTask = FetchAchievementNamesForGameAsync(apiKey, appId);
        var percentagesTask = FetchAchievementPercentagesForGameAsync(apiKey, appId);
        var statsTask = FetchAchievementStatsForGameAsync(apiKey, steamId64, appId);

        await Task.WhenAll(namesTask, percentagesTask, statsTask).ConfigureAwait(false);

        var percentLookup = percentagesTask.Result
            .ToDictionary(tok => tok["name"]!.ToString(),
                tok => tok["percent"]!.Value<double>());

        var unlockedLookup = statsTask.Result
            .ToDictionary(tok => tok["apiname"]!.ToString(),
                tok => tok["achieved"]!.Value<bool>());

        return namesTask.Result
            .Select(tok =>
            {
                var name = tok["name"]!.ToString();
                var displayName = tok["displayName"]!.ToString();
                percentLookup.TryGetValue(name, out double pct);
                unlockedLookup.TryGetValue(name, out bool unlocked);
                return new Achievement(name, displayName, pct, unlocked);
            })
            .ToList();
    }
    
    /// <summary>
    /// Fetches the game’s title from the Steam Store API by AppID.
    /// Returns null if the AppID is invalid or data isn’t available.
    /// </summary>
    public async Task<string?> GetGameNameAsync(string appId)
    {
        // Note: this is an absolute URL, so it will ignore the base address of the HttpClient
        // TODO: Make this better?
        var url  = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english";
        var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
        var jo   = JObject.Parse(json);

        var app = jo[appId];
        if (app?["success"]?.Value<bool>() != true)
            return null;

        return app["data"]?["name"]?.Value<string>();
    }
}