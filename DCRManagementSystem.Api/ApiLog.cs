using Microsoft.Extensions.Logging;

namespace DCRManagementSystem.Api;

public static class ApiLog
{
    public static string LogDirectory
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DCRManagementSystem",
                "Api",
                "Logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string ApiLogFile => Path.Combine(LogDirectory, $"dcr-api-{DateTime.Now:yyyyMMdd}.log");
    public static string TunnelLogFile => Path.Combine(LogDirectory, $"cloudflared-{DateTime.Now:yyyyMMdd}.log");

    public static void WriteEmergency(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(
                ApiLogFile,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [CRITICAL] {message}{Environment.NewLine}");
        }
        catch
        {
            // A startup error must not be hidden by a secondary logging failure.
        }
    }
}

public sealed class ApiFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private StreamWriter? _writer;

    public ApiFileLoggerProvider()
    {
        try
        {
            Directory.CreateDirectory(ApiLog.LogDirectory);
            _writer = new StreamWriter(
                new FileStream(ApiLog.ApiLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                System.Text.Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
        catch
        {
            _writer = null;
        }
    }

    public ILogger CreateLogger(string categoryName) => new ApiFileLogger(this, categoryName);

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var writer = _writer;
        if (writer is null) return;
        try
        {
            lock (_sync)
            {
                writer.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {category}: {message}");
                if (exception is not null)
                    writer.Write($" | {exception}");
                writer.WriteLine();
            }
        }
        catch
        {
            // File logging is diagnostic only and must never stop the API.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class ApiFileLogger : ILogger
    {
        private readonly ApiFileLoggerProvider _provider;
        private readonly string _category;

        public ApiFileLogger(ApiFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }
}
