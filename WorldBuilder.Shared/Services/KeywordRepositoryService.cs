using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ACE.Database.Models.World;
using ACE.Entity.Enum.Properties;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Implementation of IKeywordRepositoryService for managing generated keyword databases.
    /// </summary>
    public partial class KeywordRepositoryService : IKeywordRepositoryService {
        private readonly ILogger<KeywordRepositoryService> _log;
        private readonly IDatRepositoryService _datRepository;
        private readonly IAceRepositoryService _aceRepository;
        private string _repositoryRoot = string.Empty;
        private List<ManagedKeywordDb> _managedKeywordDbs = [];
        private readonly string _registryFileName = "managed_keywords.json";

        public string RepositoryRoot => _repositoryRoot;

        public KeywordRepositoryService(ILogger<KeywordRepositoryService> log, IDatRepositoryService datRepository, IAceRepositoryService aceRepository) {
            _log = log;
            _datRepository = datRepository;
            _aceRepository = aceRepository;
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
                    _managedKeywordDbs = JsonSerializer.Deserialize(json, KeywordSourceGenerationContext.Default.ListManagedKeywordDb) ?? [];
                }
                catch (Exception ex) {
                    _log.LogError(ex, "Failed to load managed keyword registry");
                    _managedKeywordDbs = [];
                }
            }
        }

        private void SaveRegistry() {
            var registryPath = Path.Combine(_repositoryRoot, _registryFileName);
            try {
                var json = JsonSerializer.Serialize(_managedKeywordDbs, KeywordSourceGenerationContext.Default.ListManagedKeywordDb);
                File.WriteAllText(registryPath, json);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to save managed keyword registry");
            }
        }

        public IReadOnlyList<ManagedKeywordDb> GetManagedKeywordDbs() => _managedKeywordDbs.AsReadOnly();

        public ManagedKeywordDb? GetManagedKeywordDb(Guid datId, Guid aceId) {
            return _managedKeywordDbs.FirstOrDefault(d => d.DatSetId == datId && d.AceDbId == aceId);
        }

        public bool AreKeywordsValid(Guid datId, Guid aceId) {
            var db = GetManagedKeywordDb(datId, aceId);
            if (db == null) {
                _log.LogDebug("Keywords not valid for {DatId}/{AceId}: Not found in registry", datId, aceId);
                return false;
            }
            if (db.GeneratorVersion != IKeywordRepositoryService.CurrentGeneratorVersion) {
                _log.LogDebug("Keywords not valid for {DatId}/{AceId}: Version mismatch (Found: {Version}, Expected: {CurrentVersion})", datId, aceId, db.GeneratorVersion, IKeywordRepositoryService.CurrentGeneratorVersion);
                return false;
            }
            
            var path = GetKeywordDbPath(datId, aceId);
            bool exists = File.Exists(path);
            if (!exists) {
                _log.LogDebug("Keywords not valid for {DatId}/{AceId}: Database file not found at {Path}", datId, aceId, path);
            }
            return exists;
        }

        public string GetKeywordDbPath(Guid datId, Guid aceId) {
            return Path.Combine(_repositoryRoot, $"keywords_{datId}_{aceId}.db");
        }

        public async Task<Result<ManagedKeywordDb>> GenerateAsync(Guid datId, Guid aceId, IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            _log.LogInformation("Generating keyword database for DatSet {DatId} and AceDb {AceId}...", datId, aceId);
            try {
                var acePath = _aceRepository.GetAceDbPath(aceId, string.Empty);
                if (!File.Exists(acePath)) {
                    _log.LogError("ACE database not found at {Path}", acePath);
                    return Result<ManagedKeywordDb>.Failure($"ACE database not found at {acePath}", "ACE_DB_NOT_FOUND");
                }

                progress?.Report(("Extracting keywords from ACE database...", 0.1f));

                // 1. Map SetupId -> (Names, Descriptions)
                var setupKeywords = new Dictionary<uint, (HashSet<string> Names, HashSet<string> Descriptions)>();

                var optionsBuilder = new DbContextOptionsBuilder<WorldDbContext>();
                optionsBuilder.UseSqlite($"Data Source={acePath}");

                using (var context = new WorldDbContext(optionsBuilder.Options)) {
                    var data = await context.Weenie
                        .SelectMany(w => w.WeeniePropertiesDID.Where(wpd => wpd.Type == (ushort)PropertyDataId.Setup), (w, wpd) => new {
                            SetupId = wpd.Value,
                            w.ClassName,
                            Strings = w.WeeniePropertiesString.Select(s => new { s.Type, s.Value })
                        })
                        .ToListAsync(ct);

                    foreach (var item in data) {
                        if (!setupKeywords.TryGetValue(item.SetupId, out var keywords)) {
                            keywords = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                            setupKeywords[item.SetupId] = keywords;
                        }

                        if (!string.IsNullOrWhiteSpace(item.ClassName)) {
                            keywords.Names.Add(item.ClassName);
                        }

                        foreach (var str in item.Strings) {
                            if (string.IsNullOrWhiteSpace(str.Value)) continue;

                            if (str.Type == (ushort)PropertyString.Name) {
                                keywords.Names.Add(str.Value);
                            }
                            else if (str.Type == (ushort)PropertyString.ShortDesc || str.Type == (ushort)PropertyString.LongDesc) {
                                keywords.Descriptions.Add(str.Value);
                            }
                        }
                    }
                }

                _log.LogInformation("Extracted keywords for {Count} setups.", setupKeywords.Count);
                progress?.Report(($"Extracted keywords for {setupKeywords.Count} setups. Saving to keyword database...", 0.5f));

                // 2. Save to local SQLite
                var targetPath = GetKeywordDbPath(datId, aceId);
                _log.LogInformation("Saving keywords to: {Path}", targetPath);
                if (File.Exists(targetPath)) {
                    File.Delete(targetPath);
                }

                using (var targetConn = new SqliteConnection($"Data Source={targetPath}")) {
                    await targetConn.OpenAsync(ct);

                    using (var transaction = targetConn.BeginTransaction()) {
                        // Create Metadata table
                        using (var cmd = targetConn.CreateCommand()) {
                            cmd.CommandText = "CREATE TABLE Metadata (Key TEXT PRIMARY KEY, Value TEXT)";
                            await cmd.ExecuteNonQueryAsync(ct);
                        }

                        // Store version info
                        using (var cmd = targetConn.CreateCommand()) {
                            cmd.CommandText = "INSERT INTO Metadata (Key, Value) VALUES ('GeneratorVersion', @version), ('LastGenerated', @date)";
                            cmd.Parameters.AddWithValue("@version", IKeywordRepositoryService.CurrentGeneratorVersion.ToString());
                            cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("o"));
                            await cmd.ExecuteNonQueryAsync(ct);
                        }

                        // Create SetupKeywords table
                        using (var cmd = targetConn.CreateCommand()) {
                            cmd.CommandText = "CREATE TABLE SetupKeywords (SetupId INTEGER PRIMARY KEY, Names TEXT, Descriptions TEXT)";
                            await cmd.ExecuteNonQueryAsync(ct);
                        }

                        // Insert keywords
                        var insertCmd = targetConn.CreateCommand();
                        insertCmd.CommandText = "INSERT INTO SetupKeywords (SetupId, Names, Descriptions) VALUES (@id, @names, @descriptions)";
                        var idParam = insertCmd.Parameters.Add("@id", SqliteType.Integer);
                        var namesParam = insertCmd.Parameters.Add("@names", SqliteType.Text);
                        var descParam = insertCmd.Parameters.Add("@descriptions", SqliteType.Text);

                        int count = 0;
                        int total = setupKeywords.Count;
                        foreach (var kvp in setupKeywords) {
                            idParam.Value = kvp.Key;
                            namesParam.Value = string.Join(" ", kvp.Value.Names);
                            descParam.Value = string.Join(" ", kvp.Value.Descriptions);
                            await insertCmd.ExecuteNonQueryAsync(ct);
                            
                            count++;
                            if (count % 100 == 0) {
                                progress?.Report(($"Saving keywords ({count}/{total})...", 0.5f + (float)count / total * 0.5f));
                            }
                        }

                        await transaction.CommitAsync(ct);
                    }
                }

                var metadata = new ManagedKeywordDb {
                    DatSetId = datId,
                    AceDbId = aceId,
                    GeneratorVersion = IKeywordRepositoryService.CurrentGeneratorVersion,
                    LastGenerated = DateTime.UtcNow
                };

                // Update registry
                var existing = _managedKeywordDbs.FirstOrDefault(d => d.DatSetId == datId && d.AceDbId == aceId);
                if (existing != null) {
                    _managedKeywordDbs.Remove(existing);
                }
                _managedKeywordDbs.Add(metadata);
                SaveRegistry();

                _log.LogInformation("Successfully generated keyword database for {DatId}/{AceId}.", datId, aceId);
                return Result<ManagedKeywordDb>.Success(metadata);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to generate keyword database");
                return Result<ManagedKeywordDb>.Failure(ex.Message, "GENERATION_FAILED");
            }
        }

        public async Task<Result<Unit>> DeleteAsync(Guid datId, Guid aceId, CancellationToken ct) {
            var db = GetManagedKeywordDb(datId, aceId);
            var path = GetKeywordDbPath(datId, aceId);
            
            if (File.Exists(path)) {
                try {
                    File.Delete(path);
                }
                catch (Exception ex) {
                    _log.LogError(ex, "Failed to delete keyword database file");
                    return Result<Unit>.Failure($"Failed to delete file: {ex.Message}", "DELETE_FAILED");
                }
            }

            if (db != null) {
                _managedKeywordDbs.Remove(db);
                SaveRegistry();
            }

            return Result<Unit>.Success(Unit.Value);
        }

        public async Task<(string Names, string Descriptions)?> GetKeywordsForSetupAsync(Guid datId, Guid aceId, uint setupId, CancellationToken ct) {
            var path = GetKeywordDbPath(datId, aceId);
            if (!File.Exists(path)) return null;

            try {
                using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly")) {
                    await connection.OpenAsync(ct);

                    using (var cmd = connection.CreateCommand()) {
                        cmd.CommandText = "SELECT Names, Descriptions FROM SetupKeywords WHERE SetupId = @id";
                        cmd.Parameters.AddWithValue("@id", setupId);
                        using (var reader = await cmd.ExecuteReaderAsync(ct)) {
                            if (await reader.ReadAsync(ct)) {
                                return (reader.GetString(0), reader.GetString(1));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to retrieve keywords for setup {SetupId}", setupId);
            }
            return null;
        }

        public async Task<List<uint>> SearchSetupsAsync(Guid datId, Guid aceId, string query, CancellationToken ct) {
            if (string.IsNullOrWhiteSpace(query)) return [];

            var path = GetKeywordDbPath(datId, aceId);
            if (!File.Exists(path)) return [];

            var results = new List<uint>();
            try {
                using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly")) {
                    await connection.OpenAsync(ct);

                    using (var cmd = connection.CreateCommand()) {
                        cmd.CommandText = "SELECT SetupId FROM SetupKeywords WHERE Names LIKE @query OR Descriptions LIKE @query";
                        cmd.Parameters.AddWithValue("@query", $"%{query}%");
                        
                        using (var reader = await cmd.ExecuteReaderAsync(ct)) {
                            while (await reader.ReadAsync(ct)) {
                                results.Add((uint)reader.GetInt64(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to search keywords for query {Query}", query);
            }
            return results;
        }

        [JsonSourceGenerationOptions(WriteIndented = true)]
        [JsonSerializable(typeof(ManagedKeywordDb))]
        [JsonSerializable(typeof(List<ManagedKeywordDb>))]
        internal partial class KeywordSourceGenerationContext : JsonSerializerContext {
        }
    }
}
