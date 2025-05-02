namespace Common;

public class Package(string appId, Achievement achievement)
{
    public string AppId { get; private set; } = appId;
    public Achievement Achievement { get; private set; } = achievement;
}