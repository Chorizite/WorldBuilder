﻿using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace WorldBuilder.Lib.Settings {
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

        [SettingDescription("Minimum log level for application logging")]
        [SettingOrder(1)]
        private LogLevel _logLevel = LogLevel.Information;
        public LogLevel LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }

        [SettingDescription("Enable verbose logging for database queries (may impact performance)")]
        [SettingOrder(2)]
        private bool _logDatabaseQueries = false;
        public bool LogDatabaseQueries { get => _logDatabaseQueries; set => SetProperty(ref _logDatabaseQueries, value); }

        [SettingDescription("Maximum number of history items to keep")]
        [SettingRange(5, 10000, 1, 100)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(3)]
        private int _historyLimit = 50;
        public int HistoryLimit { get => _historyLimit; set => SetProperty(ref _historyLimit, value); }
    }
}