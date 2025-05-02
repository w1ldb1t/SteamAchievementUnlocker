namespace Server;

public class ThreadSafeRandom
{
    private static int seed = Environment.TickCount;

    private static ThreadLocal<Random> threadLocal =
        new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref seed)));

    public int Next(int minValue, int maxValue) => threadLocal.Value!.Next(minValue, maxValue);
}