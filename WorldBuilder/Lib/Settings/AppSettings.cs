using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace WorldBuilder.Lib.Settings {
    [SettingCategory("Application", Order = 0)]
    public partial class AppSettings : ObservableObject {
        [ObservableProperty]
        [SettingDescription("Directory where all WorldBuilder projects are stored")]
        [SettingPath(PathType.Folder, DialogTitle = "Select Projects Directory")]
        [SettingOrder(0)]
        private string _projectsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "WorldBuilder",
            "Projects"
        );

        [ObservableProperty]
        [SettingDescription("Minimum log level for application logging")]
        [SettingOrder(1)]
        private LogLevel _logLevel = LogLevel.Information;

        [ObservableProperty]
        [SettingDescription("Enable verbose logging for database queries (may impact performance)")]
        [SettingOrder(2)]
        private bool _logDatabaseQueries = false;

        [ObservableProperty]
        [SettingDescription("Maximum number of history items to keep")]
        [SettingRange(5, 10000, 1, 100)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(3)]
        private int _historyLimit = 50;
    }
}