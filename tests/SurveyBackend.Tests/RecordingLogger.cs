using Microsoft.Extensions.Logging;

namespace SurveyBackend.Tests;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(Exception? Exception, string Message)> Errors { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Error)
        {
            Errors.Add((exception, formatter(state, exception)));
        }
    }
}
