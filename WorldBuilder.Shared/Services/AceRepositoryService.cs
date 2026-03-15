using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Implementation of IAceRepositoryService for managing centralized ACE SQLite databases.
    /// </summary>
    public partial class AceRepositoryService : IAceRepositoryService {
        private readonly ILogger<AceRepositoryService> _log;
        private string _repositoryRoot = string.Empty;
        private List<ManagedAceDb> _managedDbs = [];
        private readonly string _registryFileName = "managed_ace_dbs.json";
        private readonly HttpClient _httpClient;

        public string RepositoryRoot => _repositoryRoot;

        public AceRepositoryService(ILogger<AceRepositoryService> log, HttpClient httpClient) {
            _log = log;
            _httpClient = httpClient;
            // GitHub API requires a User-Agent
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent")) {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "WorldBuilder");
            }
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
                    _managedDbs = JsonSerializer.Deserialize(json, AceSourceGenerationContext.Default.ListManagedAceDb) ?? [];
                }
                catch (Exception ex) {
                    _log.LogError(ex, "Failed to load managed ACE DB registry");
                    _managedDbs = [];
                }
            }
        }

        private void SaveRegistry() {
            var registryPath = Path.Combine(_repositoryRoot, _registryFileName);
            try {
                var json = JsonSerializer.Serialize(_managedDbs, AceSourceGenerationContext.Default.ListManagedAceDb);
                File.WriteAllText(registryPath, json);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to save managed ACE DB registry");
            }
        }

        public IReadOnlyList<ManagedAceDb> GetManagedAceDbs() => _managedDbs.AsReadOnly();

        public ManagedAceDb? GetManagedAceDb(Guid id) => _managedDbs.FirstOrDefault(d => d.Id == id);

        public async Task<Result<ManagedAceDb>> ImportAsync(string sourcePath, string? friendlyName, IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            try {
                if (!File.Exists(sourcePath)) {
                    return Result<ManagedAceDb>.Failure("Source file not found", "NOT_FOUND");
                }

                progress?.Report(("Calculating hash...", 0.1f));
                var md5 = await CalculateFileHashAsync(sourcePath, ct);

                // Check for existing by MD5
                var existing = _managedDbs.FirstOrDefault(d => d.Md5 == md5);
                if (existing != null) {
                    return Result<ManagedAceDb>.Success(existing);
                }

                progress?.Report(("Reading version info...", 0.2f));
                var versionInfo = await ReadVersionInfoAsync(sourcePath, ct);

                // Deterministic ID from MD5
                var id = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(md5)));

                var targetPath = Path.Combine(_repositoryRoot, id.ToString(), "ace_world.db");
                var targetDir = Path.GetDirectoryName(targetPath)!;
                if (!Directory.Exists(targetDir)) {
                    Directory.CreateDirectory(targetDir);
                }

                progress?.Report(("Copying file...", 0.3f));
                await CopyFileAsync(sourcePath, targetPath, progress, ct);

                var metadata = new ManagedAceDb {
                    Id = id,
                    FriendlyName = friendlyName ?? $"ACE {versionInfo.patchVersion} ({versionInfo.baseVersion})",
                    BaseVersion = versionInfo.baseVersion,
                    PatchVersion = versionInfo.patchVersion,
                    LastModified = versionInfo.lastModified,
                    Md5 = md5,
                    ImportDate = DateTime.UtcNow
                };

                _managedDbs.Add(metadata);
                SaveRegistry();

                return Result<ManagedAceDb>.Success(metadata);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to import ACE DB");
                return Result<ManagedAceDb>.Failure(ex.Message, "IMPORT_FAILED");
            }
        }

        private async Task<(string baseVersion, string patchVersion, string lastModified)> ReadVersionInfoAsync(string dbPath, CancellationToken ct) {
            try {
                var connectionString = $"Data Source={dbPath};Mode=ReadOnly";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync(ct);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT base_Version, patch_Version, last_Modified FROM version LIMIT 1";
                using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct)) {
                    return (
                        reader.IsDBNull(0) ? "" : reader.GetString(0),
                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2)
                    );
                }
            }
            catch (Exception ex) {
                _log.LogWarning(ex, "Failed to read version table from ACE DB");
            }
            return ("Unknown", "Unknown", "Unknown");
        }

        public async Task<Result<ManagedAceDb>> DownloadLatestAsync(IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            try {
                progress?.Report(("Fetching latest release info from GitHub...", 0.05f));
                var release = await _httpClient.GetFromJsonAsync("https://api.github.com/repos/amoeba/ace-to-sqlite/releases/latest", AceSourceGenerationContext.Default.GitHubRelease, ct);
                if (release == null) return Result<ManagedAceDb>.Failure("Failed to fetch release info", "DOWNLOAD_FAILED");

                var asset = release.Assets.FirstOrDefault(a => a.Name.Equals("ace_world.db", StringComparison.OrdinalIgnoreCase));
                if (asset == null) return Result<ManagedAceDb>.Failure("Release does not contain ace_world.db", "ASSET_NOT_FOUND");

                var tempFile = Path.Combine(Path.GetTempPath(), $"ace_world_{Guid.NewGuid()}.db");
                
                progress?.Report(($"Downloading {asset.Name} ({asset.Size / 1024 / 1024} MB)...", 0.1f));
                
                using (var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)) {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
                    
                    using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    using (var stream = await response.Content.ReadAsStreamAsync(ct)) {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int read;
                        while ((read = await stream.ReadAsync(buffer, ct)) > 0) {
                            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                            totalRead += read;
                            progress?.Report(($"Downloading {asset.Name} ({(float)totalRead / 1024 / 1024:F1} / {(float)totalBytes / 1024 / 1024:F1} MB)...", 0.1f + (float)totalRead / totalBytes * 0.8f));
                        }
                    }
                }

                var result = await ImportAsync(tempFile, $"ACE ({release.TagName})", progress, ct);
                
                try {
                    File.Delete(tempFile);
                } catch { /* ignore */ }

                return result;
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to download ACE DB");
                return Result<ManagedAceDb>.Failure(ex.Message, "DOWNLOAD_FAILED");
            }
        }

        public string GetAceDbPath(Guid id, string projectDirectory) {
            return Path.Combine(_repositoryRoot, id.ToString(), "ace_world.db");
        }

        public async Task<Result<Unit>> DeleteAsync(Guid id, CancellationToken ct) {
            var db = GetManagedAceDb(id);
            if (db == null) return Result<Unit>.Failure("Managed ACE DB not found", "NOT_FOUND");

            var dbDir = Path.Combine(_repositoryRoot, id.ToString());
            if (Directory.Exists(dbDir)) {
                try {
                    Directory.Delete(dbDir, true);
                }
                catch (Exception ex) {
                    _log.LogError(ex, "Failed to delete ACE DB directory");
                    return Result<Unit>.Failure($"Failed to delete files: {ex.Message}", "DELETE_FAILED");
                }
            }

            _managedDbs.Remove(db);
            SaveRegistry();
            return Result<Unit>.Success(Unit.Value);
        }

        public async Task<Result<Unit>> UpdateFriendlyNameAsync(Guid id, string newFriendlyName, CancellationToken ct) {
            var db = GetManagedAceDb(id);
            if (db == null) return Result<Unit>.Failure("Managed ACE DB not found", "NOT_FOUND");

            db.FriendlyName = newFriendlyName;
            SaveRegistry();
            return Result<Unit>.Success(Unit.Value);
        }

        private async Task<string> CalculateFileHashAsync(string path, CancellationToken ct) {
            using var md5 = MD5.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var hashBytes = await md5.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hashBytes);
        }

        private async Task CopyFileAsync(string source, string target, IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            var fileInfo = new FileInfo(source);
            var totalBytes = fileInfo.Length;
            long copiedBytes = 0;

            using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using var targetStream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            
            var buffer = new byte[81920];
            int read;
            while ((read = await sourceStream.ReadAsync(buffer, ct)) > 0) {
                await targetStream.WriteAsync(buffer.AsMemory(0, read), ct);
                copiedBytes += read;
                progress?.Report(($"Copying {Path.GetFileName(source)} ({(float)copiedBytes / 1024 / 1024:F1} / {(float)totalBytes / 1024 / 1024:F1} MB)...", 0.3f + (float)copiedBytes / totalBytes * 0.7f));
            }
        }

        internal class GitHubRelease {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;
            [JsonPropertyName("assets")]
            public List<GitHubAsset> Assets { get; set; } = [];
        }

        internal class GitHubAsset {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
            [JsonPropertyName("size")]
            public long Size { get; set; }
            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }

        [JsonSourceGenerationOptions(WriteIndented = true)]
        [JsonSerializable(typeof(ManagedAceDb))]
        [JsonSerializable(typeof(List<ManagedAceDb>))]
        [JsonSerializable(typeof(GitHubRelease))]
        internal partial class AceSourceGenerationContext : JsonSerializerContext {
        }
    }
}
