using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Implementation of IDatRepositoryService for managing centralized DAT sets.
    /// </summary>
    public class DatRepositoryService : IDatRepositoryService {
        private readonly ILogger<DatRepositoryService> _log;
        private string _repositoryRoot = string.Empty;
        private List<ManagedDatSet> _managedSets = [];
        private readonly string _registryFileName = "managed_dats.json";

        public string RepositoryRoot => _repositoryRoot;

        public DatRepositoryService(ILogger<DatRepositoryService> log) {
            _log = log;
        }

        public void SetRepositoryRoot(string rootDirectory) {
            _repositoryRoot = rootDirectory;
            if (!Directory.Exists(_repositoryRoot)) {
                Directory.CreateDirectory(_repositoryRoot);
            }
            LoadRegistry();
        }

        private void LoadRegistry() {
            var registryPath = Path.Combine(_repositoryRoot, _registryFileName);
            if (File.Exists(registryPath)) {
                try {
                    var json = File.ReadAllText(registryPath);
                    _managedSets = JsonSerializer.Deserialize(json, DatSourceGenerationContext.Default.ListManagedDatSet) ?? [];

                    foreach (var set in _managedSets) {
                        if (string.IsNullOrEmpty(set.FriendlyName) || IsGeneratedName(set, set.FriendlyName)) {
                            var defaultName = GetDefaultFriendlyName(set);
                            if (set.FriendlyName != defaultName) {
                                set.FriendlyName = defaultName;
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    _log.LogError(ex, "Failed to load managed DAT registry");
                    _managedSets = [];
                }
            }
        }

        private bool IsGeneratedName(ManagedDatSet set, string friendlyName) {
            if (string.IsNullOrEmpty(set.CombinedMd5) || set.CombinedMd5.Length < 8) return false;
            return friendlyName == $"Iteration P:{set.PortalIteration} C:{set.CellIteration} ({set.CombinedMd5[..8]})";
        }

        private string GetDefaultFriendlyName(ManagedDatSet set) {
            if (set.PortalIteration == 2072 && set.CellIteration == 982 && !string.IsNullOrEmpty(set.CombinedMd5) && set.CombinedMd5.StartsWith("C328EAFE", StringComparison.OrdinalIgnoreCase)) {
                return "EndOfRetail";
            }

            var hashPrefix = !string.IsNullOrEmpty(set.CombinedMd5) && set.CombinedMd5.Length >= 8 ? $" ({set.CombinedMd5[..8]})" : string.Empty;
            return $"Iteration P:{set.PortalIteration} C:{set.CellIteration}{hashPrefix}";
        }

        private void SaveRegistry() {
            var registryPath = Path.Combine(_repositoryRoot, _registryFileName);
            try {
                var json = JsonSerializer.Serialize(_managedSets, DatSourceGenerationContext.Default.ListManagedDatSet);
                File.WriteAllText(registryPath, json);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to save managed DAT registry");
            }
        }

        public IReadOnlyList<ManagedDatSet> GetManagedDataSets() => _managedSets.AsReadOnly();

        public ManagedDatSet? GetManagedDataSet(Guid id) => _managedSets.FirstOrDefault(s => s.Id == id);

        public async Task<Result<(Guid id, ManagedDatSet metadata)>> CalculateDeterministicIdAsync(string directory, CancellationToken ct) {
            try {
                var requiredFiles = new List<string> { "client_portal.dat", "client_highres.dat", "client_local_English.dat" };
                
                // Find all cell dats
                var cellFiles = Directory.EnumerateFiles(directory, "client_cell_*.dat")
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .Cast<string>()
                    .OrderBy(f => f)
                    .ToList();

                if (!cellFiles.Any()) {
                    return Result<(Guid id, ManagedDatSet metadata)>.Failure("Missing required cell DAT file: client_cell_1.dat", "MISSING_DAT");
                }

                var allFiles = requiredFiles.Concat(cellFiles).ToList();
                var fileHashes = new Dictionary<string, string>();
                var iterations = new Dictionary<string, int>();
                var cellIterations = new Dictionary<int, int>();

                using var reader = new DefaultDatReaderWriter(directory);
                iterations["client_portal.dat"] = reader.PortalIteration;
                iterations["client_highres.dat"] = reader.HighResIteration;
                iterations["client_local_English.dat"] = reader.LanguageIteration;

                foreach (var cellRegion in reader.CellRegions) {
                    var cellFileName = $"client_cell_{cellRegion.Key}.dat";
                    iterations[cellFileName] = cellRegion.Value.Iteration;
                    cellIterations[(int)cellRegion.Key] = cellRegion.Value.Iteration;
                }

                var combinedStringBuilder = new StringBuilder();
                foreach (var file in allFiles) {
                    var path = Path.Combine(directory, file);
                    if (!File.Exists(path)) {
                        return Result<(Guid id, ManagedDatSet metadata)>.Failure($"Missing required DAT file: {file}", "MISSING_DAT");
                    }

                    var hash = await CalculateFileHashAsync(path, ct);
                    fileHashes[file] = hash;
                    combinedStringBuilder.Append(hash);
                    combinedStringBuilder.Append(iterations.TryGetValue(file, out var iter) ? iter : 0);
                }

                // Generate version 5 style GUID from combined hash
                var combinedHash = CalculateStringHash(combinedStringBuilder.ToString());
                var guidBytes = MD5.HashData(Encoding.UTF8.GetBytes(combinedHash));
                var id = new Guid(guidBytes);

                var metadata = new ManagedDatSet {
                    Id = id,
                    PortalIteration = iterations["client_portal.dat"],
                    CellIteration = cellIterations.TryGetValue(1, out var c1) ? c1 : 0,
                    CellIterations = cellIterations,
                    HighResIteration = iterations["client_highres.dat"],
                    LanguageIteration = iterations["client_local_English.dat"],
                    CombinedMd5 = combinedHash,
                    FileHashes = fileHashes,
                    ImportDate = DateTime.UtcNow
                };

                return Result<(Guid id, ManagedDatSet metadata)>.Success((id, metadata));
            }
            catch (Exception ex) {
                return Result<(Guid id, ManagedDatSet metadata)>.Failure(ex.Message, "HASH_CALCULATION_FAILED");
            }
        }

        private async Task<string> CalculateFileHashAsync(string path, CancellationToken ct) {
            using var md5 = MD5.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var hashBytes = await md5.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hashBytes);
        }

        private string CalculateStringHash(string input) {
            var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes);
        }

        public async Task<Result<ManagedDatSet>> ImportAsync(string sourceDirectory, string? friendlyName, IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            var idResult = await CalculateDeterministicIdAsync(sourceDirectory, ct);
            if (idResult.IsFailure) return Result<ManagedDatSet>.Failure(idResult.Error.Message, idResult.Error.Code);

            var (id, metadata) = idResult.Value;
            var existing = GetManagedDataSet(id);
            if (existing != null) {
                return Result<ManagedDatSet>.Success(existing);
            }

            var targetDirectory = Path.Combine(_repositoryRoot, id.ToString());
            if (!Directory.Exists(targetDirectory)) {
                Directory.CreateDirectory(targetDirectory);
            }

            var filesToCopy = Directory.EnumerateFiles(sourceDirectory, "client_*.dat").ToList();
            var totalBytes = filesToCopy.Sum(f => new FileInfo(f).Length);
            long copiedBytes = 0;

            foreach (var file in filesToCopy) {
                var fileName = Path.GetFileName(file);
                var targetPath = Path.Combine(targetDirectory, fileName);
                
                progress?.Report(($"Copying {fileName}...", (float)copiedBytes / totalBytes));

                using var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                
                var buffer = new byte[81920];
                int read;
                while ((read = await sourceStream.ReadAsync(buffer, ct)) > 0) {
                    await targetStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    copiedBytes += read;
                    progress?.Report(($"Copying {fileName}...", (float)copiedBytes / totalBytes));
                }
            }

            metadata.FriendlyName = friendlyName ?? GetDefaultFriendlyName(metadata);
            _managedSets.Add(metadata);
            SaveRegistry();

            return Result<ManagedDatSet>.Success(metadata);
        }

        public string GetDatSetPath(Guid id, string projectDirectory) {
            // Path is relative to project directory: ../Dats/<Id>
            // But we store it in RepositoryRoot. 
            // If RepositoryRoot is set, we use that.
            return Path.Combine(_repositoryRoot, id.ToString());
        }

        public async Task<Result<Unit>> DeleteAsync(Guid id, CancellationToken ct) {
            var set = GetManagedDataSet(id);
            if (set == null) return Result<Unit>.Failure("Managed DAT set not found", "NOT_FOUND");

            var setDir = Path.Combine(_repositoryRoot, id.ToString());
            DatUtils.DeleteDatSet(setDir, _log);

            _managedSets.Remove(set);
            SaveRegistry();
            return Result<Unit>.Success(Unit.Value);
        }

        public async Task<IReadOnlyList<string>> GetProjectsUsingAsync(Guid id, string searchRoot, CancellationToken ct) {
            return new List<string>();
        }

        public async Task<Result<Unit>> UpdateFriendlyNameAsync(Guid id, string newFriendlyName, CancellationToken ct) {
            var set = GetManagedDataSet(id);
            if (set == null) return Result<Unit>.Failure("Managed DAT set not found", "NOT_FOUND");

            set.FriendlyName = newFriendlyName;
            SaveRegistry();
            return Result<Unit>.Success(Unit.Value);
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ManagedDatSet))]
    [JsonSerializable(typeof(List<ManagedDatSet>))]
    internal partial class DatSourceGenerationContext : JsonSerializerContext {
    }
}
