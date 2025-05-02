namespace Common;

public class Achievement(string id, string name, double percent, bool unlocked)
{
    public string Id { get; private set; } = id;
    public string Name { get; private set; } = name;
    public double Percent { get; private set; } = percent;
    public bool Unlocked { get; private set; } = unlocked;
}