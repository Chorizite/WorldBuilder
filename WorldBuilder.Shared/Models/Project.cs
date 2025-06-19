using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;

namespace WorldBuilder.Shared.Models {
    public partial class Project : ObservableObject, IDisposable {
        private static JsonSerializerOptions _opts = new JsonSerializerOptions { WriteIndented = true };
        private string _filePath;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private Guid _guid;

        [JsonIgnore]
        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        [JsonIgnore]
        public DocumentManager DocumentManager { get; private set; }

        public static Project? FromDisk(string projectFilePath) {
            if (!File.Exists(projectFilePath)) {
                return null;
            }

            var project = JsonSerializer.Deserialize<Project>(File.ReadAllText(projectFilePath), _opts);
            if (project != null) {
                project.FilePath = projectFilePath;
                project.DocumentManager = new DocumentManager(project);
            }
            return project;
        }

        public static Project Create(string projectName, string projectFilePath) {
            var projectDir = Path.GetDirectoryName(projectFilePath);
            if (!Directory.Exists(projectDir)) {
                Directory.CreateDirectory(projectDir);
            }

            var project = new Project() {
                Name = projectName,
                FilePath = projectFilePath,
                Guid = Guid.NewGuid()
            };
            project.DocumentManager = new DocumentManager(project);
            project.Save();
            return project;
        }

        public Project() {
        
        }

        public void Save() {
            var tmp = Path.GetTempFileName();
            try {
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, _opts));
                File.Move(tmp, FilePath);
            }
            finally {
                File.Delete(tmp);
            }
        }

        public void Dispose() {
            DocumentManager?.Dispose();
        }
    }
}
