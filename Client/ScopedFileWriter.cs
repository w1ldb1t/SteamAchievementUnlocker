namespace Client;

using System;
using System.IO;
using System.Threading;

/// <summary>
/// Acquires a global named mutex on construction and holds it until disposed,
/// so you can perform multiple writes/appends under one lock.
/// </summary>
public sealed class ScopedFileWriter : IDisposable
{
    private const string MutexName = @"Global\92307492869274235";
    private readonly Mutex _mutex;
    private readonly string _path;
    private bool _disposed;

    /// <summary>
    /// Acquires the named mutex for the given file path.
    /// Blocks indefinitely (or until <paramref name="timeout"/> elapses) before throwing.
    /// </summary>
    /// <param name="path">The file to lock and write to.</param>
    /// <param name="timeout">
    /// How long to wait for the lock. If null, waits forever.
    /// </param>
    public ScopedFileWriter(string path, TimeSpan? timeout = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _mutex = new Mutex(false, MutexName);

        bool locked = timeout.HasValue
            ? _mutex.WaitOne(timeout.Value)
            : _mutex.WaitOne();

        if (locked) return;
        _mutex.Dispose();
        throw new TimeoutException($"Timed out acquiring lock for '{path}'");
    }

    /// <summary>
    /// Overwrite the file completely with <paramref name="content"/>.
    /// </summary>
    public void Write(string content)
    {
        ThrowIfDisposed();
        File.WriteAllText(_path, content);
    }

    /// <summary>
    /// Append <paramref name="content"/> to the end of the file.
    /// </summary>
    public void Append(string content)
    {
        ThrowIfDisposed();
        File.AppendAllText(_path, content);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ScopedFileWriter));
    }

    /// <summary>
    /// Releases the mutex so another process can enter.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _disposed = true;
    }
}