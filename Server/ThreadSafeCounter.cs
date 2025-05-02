namespace Server;

using System.Threading;

public class ThreadSafeCounter
{
    private long _count = 0;

    /// <summary>
    /// Atomically increments the counter and returns the new value.
    /// </summary>
    public long Increment() => Interlocked.Increment(ref _count);

    /// <summary>
    /// Atomically decrements the counter and returns the new value.
    /// </summary>
    public long Decrement() => Interlocked.Decrement(ref _count);

    /// <summary>
    /// Atomically reads the current value.
    /// </summary>
    public long Value => Interlocked.Read(ref _count);
}