using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Lib
{

    [ProviderAlias("ColorConsole")]
    public sealed class ColorConsoleLoggerProvider : ILoggerProvider
    {
        private readonly IDisposable? _onChangeToken;
        private ColorConsoleLoggerConfiguration _currentConfig = new();
        private readonly ConcurrentDictionary<string, ColorConsoleLogger> _loggers =
            new(StringComparer.OrdinalIgnoreCase);

        public ColorConsoleLoggerProvider()
        {
        }

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new ColorConsoleLogger(name, GetCurrentConfig));

        private ColorConsoleLoggerConfiguration GetCurrentConfig() => _currentConfig;

        public void Dispose()
        {
            _loggers.Clear();
            _onChangeToken?.Dispose();
        }
    }

    public sealed class ColorConsoleLoggerConfiguration
    {
        public int EventId { get; set; }

        public Dictionary<LogLevel, ConsoleColor> LogLevelToColorMap { get; set; } = new()
        {
            [LogLevel.Information] = ConsoleColor.Green
        };
    }
    public sealed class ColorConsoleLogger(
        string name,
        Func<ColorConsoleLoggerConfiguration> getCurrentConfig) : ILogger, NetSparkleUpdater.Interfaces.ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

        public bool IsEnabled(LogLevel logLevel) =>
            getCurrentConfig().LogLevelToColorMap.ContainsKey(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) {
            ColorConsoleLoggerConfiguration config = getCurrentConfig();
            if (config.EventId == 0 || config.EventId == eventId.Id) {
                // Get the formatted message from the formatter
                string message = formatter(state, exception);

                // Include exception details if present
                string exceptionMessage = exception != null ? $"\nException: {exception}" : string.Empty;

                // Set console color based on log level
                if (config.LogLevelToColorMap.TryGetValue(logLevel, out var color)) {
                    Console.ForegroundColor = color;
                }

                Console.WriteLine($"[{eventId.Id,2}: {logLevel,-12}] {name} - {message}{exceptionMessage}");

                // Reset console color
                Console.ResetColor();
            }
        }

        public void PrintMessage(string message, params object[]? arguments) {
            Console.WriteLine(message, arguments);
        }
    }
}
