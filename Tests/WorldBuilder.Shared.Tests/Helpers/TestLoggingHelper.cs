using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Tests.Helpers {
    public static class TestLoggingHelper {
        public static void SetupTestLogging() {
            ColorConsoleLoggerProvider.MinLogLevel = LogLevel.Warning;
        }

        public static void CleanupTestLogging() {
            ColorConsoleLoggerProvider.MinLogLevel = LogLevel.Trace;
        }
    }
}
