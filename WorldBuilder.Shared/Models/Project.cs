using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Options;
using Microsoft.Data.Sqlite;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Models {
    public partial class Project : ObservableObject, IDisposable {
        private static JsonSerializerOptions _opts = new JsonSerializerOptions { WriteIndented = true };
        private string _filePath = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private Guid _guid;

        [ObservableProperty]
        private bool _isHosting = false;

        [ObservableProperty]
        private string _remoteUrl = string.Empty;

        public string ProjectDirectory => Path.GetDirectoryName(FilePath) ?? string.Empty;
        public string DatDirectory => Path.Combine(ProjectDirectory, "dats");
        public string BaseDatDirectory => Path.Combine(DatDirectory, "base");
        public string DatabasePath => Path.Combine(ProjectDirectory, "project.db");

        [JsonIgnore]
        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        [JsonIgnore]
        public DocumentManager DocumentManager { get; set; }

        [JsonIgnore]
        public IDatReaderWriter DatReaderWriter { get; set; }

        public static Project? FromDisk(string projectFilePath) {
            if (!File.Exists(projectFilePath)) {
                return null;
            }

            var project = JsonSerializer.Deserialize<Project>(File.ReadAllText(projectFilePath), _opts);
            if (project != null) {
                project.FilePath = projectFilePath;
                project.InitializeDatReaderWriter();
            }
            return project;
        }

        public static Project? Create(string projectName, string projectFilePath, string baseDatDirectory) {
            var projectDir = Path.GetDirectoryName(projectFilePath);
            if (!Directory.Exists(projectDir)) {
                Directory.CreateDirectory(projectDir);
            }

            var datDir = Path.Combine(projectDir, "dats");
            var baseDatDir = Path.Combine(datDir, "base");

            if (!Directory.Exists(baseDatDir)) {
                Directory.CreateDirectory(baseDatDir);
            }

            // Copy base dat files
            var datFiles = new[] {
                "client_cell_1.dat",
                "client_portal.dat",
                "client_highres.dat",
                "client_local_English.dat"
            };


            if (Directory.Exists(baseDatDirectory)) {
                foreach (var datFile in datFiles) {
                    var sourcePath = Path.Combine(baseDatDirectory, datFile);
                    var destPath = Path.Combine(baseDatDir, datFile);

                    if (File.Exists(sourcePath)) {
                        File.Copy(sourcePath, destPath, true);
                    }
                }
            }

            var project = new Project() {
                Name = projectName,
                FilePath = projectFilePath,
                Guid = Guid.NewGuid()
            };

            project.InitializeDatReaderWriter();
            project.Save();
            return project;
        }

        public Project() {

        }

        private void InitializeDatReaderWriter() {
            if (Directory.Exists(BaseDatDirectory)) {
                DatReaderWriter = new DefaultDatReaderWriter(BaseDatDirectory, DatAccessType.Read);
            }
            else {
                throw new DirectoryNotFoundException($"Base dat directory not found: {BaseDatDirectory}");
            }
        }

        public void Save() {
            var tmp = Path.GetTempFileName();
            try {
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, _opts));
                File.Move(tmp, FilePath);
            }
            finally {
                if (File.Exists(tmp)) {
                    File.Delete(tmp);
                }
            }
        }

        public bool ExportDats(string exportDirectory, int portalIteration) {
            if (!Directory.Exists(exportDirectory)) {
                Directory.CreateDirectory(exportDirectory);
            }

            // Copy base dats from project's base directory
            var datFiles = new[] {
                "client_cell_1.dat",
                "client_portal.dat",
                "client_highres.dat",
                "client_local_English.dat"
            };

            foreach (var datFile in datFiles) {
                var sourcePath = Path.Combine(BaseDatDirectory, datFile);
                var destPath = Path.Combine(exportDirectory, datFile);

                if (File.Exists(sourcePath)) {
                    File.Copy(sourcePath, destPath, true);
                }
            }

            using var writer = new DefaultDatReaderWriter(exportDirectory, DatAccessType.ReadWrite);

            var doc = DocumentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain").Result;
            doc.SaveToDats(writer);
            /*
            writer.Dats.Portal.Iteration.CurrentIteration = portalIteration;

            writer.Dats.Cell.TryWriteFile(writer.Dats.Cell.Iteration);
            writer.Dats.Portal.TryWriteFile(writer.Dats.Portal.Iteration);
            writer.Dats.Local.TryWriteFile(writer.Dats.Local.Iteration);
            writer.Dats.HighRes.TryWriteFile(writer.Dats.HighRes.Iteration);
            */
            return true;
        }

        public void Dispose() {
            DatReaderWriter?.Dispose();
        }
    }
}