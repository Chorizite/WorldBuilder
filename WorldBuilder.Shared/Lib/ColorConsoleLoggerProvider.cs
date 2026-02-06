using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// A logger provider that outputs log messages to the console with colors based on log level.
    /// </summary>
    [ProviderAlias("ColorConsole")]
    public sealed class ColorConsoleLoggerProvider : ILoggerProvider {
        private ColorConsoleLoggerConfiguration _currentConfig = new();

        private readonly ConcurrentDictionary<string, ColorConsoleLogger> _loggers =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the ColorConsoleLoggerProvider class.
        /// </summary>
        public ColorConsoleLoggerProvider() {
        }

        /// <summary>
        /// Creates a new logger for the specified category.
        /// </summary>
        /// <param name="categoryName">The category name for the logger</param>
        /// <returns>A new ILogger instance</returns>
        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new ColorConsoleLogger(name, GetCurrentConfig));

        private ColorConsoleLoggerConfiguration GetCurrentConfig() => _currentConfig;

        /// <summary>
        /// Disposes the logger provider and clears all loggers.
        /// </summary>
        public void Dispose() {
            _loggers.Clear();
        }
    }

    /// <summary>
    /// Configuration for the ColorConsoleLogger.
    /// </summary>
    public sealed class ColorConsoleLoggerConfiguration {
        /// <summary>
        /// Gets or sets the event ID to filter logs by.
        /// </summary>
        public int EventId { get; set; }

        /// <summary>
        /// Gets or sets the mapping from log level to console color.
        /// </summary>
        public Dictionary<LogLevel, ConsoleColor> LogLevelToColorMap { get; set; } = new() {
            [LogLevel.Trace] = ConsoleColor.Gray,
            [LogLevel.Debug] = ConsoleColor.Gray,
            [LogLevel.Information] = ConsoleColor.Green,
            [LogLevel.Warning] = ConsoleColor.Yellow,
            [LogLevel.Error] = ConsoleColor.Red,
            [LogLevel.Critical] = ConsoleColor.DarkRed
        };
    }

    /// <summary>
    /// A logger that outputs log messages to the console with colors based on log level.
    /// </summary>
    public sealed class ColorConsoleLogger(
        string name,
        Func<ColorConsoleLoggerConfiguration> getCurrentConfig) : ILogger {
        /// <summary>
        /// Begins a logical operation scope.
        /// </summary>
        /// <typeparam name="TState">The type of the state object</typeparam>
        /// <param name="state">The state object to begin the scope with</param>
        /// <returns>An IDisposable that ends the logical operation scope when disposed</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

        /// <summary>
        /// Checks if the given log level is enabled.
        /// </summary>
        /// <param name="logLevel">The log level to check</param>
        /// <returns>True if the log level is enabled, false otherwise</returns>
        public bool IsEnabled(LogLevel logLevel) =>
            getCurrentConfig().LogLevelToColorMap.ContainsKey(logLevel);

        /// <summary>
        /// Writes a log entry.
        /// </summary>
        /// <typeparam name="TState">The type of the state object</typeparam>
        /// <param name="logLevel">The log level</param>
        /// <param name="eventId">The event ID</param>
        /// <param name="state">The state object</param>
        /// <param name="exception">The exception (if any)</param>
        /// <param name="formatter">The formatter function</param>
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

        /// <summary>
        /// Prints a formatted message to the console.
        /// </summary>
        /// <param name="message">The message format string</param>
        /// <param name="arguments">The arguments to format the message with</param>
        public void PrintMessage(string message, params object[]? arguments) {
            Console.WriteLine(message, arguments);
        }
    }
}