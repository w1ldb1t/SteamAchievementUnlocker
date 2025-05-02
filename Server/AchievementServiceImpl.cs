using System.Collections.Concurrent;
using System.Diagnostics;
using Common;

namespace Server;

public class AchievementServiceImpl(Logger logger) : IAchievementService
{
    private readonly ConcurrentDictionary<long, Package> _store = new();

    public Task<Package?> GetPackage(long ticket)
    {
        if (!_store.TryRemove(ticket, out var package)) return Task.FromResult<Package?>(null);
        return Task.FromResult(package)!;
    }

    public void LogMessage(string line, MessageType type)
    {
        switch (type)
        {
            case MessageType.Info:
                logger.Info(line);
                break;
            case MessageType.Error:
                logger.Error(line);
                break;
            case MessageType.Warning:
                logger.Warning(line);
                break;
            case MessageType.Debug:
                logger.Debug(line);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    public void AddPackage(long ticket, Package package)
    {
        bool added = _store.TryAdd(ticket, package);
        Debug.Assert(added);
    }
}