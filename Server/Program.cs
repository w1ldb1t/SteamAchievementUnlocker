using System.Diagnostics;
using System.Net;
using Common;
using Microsoft.Extensions.Configuration;
using ServiceWire.TcpIp;

namespace Server;

internal class Program
{
    static void Main()
    {
        var logger = new Logger(Console.Out);
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("settings.json", optional: true, reloadOnChange: true)
            .Build();

        var settings = config.Get<Settings>();
        if (settings is null)
        {
            logger.Error("Settings file not found. Please create a settings.json file.");
            return;
        }

        // Configure TCP endpoint
        var endpoint = new IPEndPoint(IPAddress.Loopback, 9090);

        var achievementService = new AchievementServiceImpl(logger);

        // Start ServiceWire TCP host
        var host = new TcpHost(endpoint);
        host.AddService<IAchievementService>(achievementService);
        host.Open();

        logger.Info("Steam Achievement Automation");

        using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.steampowered.com/") };
        // an object to talk to Steam's API
        var steamService = new SteamService(httpClient);
        // a threadsafe counter for generating tickets
        var counter = new ThreadSafeCounter();

        logger.Info("Kicking off threads for the following games:");
        foreach (var appId in settings.AppId)
        {
            // Capture the loop variable so each lambda gets its own copy
            var game = appId;
            var gameName = Task.Run(async () => await steamService.GetGameNameAsync(game)).Result;
            logger.Info($"- {gameName}");
            _ = Task.Run(async () =>
            {
                var rnd = new ThreadSafeRandom();
                while (true)
                {
                    // Sleep for a random interval specific to this game
                    var minutes = rnd.Next(settings.MinMinutes, settings.MaxMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(minutes));

                    // Pick next locked achievement for this game
                    var achievements =
                        await steamService.GetAchievementListAsync(settings.ApiKey, settings.SteamId64, game);
                    achievements.RemoveAll(x => x.Unlocked == true);
                    achievements.Sort((x, y) => x.Percent.CompareTo(y.Percent));
                    achievements.Reverse();

                    if (achievements.Count == 0)
                    {
                        logger.Warning("No unlockable achievements found for this game.");
                        continue;
                    }

                    long ticket = counter.Increment();
                    var package = new Package(game, achievements[0]);
                    achievementService.AddPackage(ticket, package);

                    logger.Info($"Unlocking achievement for {gameName} ...");
                    logger.Debug($"Ticket {ticket} created for {gameName}:{package.Achievement.Id}.");

                    // Spawn an Agent to open it
                    var psi = new ProcessStartInfo(
                        $"{AppDomain.CurrentDomain.BaseDirectory}\\Client\\Client.exe",
                        $"{ticket.ToString()}")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var process = Process.Start(psi);
                    Debug.Assert(process is not null);
                    logger.Debug($"Process with PID {process.Id} started for ticket {ticket}.");
                }
            });
        }

        // Keep alive
        Thread.Sleep(Timeout.Infinite);
    }
}