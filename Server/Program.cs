using System.Diagnostics;
using System.Net;
using Common;
using ServiceWire.TcpIp;

namespace Server;

internal class Program
{
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "Steam Achievement Unlocker";
        Services.Logger.Info("Steam Achievement Unlocker");

        var settings = Settings.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        if (settings is null)
        {
            Services.Logger.Error("Failed to load settings.json. Make sure the file exists and is valid.");
            return;
        }

        // Start ServiceWire TCP host
        var endpoint = new IPEndPoint(IPAddress.Loopback, 9090);
        var host = new TcpHost(endpoint);
        host.AddService<IAchievementService>(Services.AchievementService);
        host.Open();

        // Check the validity of the api key
        var validKey = Task.Run(async () => await Services.Steam.ValidateApiKeyAsync(settings.ApiKey)).Result;
        if (!validKey)
        {
            Services.Logger.Error("Invalid Steam Web API key. Check the 'apiKey' field in settings.json.");
            return;
        }

        var useReferenceUser = !string.IsNullOrEmpty(settings.ReferenceSteamId64);
        if (useReferenceUser)
        {
            string? refName = Task.Run(async () =>
                await Services.Steam.GetPlayerNameAsync(settings.ApiKey, settings.ReferenceSteamId64!)).Result;
            Services.Logger.Warning($"Using reference profile: {refName ?? "Unknown"} (ID: {settings.ReferenceSteamId64})");
        }

        Services.Logger.Info("Unlocking achievements for the following games:");
        var tasks = new List<Task>();
        foreach (var appId in settings.AppId)
        {
            var gameName = Task.Run(async () => await Services.Steam.GetGameNameAsync(appId)).Result;
            var game = new Game(appId, gameName ?? "N/A");

            var candidates = TryBuildCandidateQueue(settings, game, useReferenceUser);
            if (candidates is null)
                continue;

            Services.Logger.Info($"• {game.Name} ({candidates.Count} achievements)");

            if (candidates.Count == 0)
                continue;

            tasks.Add(Task.Run(() => ProcessGame(
                game, settings, candidates, settings.UnlockMode)));
        }

        if (settings.UnlockMode == UnlockMode.Single)
            Task.WaitAll(tasks.ToArray());
        else
            Thread.Sleep(Timeout.Infinite);
    }

    static async Task ProcessGame(
        Game game, Settings settings, Queue<Achievement> candidates, UnlockMode unlockMode)
    {
        if (unlockMode == UnlockMode.Single)
        {
            SpawnClient(game, candidates.Dequeue());
            return;
        }

        Debug.Assert(unlockMode == UnlockMode.Continuous);

        var rnd = new ThreadSafeRandom();
        while (candidates.Count > 0)
        {
            var minutes = rnd.Next(settings.MinMinutes!.Value, settings.MaxMinutes!.Value);
            var timeStr = minutes >= 60
                ? $"{minutes / 60.0:F1} hours"
                : $"{minutes} minutes";
            Services.Logger.Info($"[{game.Name}] Next achievement in {timeStr}.");
            await Task.Delay(TimeSpan.FromMinutes(minutes));

            SpawnClient(game, candidates.Dequeue());
        }

        Services.Logger.Info($"[{game.Name}] All achievements unlocked!");
    }

    static Queue<Achievement>? TryBuildCandidateQueue(
        Settings settings, Game game, bool useReferenceUser)
    {
        try
        {
            return Task.Run(() => BuildCandidateQueue(settings, game, useReferenceUser)).Result;
        }
        catch (AggregateException ex)
        {
            Services.Logger.Error($"[{game.Name}] Failed to fetch achievements: {ex.InnerException?.Message ?? ex.Message}");
            return null;
        }
    }

    static async Task<Queue<Achievement>> BuildCandidateQueue(
        Settings settings, Game game, bool useReferenceUser)
    {
        if (useReferenceUser)
        {
            var myAchievements =
                await Services.Steam.GetAchievementListAsync(settings.ApiKey, settings.SteamId64, game.Id,
                    includePercentages: false);
            var myUnlocked = new HashSet<string>(
                myAchievements.Where(x => x.Unlocked).Select(x => x.Id));

            var refAchievements =
                await Services.Steam.GetAchievementListAsync(settings.ApiKey,
                    settings.ReferenceSteamId64!, game.Id, includePercentages: false);

            return new Queue<Achievement>(refAchievements
                .Where(x => x.Unlocked && !myUnlocked.Contains(x.Id))
                .OrderBy(x => x.UnlockTime));
        }

        var all = await Services.Steam.GetAchievementListAsync(settings.ApiKey, settings.SteamId64, game.Id);
        all.RemoveAll(x => x.Unlocked);
        all.Sort((x, y) => y.Percent.CompareTo(x.Percent));
        return new Queue<Achievement>(all);
    }

    static void SpawnClient(Game game, Achievement achievement)
    {
        try
        {
            long ticket = Services.Counter.Increment();
            var package = new Package(game.Id, achievement);
            Services.AchievementService.AddPackage(ticket, package);

            Services.Logger.Info($"[{game.Name}] Unlocking new achievement ...");
            Services.Logger.Debug($"Ticket {ticket} created for {game.Name}:{achievement.Id}.");

            var clientName = OperatingSystem.IsWindows() ? "Client.exe" : "Client";
            var clientPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, clientName);
            Debug.Assert(File.Exists(clientPath));

            var psi = new ProcessStartInfo(clientPath, $"{ticket.ToString()}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            Debug.Assert(process is not null);
            Services.Logger.Debug($"Process with PID {process.Id} started for ticket {ticket}.");

            process.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            Services.Logger.Error($"[{game.Name}] {ex}");
        }
    }
}
