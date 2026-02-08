using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace NetworkEngine.Tests.Node.TestHelper;


// XunitLoggerProvider 구현
public class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new XunitLogger(output, categoryName);
    public void Dispose() { }
}

public class XunitLogger(ITestOutputHelper output, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        output.WriteLine($"[{logLevel}] {categoryName}: {formatter(state, exception)}");
        if (exception != null)
        {
            output.WriteLine(exception.ToString());
        }
    }
}