using System;
using System.IO;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using Microsoft.Extensions.Logging;

namespace WorldBuilder.Shared.Tests.Helpers {
    public static class TestSettingsHelper {
        public static string SetupTestSettings() {
            var tempDir = Path.Combine(Path.GetTempPath(), "WorldBuilderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            WorldBuilderSettings.OverrideAppDataDirectory = tempDir;
            ColorConsoleLoggerProvider.MinLogLevel = LogLevel.Warning;
            return tempDir;
        }

        public static void CleanupTestSettings(string tempDir) {
            WorldBuilderSettings.OverrideAppDataDirectory = null;
            ColorConsoleLoggerProvider.MinLogLevel = LogLevel.Trace;
            if (Directory.Exists(tempDir)) {
                try {
                    Directory.Delete(tempDir, true);
                }
                catch {
                    // Ignore cleanup errors in tests
                }
            }
        }
    }
}
