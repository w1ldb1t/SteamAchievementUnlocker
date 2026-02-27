using System.Text;

namespace Server;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed class Logger(
    TextWriter inner,
    string dateFormat = "yyyy-MM-dd HH:mm:ss",
    bool useConsoleColors = true)
    : TextWriter
{
    private readonly TextWriter _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly bool _useConsoleColors = useConsoleColors && (inner == Console.Out || inner == Console.Error);

    /// <summary>
    /// The format used to render the timestamp.
    /// </summary>
    public string DateFormat { get; init; } = dateFormat ?? throw new ArgumentNullException(nameof(dateFormat));

    public override Encoding Encoding => _inner.Encoding;

    // Default WriteLine → Info
    public override void WriteLine(string? value) =>
        WriteLine(LogLevel.Info, value!);

    // Level-specific methods
    public void Debug(string message) => WriteLine(LogLevel.Debug, message);
    public void Info(string message) => WriteLine(LogLevel.Info, message);
    public void Warning(string message) => WriteLine(LogLevel.Warning, message);
    public void Error(string message) => WriteLine(LogLevel.Error, message);

    private void WriteLine(LogLevel level, string message)
    {
        // Skip debug in non-debug builds
#if !DEBUG
        if (level == LogLevel.Debug)
            return;
#endif
        var formatted = Format(level, message);
        if (_useConsoleColors)
            WithColor(level, () => _inner.WriteLine(formatted));
        else
            _inner.WriteLine(formatted);
    }

    private string Format(LogLevel level, string message) =>
        $"[{DateTimeOffset.Now.ToString(DateFormat)}] [{level}] {message}";

    private void WithColor(LogLevel level, Action write)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = level switch
        {
            LogLevel.Debug => ConsoleColor.Cyan,
            LogLevel.Info => ConsoleColor.Green,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => prev
        };
        write();
        Console.ForegroundColor = prev;
    }
}