namespace Client;

using System.Diagnostics;
using ServiceWire.TcpIp;
using System.Net;
using System.Threading.Tasks;
using Steamworks;
using Common;

internal class Program
{
    static async Task Main(string[] args)
    {
        Debug.Assert(args.Length == 1, "Ticket ID must be provided as first argument");
        if (!long.TryParse(args[0], out long ticket))
        {
            Debug.Assert(false, "Ticket ID must be a valid number");
            return;
        }

        int pid = Process.GetCurrentProcess().Id;

        // Create IPC connection with host
        var endpoint = new IPEndPoint(IPAddress.Loopback, 9090);
        using var client = new TcpClient<IAchievementService>(endpoint);
        Debug.Assert(client.IsConnected);

        IAchievementService host = client.Proxy;
        Debug.Assert(host is not null);
        host.LogMessage($"Client with PID {pid} connected to server.", MessageType.Debug);

        var package = await host.GetPackage(ticket);
        if (package is null)
        {
            // This should never happen, but just in case
            host.LogMessage($"[{pid}] Package not found.", MessageType.Debug);
            return;
        }

        // Write the app ID to the steam_appid.txt file
        using var writer = new ScopedFileWriter($"{AppDomain.CurrentDomain.BaseDirectory}\\steam_appid.txt");
        writer.Write(package.AppId);

        // Check if Steam is running
        if (!SteamAPI.IsSteamRunning())
        {
            host.LogMessage($"(client = {pid}) Steam is not running.", MessageType.Error);
            return;
        }

        // Initialize Steam API
        if (!SteamAPI.Init())
        {
            host.LogMessage($"(client = {pid}) Steam API initialization failure.", MessageType.Error);
            return;
        }

        // Request current stats
        if (!SteamUserStats.RequestCurrentStats())
        {
            host.LogMessage($"(client = {pid}) Steam user stats request failure.", MessageType.Error);
            return;
        }

        // Wait for UserStatsReceived_t callback
        bool statsReceived = false;
        Callback<UserStatsReceived_t>.Create(_ => statsReceived = true);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!statsReceived && DateTime.UtcNow < deadline)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(50);
        }

        if (!statsReceived)
        {
            host.LogMessage($"(client = {pid}) Timed out waiting for stats from Steam.", MessageType.Error);
            SteamAPI.Shutdown();
            return;
        }

        // Unlock the achievement
        if (!SteamUserStats.SetAchievement(package.Achievement.Id))
        {
            host.LogMessage($"(client = {pid}) Failed to unlock achievement.", MessageType.Error);
            return;
        }
        
        // Store the stats
        if (!SteamUserStats.StoreStats())
        {
            host.LogMessage($"(client = {pid}) Failed to commit changes to Steam.", MessageType.Error);
            return;
        }

        host.LogMessage($"Successfully unlocked achievement {package.Achievement.Name}!");

        // Shutdown Steam API
        SteamAPI.Shutdown();
    }
}