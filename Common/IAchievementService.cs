namespace Common;

using System.Threading.Tasks;

public enum MessageType { Info, Warning, Debug, Error };

public interface IAchievementService
{
    Task<Package?> GetPackage(long ticket);
    void LogMessage(string line, MessageType type = MessageType.Info);
}