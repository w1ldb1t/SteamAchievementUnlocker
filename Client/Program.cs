namespace Client;

using System.Diagnostics;
using System.Runtime.InteropServices;
using ServiceWire.TcpIp;
using System.Net;
using Common;

#if LINUX
internal static partial class Libc
{
    [DllImport("libc", EntryPoint = "dup", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Dup(int fd);

    [DllImport("libc", EntryPoint = "dup2", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Dup2(int oldfd, int newfd);

    [DllImport("libc", EntryPoint = "close", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "open", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags);
}
#endif

internal static partial class SteamApi
{
#if LINUX
    const string Lib = "libsteam_api.so";
#else
    const string Lib = "steam_api64";
#endif

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_Init();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SteamAPI_Shutdown();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SteamAPI_SteamUserStats_v012();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SteamAPI_GetHSteamPipe();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SteamAPI_ManualDispatch_Init();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SteamAPI_ManualDispatch_RunFrame(int hSteamPipe);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ManualDispatch_GetNextCallback(int hSteamPipe, IntPtr pCallbackMsg);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SteamAPI_ManualDispatch_FreeLastCallback(int hSteamPipe);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUserStats_RequestCurrentStats(IntPtr instance);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUserStats_SetAchievement(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUserStats_StoreStats(IntPtr instance);
}

[StructLayout(LayoutKind.Sequential)]
internal struct CallbackMsg_t
{
    public int m_hSteamUser;
    public int m_iCallback;
    public IntPtr m_pubParam;
    public int m_cubParam;
}

[StructLayout(LayoutKind.Explicit, Pack = 8)]
internal struct UserStatsReceived_t
{
    public const int k_iCallback = 1101;

    [FieldOffset(0)] public ulong m_nGameID;
    [FieldOffset(8)] public int m_eResult;
    [FieldOffset(12)] public ulong m_steamIDUser;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct UserStatsStored_t
{
    public const int k_iCallback = 1102;

    public ulong m_nGameID;
    public int m_eResult;
}

internal class Program
{
    const int k_EResultOK = 1;
    const int TimeoutMs = 10_000;

    static void Main(string[] args)
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

        var package = host.GetPackage(ticket).Result;
        if (package is null)
        {
            host.LogMessage($"[{pid}] Package not found.", MessageType.Debug);
            return;
        }

        // Set AppID
        Environment.SetEnvironmentVariable("SteamAppId", package.AppId);

#if LINUX
	// Unlike Windows, on Linux the appid file must exist
        var appIdFile = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
        File.WriteAllText(appIdFile, package.AppId);

        // Suppress all native Steam library output
        int devNull = Libc.Open("/dev/null", 1); // O_WRONLY
        Libc.Dup2(devNull, 1);
        Libc.Dup2(devNull, 2);
        Libc.Close(devNull);
#endif

        bool initOk = SteamApi.SteamAPI_Init();

        if (!initOk)
        {
#if LINUX
            TryDelete(appIdFile);
#endif
            host.LogMessage($"(client = {pid}) SteamAPI_Init failed.", MessageType.Error);
            return;
        }

        // Switch to manual dispatch
        SteamApi.SteamAPI_ManualDispatch_Init();
        int pipe = SteamApi.SteamAPI_GetHSteamPipe();

        try
        {
            var userStats = SteamApi.SteamAPI_SteamUserStats_v012();
            if (userStats == IntPtr.Zero)
            {
                host.LogMessage($"(client = {pid}) Failed to get ISteamUserStats.", MessageType.Error);
                return;
            }

            // Request stats and wait for UserStatsReceived_t callback
            SteamApi.SteamAPI_ISteamUserStats_RequestCurrentStats(userStats);

            if (!WaitForCallback<UserStatsReceived_t>(pipe, UserStatsReceived_t.k_iCallback, out var received))
            {
                host.LogMessage($"(client = {pid}) Timed out waiting for stats to load.", MessageType.Error);
                return;
            }

            if (received.m_eResult != k_EResultOK)
            {
                host.LogMessage($"(client = {pid}) RequestCurrentStats failed with EResult {received.m_eResult}.", MessageType.Error);
                return;
            }

            // Set the achievement
            if (!SteamApi.SteamAPI_ISteamUserStats_SetAchievement(userStats, package.Achievement.Id))
            {
                host.LogMessage($"(client = {pid}) SetAchievement failed for '{package.Achievement.Id}'.", MessageType.Error);
                return;
            }

            // Commit and wait for UserStatsStored_t callback
            if (!SteamApi.SteamAPI_ISteamUserStats_StoreStats(userStats))
            {
                host.LogMessage($"(client = {pid}) StoreStats failed.", MessageType.Error);
                return;
            }

            if (!WaitForCallback<UserStatsStored_t>(pipe, UserStatsStored_t.k_iCallback, out var stored))
            {
                host.LogMessage($"(client = {pid}) Timed out waiting for StoreStats confirmation.", MessageType.Error);
                return;
            }

            if (stored.m_eResult != k_EResultOK)
            {
                host.LogMessage($"(client = {pid}) StoreStats completed with EResult {stored.m_eResult}.", MessageType.Error);
                return;
            }

            host.LogMessage($"Successfully unlocked achievement {package.Achievement.Name}!");
        }
        finally
        {
            SteamApi.SteamAPI_Shutdown();
#if LINUX
            TryDelete(appIdFile);
#endif
        }
    }

    /// <summary>
    /// Pumps the ManualDispatch loop until a callback with the given ID arrives or timeout elapses.
    /// Other callbacks are consumed and freed normally.
    /// </summary>
    static bool WaitForCallback<T>(int pipe, int targetCallbackId, out T result) where T : struct
    {
        result = default;
        var sw = Stopwatch.StartNew();
        IntPtr pMsg = Marshal.AllocHGlobal(Marshal.SizeOf<CallbackMsg_t>());

        try
        {
            while (sw.ElapsedMilliseconds < TimeoutMs)
            {
                SteamApi.SteamAPI_ManualDispatch_RunFrame(pipe);

                while (SteamApi.SteamAPI_ManualDispatch_GetNextCallback(pipe, pMsg))
                {
                    var msg = Marshal.PtrToStructure<CallbackMsg_t>(pMsg);
                    try
                    {
                        if (msg.m_iCallback == targetCallbackId)
                        {
                            result = Marshal.PtrToStructure<T>(msg.m_pubParam);
                            return true;
                        }
                    }
                    finally
                    {
                        SteamApi.SteamAPI_ManualDispatch_FreeLastCallback(pipe);
                    }
                }

                Thread.Sleep(10);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pMsg);
        }

        return false;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
