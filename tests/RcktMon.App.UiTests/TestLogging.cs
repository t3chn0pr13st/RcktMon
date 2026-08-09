using Microsoft.Extensions.Logging;

namespace RcktMon.App.UiTests;

/// <summary>
/// Sends the client's own log to a file next to the captured frames.
/// </summary>
/// <remarks>
/// The test runner swallows console output from background threads, and the connection
/// supervisor is entirely background work — without this, a UI test that fails because
/// the transport misbehaved gives no hint of why.
/// </remarks>
internal static class TestLogging
{
    public static ILoggerFactory Factory { get; } = LoggerFactory.Create(builder =>
        builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new FileLoggerProvider()));

    private sealed class FileLoggerProvider : ILoggerProvider
    {
        private static readonly Lock Gate = new();

        private static readonly string Path = System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("RCKTMON_UI_ARTIFACTS")
            ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rcktmon-ui"),
            "client.log");

        public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);

        public void Dispose()
        {
        }

        private sealed class FileLogger(string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} {logLevel} {category}: " +
                              $"{formatter(state, exception)} {exception}";

                lock (Gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                    File.AppendAllText(Path, line + Environment.NewLine);
                }
            }
        }
    }
}
