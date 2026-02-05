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

        [ObservableProperty] private string _name = string.Empty;

        [ObservableProperty] private Guid _guid;

        [ObservableProperty] private bool _isHosting = false;

        [ObservableProperty] private string _remoteUrl = string.Empty;

        public string ProjectDirectory => Path.GetDirectoryName(FilePath) ?? string.Empty;
        public string DatDirectory => Path.Combine(ProjectDirectory, "dats");
        public string BaseDatDirectory => Path.Combine(DatDirectory, "base");
        public string DatabasePath => Path.Combine(ProjectDirectory, "project.db");

        [JsonIgnore] public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        [JsonIgnore] public DocumentManager DocumentManager { get; set; }

        [JsonIgnore] public IDatReaderWriter DatReaderWriter { get; set; }

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
                "client_cell_1.dat", "client_portal.dat", "client_highres.dat", "client_local_English.dat"
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

            var project = new Project() { Name = projectName, FilePath = projectFilePath, Guid = Guid.NewGuid() };

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
                "client_cell_1.dat", "client_portal.dat", "client_highres.dat", "client_local_English.dat"
            };

            foreach (var datFile in datFiles) {
                var sourcePath = Path.Combine(BaseDatDirectory, datFile);
                var destPath = Path.Combine(exportDirectory, datFile);

                if (File.Exists(sourcePath)) {
                    File.Copy(sourcePath, destPath, true);
                }
            }

            using var writer = new DefaultDatReaderWriter(exportDirectory, DatAccessType.ReadWrite);

            if (portalIteration == DatReaderWriter.Dats.Portal.Iteration.CurrentIteration) {
                portalIteration = 0;
            }

            var terrainDoc = DocumentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain").Result;

            // Collect all layers that are marked for export
            var exportLayers = new List<TerrainLayer>();
            if (terrainDoc.TerrainData.RootItems != null) {
                CollectExportLayers(terrainDoc.TerrainData.RootItems, exportLayers);
            }

            exportLayers.Reverse(); // Apply Bottom-To-Top (painter's algorithm)

            // Identify all landblocks that need to be saved (modified in base OR any export layer)
            var modifiedLandblocks = new HashSet<ushort>(terrainDoc.TerrainData.Landblocks.Keys);
            var layerDocs = new Dictionary<string, LayerDocument>();

            foreach (var layer in exportLayers) {
                var layerDoc = DocumentManager.GetOrCreateDocumentAsync<LayerDocument>(layer.DocumentId).Result;
                if (layerDoc != null) {
                    layerDocs[layer.DocumentId] = layerDoc;
                    foreach (var lbKey in layerDoc.TerrainData.Landblocks.Keys) {
                        modifiedLandblocks.Add(lbKey);
                    }
                }
            }

            const int LANDBLOCK_SIZE = 81;

            // Process and save each modified landblock
            foreach (var lbKey in modifiedLandblocks) {
                var lbId = (uint)(lbKey << 16) | 0xFFFF;

                var currentEntries = terrainDoc.GetLandblockInternal(lbKey);
                if (currentEntries == null) {
                    continue;
                }

                // 2. Apply changes from each export layer in order
                foreach (var layer in exportLayers) {
                    if (layerDocs.TryGetValue(layer.DocumentId, out var layerDoc)) {
                        if (layerDoc.TerrainData.Landblocks.TryGetValue(lbKey, out var sparseCells)) {
                            foreach (var (cellIdx, cellValue) in sparseCells) {
                                currentEntries[cellIdx] = new TerrainEntry(cellValue);
                            }
                        }
                    }
                }

                // 3. Save the composite result to the DAT
                if (!writer.TryGet<LandBlock>(lbId, out var lb)) {
                    // If missing from the destination DAT, implies missing from base DAT too usually.
                    continue;
                }

                for (var i = 0; i < LANDBLOCK_SIZE; i++) {
                    var entry = currentEntries[i];
                    lb.Terrain[i] = new() {
                        Road = entry.Road,
                        Scenery = entry.Scenery,
                        Type = (DatReaderWriter.Enums.TerrainTextureType)entry.Type
                    };
                    lb.Height[i] = entry.Height;
                }

                if (!writer.TrySave(lb, portalIteration)) {
                    // Log error? Project doesn't store logger directly but could throw or ignore.
                }
            }

            // TODO: all other dat iterations
            writer.Dats.Portal.Iteration.CurrentIteration = portalIteration;

            return true;
        }

        private void CollectExportLayers(IEnumerable<TerrainLayerBase> items, List<TerrainLayer> result) {
            foreach (var item in items) {
                if (!item.IsExport) continue;

                if (item is TerrainLayerGroup group) {
                    CollectExportLayers(group.Children, result);
                }
                else if (item is TerrainLayer layer) {
                    result.Add(layer);
                }
            }
        }

        public void Dispose() {
            DatReaderWriter?.Dispose();
        }
    }
}