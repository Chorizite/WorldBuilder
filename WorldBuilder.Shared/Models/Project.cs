using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Options;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;

namespace WorldBuilder.Shared.Models {
    public partial class Project : ObservableObject, IDisposable {
        private static JsonSerializerOptions _opts = new JsonSerializerOptions { WriteIndented = true };
        private string _filePath = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private Guid _guid;

        public string BaseDatDirectory => Path.Combine(Path.GetDirectoryName(FilePath), "dats", "base");

        [JsonIgnore]
        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        [JsonIgnore]
        public DocumentManager DocumentManager { get; private set; }

        [JsonIgnore]
        public DatCollection Dats { get; private set; }

        public static Project? FromDisk(string projectFilePath) {
            if (!File.Exists(projectFilePath)) {
                return null;
            }

            var project = JsonSerializer.Deserialize<Project>(File.ReadAllText(projectFilePath), _opts);
            if (project != null) {
                project.FilePath = projectFilePath;
                project.DocumentManager = new DocumentManager(project);
                project.Dats = new DatCollection(new DatCollectionOptions() {
                    AccessType = DatAccessType.Read,
                    DatDirectory = project.BaseDatDirectory
                });
            }
            return project;
        }

        public static Project? Create(string projectName, string projectFilePath, string baseDatDirectory) {
            var projectDir = Path.GetDirectoryName(projectFilePath);
            if (!Directory.Exists(projectDir)) {
                Directory.CreateDirectory(projectDir);
            }

            if (!Directory.Exists(baseDatDirectory)) {
                return null;
            }

            if (!Directory.Exists(Path.Combine(projectDir, "dats", "base"))) {
                Directory.CreateDirectory(Path.Combine(projectDir, "dats", "base"));
            }

            File.Copy(Path.Combine(baseDatDirectory, "client_cell_1.dat"), Path.Combine(projectDir, "dats", "base", "client_cell_1.dat"));
            File.Copy(Path.Combine(baseDatDirectory, "client_portal.dat"), Path.Combine(projectDir, "dats", "base", "client_portal.dat"));
            File.Copy(Path.Combine(baseDatDirectory, "client_highres.dat"), Path.Combine(projectDir, "dats", "base", "client_highres.dat"));
            File.Copy(Path.Combine(baseDatDirectory, "client_local_English.dat"), Path.Combine(projectDir, "dats", "base", "client_local_English.dat"));

            var project = new Project() {
                Name = projectName,
                FilePath = projectFilePath,
                Guid = Guid.NewGuid()
            };
            project.DocumentManager = new DocumentManager(project);
            project.Dats = new DatCollection(new DatCollectionOptions() {
                AccessType = DatAccessType.Read,
                DatDirectory = project.BaseDatDirectory
            });
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

        public bool ExportDats(string exportDirectory, int cellIteration, int portalIteration, int languageIteration, int highResIteration) {
            if (!Directory.Exists(exportDirectory)) {
                Directory.CreateDirectory(exportDirectory);
            }

            // first copy base dats
            File.Copy(Path.Combine(BaseDatDirectory, "client_cell_1.dat"), Path.Combine(exportDirectory, "client_cell_1.dat"));
            File.Copy(Path.Combine(BaseDatDirectory, "client_portal.dat"), Path.Combine(exportDirectory, "client_portal.dat"));
            File.Copy(Path.Combine(BaseDatDirectory, "client_highres.dat"), Path.Combine(exportDirectory, "client_highres.dat"));
            File.Copy(Path.Combine(BaseDatDirectory, "client_local_English.dat"), Path.Combine(exportDirectory, "client_local_English.dat"));


            using var writer = new DatCollection(new() {
                AccessType = DatAccessType.ReadWrite,
                DatDirectory = exportDirectory
            });

            writer.Cell.Iteration.CurrentIteration = cellIteration;
            writer.Portal.Iteration.CurrentIteration = portalIteration;
            writer.Local.Iteration.CurrentIteration = languageIteration;
            writer.HighRes.Iteration.CurrentIteration = highResIteration;

            writer.Cell.TryWriteFile(writer.Cell.Iteration);
            writer.Portal.TryWriteFile(writer.Portal.Iteration);
            writer.Local.TryWriteFile(writer.Local.Iteration);
            writer.HighRes.TryWriteFile(writer.HighRes.Iteration);


            return true;
        }

        public void Dispose() {
            Dats?.Dispose();
            DocumentManager?.Dispose();
        }
    }
}
