using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldBuilder.Lib.Extensions {
    public class WorldBuilderSettings : ObservableObject {
        private readonly ILogger<WorldBuilderSettings> _log;

        [JsonIgnore]
        public string AppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorldBuilder");

        [JsonIgnore]
        public string ProjectsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "WorldBuilder", "Projects");

        public WorldBuilderSettings(ILogger<WorldBuilderSettings> log) {
            _log = log;

            if (!Directory.Exists(AppDataDirectory)) {
                Directory.CreateDirectory(AppDataDirectory);
            }
            if(!Directory.Exists(ProjectsDirectory)) {
                Directory.CreateDirectory(ProjectsDirectory);
            }
        }

        public void Save() {
            var tmpFile = Path.GetTempFileName();
            try {
                var json = JsonSerializer.Serialize(this);
                File.WriteAllText(tmpFile, json);
                File.Move(tmpFile, Path.Combine(AppDataDirectory, "settings.json"));
            }
            catch(Exception ex) {
                _log.LogError(ex, "Failed to save settings");
            }
            finally {
                File.Delete(tmpFile);
            }
        }
    }
}