using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

using WorldBuilder.Shared.Lib.Settings;

namespace WorldBuilder.Lib.Settings {
    public enum AppTheme {
        Default,
        Light,
        Dark
    }

    [SettingCategory("Application", Order = 0)]
    public partial class AppSettings : ObservableObject {
        [SettingDescription("Directory where all WorldBuilder projects are stored")]
        [SettingPath(PathType.Folder, DialogTitle = "Select Projects Directory")]
        [SettingOrder(0)]
        private string _projectsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "WorldBuilder",
            "Projects"
        );
        public string ProjectsDirectory { get => _projectsDirectory; set => SetProperty(ref _projectsDirectory, value); }

        [SettingDescription("Automatically load most recent project on startup")]
        [SettingOrder(1)]
        private bool _autoLoadProject = false;
        public bool AutoLoadProject { get => _autoLoadProject; set => SetProperty(ref _autoLoadProject, value); }

        [SettingDescription("Minimum log level for application logging")]
        [SettingOrder(2)]
        private LogLevel _logLevel = LogLevel.Information;
        public LogLevel LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }

        [SettingDescription("Enable verbose logging for database queries (may impact performance)")]
        [SettingOrder(3)]
        private bool _logDatabaseQueries = false;
        public bool LogDatabaseQueries { get => _logDatabaseQueries; set => SetProperty(ref _logDatabaseQueries, value); }

        [SettingDescription("Maximum number of history items to keep")]
        [SettingRange(5, 10000, 1, 100)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(4)]
        private int _historyLimit = 50;
        public int HistoryLimit { get => _historyLimit; set => SetProperty(ref _historyLimit, value); }

        [SettingDescription("Last directory used for base DAT files when creating a project")]
        [SettingPath(PathType.Folder, DialogTitle = "Select Last Base DAT Directory")]
        [SettingOrder(5)]
        private string _lastBaseDatDirectory = string.Empty;
        public string LastBaseDatDirectory { get => _lastBaseDatDirectory; set => SetProperty(ref _lastBaseDatDirectory, value); }

        [SettingDescription("Application Theme")]
        [SettingOrder(6)]
        private AppTheme _theme = AppTheme.Default;
        public AppTheme Theme { get => _theme; set => SetProperty(ref _theme, value); }
    }
}