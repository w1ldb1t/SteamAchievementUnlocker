using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Common;
using ServiceWire.TcpIp;

namespace Server;

internal class Program
{
    static void Main()
    {
        Console.Title = "Steam Achievement Unlocker";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var logger = new Logger(Console.Out);
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (!File.Exists(settingsPath))
        {
            logger.Error("Settings file not found. Please create a settings.json file.");
            return;
        }

        var settings = JsonSerializer.Deserialize<Settings>(
            File.ReadAllText(settingsPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString });
        if (settings is null)
        {
            logger.Error("Failed to parse settings.json.");
            return;
        }

        if (settings.MinMinutes.HasValue != settings.MaxMinutes.HasValue)
        {
            logger.Error("Both 'minMinutes' and 'maxMinutes' must be set together, or both omitted.");
            return;
        }

        var singleUnlock = !settings.MinMinutes.HasValue;

        // Configure TCP endpoint
        var endpoint = new IPEndPoint(IPAddress.Loopback, 9090);

        var achievementService = new AchievementServiceImpl(logger);

        // Start ServiceWire TCP host
        var host = new TcpHost(endpoint);
        host.AddService<IAchievementService>(achievementService);
        host.Open();

        logger.Info("Steam Achievement Unlocker");

        using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.steampowered.com/") };
        var steamService = new SteamService(httpClient);

        // Check the validity of the api key
        var validKey = Task.Run(async () => await steamService.ValidateApiKeyAsync(settings.ApiKey)).Result;
        if (!validKey)
        {
            logger.Error("Invalid Steam Web API key. Check the 'apiKey' field in settings.json.");
            return;
        }

        // Threadsafe counter for generating tickets
        var counter = new ThreadSafeCounter();

        var useReferenceUser = !string.IsNullOrEmpty(settings.ReferenceSteamId64);
        string? refName = null;
        if (useReferenceUser)
        {
            refName = Task.Run(async () =>
                await steamService.GetPlayerNameAsync(settings.ApiKey, settings.ReferenceSteamId64!)).Result;
            logger.Warning($"Using reference profile: {refName ?? "Unknown"} (ID: {settings.ReferenceSteamId64})");
        }

        if (singleUnlock)
            logger.Info("Single unlock mode.");
        else
            logger.Info($"Continuous mode — unlocking every {settings.MinMinutes}-{settings.MaxMinutes} minutes.");

        logger.Info("Unlocking achievements for the following games:");
        var tasks = new List<Task>();
        foreach (var appId in settings.AppId)
        {
            // Capture the loop variable so each lambda gets its own copy
            var game = appId;
            var gameName = Task.Run(async () => await steamService.GetGameNameAsync(game)).Result;
            logger.Info($"• {gameName}");
            tasks.Add(Task.Run(async () =>
            {
                // Build the candidate list once upfront
                Queue<Achievement> candidates;
                try
                {
                    if (useReferenceUser)
                    {
                        var myAchievements =
                            await steamService.GetAchievementListAsync(settings.ApiKey, settings.SteamId64, game,
                                includePercentages: false);
                        var myUnlocked = new HashSet<string>(
                            myAchievements.Where(x => x.Unlocked).Select(x => x.Id));

                        var refAchievements =
                            await steamService.GetAchievementListAsync(settings.ApiKey,
                                settings.ReferenceSteamId64!, game, includePercentages: false);

                        candidates = new Queue<Achievement>(refAchievements
                            .Where(x => x.Unlocked && !myUnlocked.Contains(x.Id))
                            .OrderBy(x => x.UnlockTime));
                    }
                    else
                    {
                        var all = await steamService.GetAchievementListAsync(settings.ApiKey, settings.SteamId64, game);
                        all.RemoveAll(x => x.Unlocked);
                        all.Sort((x, y) => y.Percent.CompareTo(x.Percent));
                        candidates = new Queue<Achievement>(all);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"[{gameName}] Failed to fetch achievements: {ex.Message}");
                    return;
                }

                logger.Info($"{candidates.Count} achievements to unlock for {gameName}.");

                if (candidates.Count == 0)
                    return;

                if (singleUnlock)
                {
                    try
                    {
                        var achievement = candidates.Dequeue();
                        long ticket = counter.Increment();
                        var package = new Package(game, achievement);
                        achievementService.AddPackage(ticket, package);

                        logger.Info($"Unlocking achievement for {gameName} ...");
                        logger.Debug($"Ticket {ticket} created for {gameName}:{achievement.Id}.");

                        var clientName = OperatingSystem.IsWindows() ? "Client.exe" : "Client";
                        var clientPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, clientName);
                        Debug.Assert(File.Exists(clientPath));

                        var psi = new ProcessStartInfo(
                            clientPath,
                            $"{ticket.ToString()}")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        var process = Process.Start(psi);
                        Debug.Assert(process is not null);
                        logger.Debug($"Process with PID {process.Id} started for ticket {ticket}.");

                        process.WaitForExit(TimeSpan.FromSeconds(30));
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"[{gameName}] {ex}");
                    }
                }
                else
                {
                    var rnd = new ThreadSafeRandom();
                    while (candidates.Count > 0)
                    {
                        try
                        {
                            var minutes = rnd.Next(settings.MinMinutes!.Value, settings.MaxMinutes!.Value);
                            var timeStr = minutes >= 60
                                ? $"{minutes / 60.0:F1} hours"
                                : $"{minutes} minutes";
                            logger.Info($"[{gameName}] Next achievement in {timeStr}.");
                            await Task.Delay(TimeSpan.FromMinutes(minutes));

                            var achievement = candidates.Dequeue();
                            long ticket = counter.Increment();
                            var package = new Package(game, achievement);
                            achievementService.AddPackage(ticket, package);

                            logger.Info($"Unlocking achievement for {gameName} ...");
                            logger.Debug($"Ticket {ticket} created for {gameName}:{achievement.Id}.");

                            var clientName = OperatingSystem.IsWindows() ? "Client.exe" : "Client";
                            var clientPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, clientName);
                            Debug.Assert(File.Exists(clientPath));

                            var psi = new ProcessStartInfo(
                                clientPath,
                                $"{ticket.ToString()}")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            var process = Process.Start(psi);
                            Debug.Assert(process is not null);
                            logger.Debug($"Process with PID {process.Id} started for ticket {ticket}.");

                            process.WaitForExit(TimeSpan.FromSeconds(30));
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"[{gameName}] {ex}");
                        }
                    }

                    logger.Info($"All achievements unlocked for {gameName}!");
                }
            }));
        }

        if (singleUnlock)
            Task.WaitAll(tasks.ToArray());
        else
            Thread.Sleep(Timeout.Infinite);
    }
}