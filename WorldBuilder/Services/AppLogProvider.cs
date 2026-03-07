using System;
using Microsoft.Extensions.Logging;

namespace WorldBuilder.Services;

// provider that creates our custom logger
public class AppLogProvider : ILoggerProvider {
    private readonly AppLogService _appLogService;

    public AppLogProvider(AppLogService appLogService) {
        _appLogService = appLogService;
    }

    public ILogger CreateLogger(string categoryName) {
        return new AppLogger(_appLogService, categoryName);
    }

    public void Dispose() { }
}

// actual logger that catches messages
public class AppLogger : ILogger {
    private readonly AppLogService _appLogService;
    private readonly string _categoryName;

    public AppLogger(AppLogService appLogService, string categoryName) {
        _appLogService = appLogService;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true; // Catch everything

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var fullMessage = $"[{_categoryName}] {message}";

        if (exception != null) {
            fullMessage += $"\nException: {exception.Message}\n{exception.StackTrace}";
        }

        if (logLevel == LogLevel.Error || logLevel == LogLevel.Critical || exception != null) {
            _appLogService.LogError(fullMessage);
        } else {
            _appLogService.LogInfo(fullMessage);
        }
    }
}