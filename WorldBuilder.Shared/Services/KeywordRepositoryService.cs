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
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Onnx;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using Microsoft.Extensions.VectorData;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Implementation of IKeywordRepositoryService for managing generated keyword databases.
    /// </summary>
    public partial class KeywordRepositoryService : IKeywordRepositoryService {
        private readonly ILogger<KeywordRepositoryService> _log;
        private readonly IDatRepositoryService _datRepository;
        private readonly IAceRepositoryService _aceRepository;
        private readonly System.Net.Http.HttpClient _httpClient;
        private string _repositoryRoot = string.Empty;
        private string _modelsRoot = string.Empty;
        private List<ManagedKeywordDb> _managedKeywordDbs = [];
        private readonly string _registryFileName = "managed_keywords.json";
        private readonly ReadOnlyMemory<float> _emptyVector = new float[384];
        private readonly SemaphoreSlim _dbLock = new(1, 1);

        public event EventHandler<IKeywordRepositoryService.KeywordGenerationProgress>? GlobalProgress;

        public string RepositoryRoot => _repositoryRoot;

        public KeywordRepositoryService(ILogger<KeywordRepositoryService> log, IDatRepositoryService datRepository, IAceRepositoryService aceRepository, System.Net.Http.HttpClient httpClient) {
            _log = log;
            _datRepository = datRepository;
            _aceRepository = aceRepository;
            _httpClient = httpClient;
        }

        public void SetRepositoryRoot(string rootDirectory) {
            _repositoryRoot = rootDirectory;
            if (!Directory.Exists(_repositoryRoot)) {
                Directory.CreateDirectory(_repositoryRoot);
            }
            LoadRegistry();
        }

        public void SetModelsRoot(string modelsDirectory) {
            _modelsRoot = modelsDirectory;
            if (!Directory.Exists(_modelsRoot)) {
                Directory.CreateDirectory(_modelsRoot);
            }
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
            var tempPath = registryPath + ".tmp";
            try {
                var json = JsonSerializer.Serialize(_managedKeywordDbs, KeywordSourceGenerationContext.Default.ListManagedKeywordDb);
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, registryPath, true);
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

            if (!db.IsComplete) {
                _log.LogDebug("Keywords not valid for {DatId}/{AceId}: Generation incomplete (Keyword: {KP}, Name: {NP}, Desc: {DP})", datId, aceId, db.KeywordProgress, db.NameEmbeddingProgress, db.DescEmbeddingProgress);
                return false;
            }
            
            var path = GetKeywordDbPath(datId, aceId);
            bool exists = File.Exists(path);
            if (!exists) {
                _log.LogDebug("Keywords not valid for {DatId}/{AceId}: Database file not found at {Path}", datId, aceId, path);
            }
            return exists;
        }

        public bool CanSearchKeywords(Guid datId, Guid aceId) {
            var db = GetManagedKeywordDb(datId, aceId);
            if (db == null || db.GeneratorVersion != IKeywordRepositoryService.CurrentGeneratorVersion) {
                return false;
            }
            
            if (db.KeywordProgress < 1f) {
                return false;
            }

            return File.Exists(GetKeywordDbPath(datId, aceId));
        }

        public string GetKeywordDbPath(Guid datId, Guid aceId) {
            return Path.Combine(_repositoryRoot, $"keywords_{datId}_{aceId}.db");
        }

        public async Task<Result<ManagedKeywordDb>> GenerateAsync(Guid datId, Guid aceId, bool forceRegenerate, CancellationToken ct) {
            _log.LogInformation("Generating keyword database for DatSet {DatId} and AceDb {AceId}...", datId, aceId);
            try {
                var acePath = _aceRepository.GetAceDbPath(aceId, string.Empty);
                if (!File.Exists(acePath)) {
                    _log.LogError("ACE database not found at {Path}", acePath);
                    return Result<ManagedKeywordDb>.Failure($"ACE database not found at {acePath}", "ACE_DB_NOT_FOUND");
                }

                var targetPath = GetKeywordDbPath(datId, aceId);
                var connectionString = $"Data Source={targetPath}";

                var metadata = _managedKeywordDbs.FirstOrDefault(d => d.DatSetId == datId && d.AceDbId == aceId);
                if (forceRegenerate || metadata == null || metadata.GeneratorVersion != IKeywordRepositoryService.CurrentGeneratorVersion) {
                    if (File.Exists(targetPath)) {
                        File.Delete(targetPath);
                    }
                    
                    // Remove all existing entries for this dat/ace combo to ensure no duplicates
                    _managedKeywordDbs.RemoveAll(d => d.DatSetId == datId && d.AceDbId == aceId);
                    
                    metadata = new ManagedKeywordDb {
                        DatSetId = datId,
                        AceDbId = aceId,
                        GeneratorVersion = IKeywordRepositoryService.CurrentGeneratorVersion,
                        LastGenerated = DateTime.UtcNow,
                        KeywordProgress = 0,
                        NameEmbeddingProgress = 0,
                        DescEmbeddingProgress = 0
                    };
                    _managedKeywordDbs.Add(metadata);
                    SaveRegistry();
                }

                // 1. Keyword Extraction
                if (metadata.KeywordProgress < 1f) {
                    GlobalProgress?.Invoke(this, new IKeywordRepositoryService.KeywordGenerationProgress("Extracting keywords from ACE database...", 0.1f, 0f, 0f));

                    // Map SetupId -> (Names, Descriptions)
                    var setupKeywords = new Dictionary<uint, (HashSet<string> Names, HashSet<string> Descriptions)>();

                    var optionsBuilder = new DbContextOptionsBuilder<WorldDbContext>();
                    optionsBuilder.UseSqlite($"Data Source={acePath}");

                    using (var context = new WorldDbContext(optionsBuilder.Options)) {
                        var data = await context.Weenie
                            .Where(w => !w.WeeniePropertiesFloat.Any(wpf => wpf.Type == (ushort)PropertyFloat.GeneratorRadius))
                            .SelectMany(w => w.WeeniePropertiesDID.Where(wpd => wpd.Type == (ushort)PropertyDataId.Setup), (w, wpd) => new {
                                SetupId = wpd.Value,
                                Strings = w.WeeniePropertiesString.Select(s => new { s.Type, s.Value })
                            })
                            .ToListAsync(ct);

                        foreach (var item in data) {
                            if (!setupKeywords.TryGetValue(item.SetupId, out var keywords)) {
                                keywords = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                                setupKeywords[item.SetupId] = keywords;
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
                    
                    using (var targetConn = new SqliteConnection(connectionString)) {
                        await targetConn.OpenAsync(ct);
                        using (var transaction = targetConn.BeginTransaction()) {
                            // Create SetupKeywords table (compatibility with old search if needed)
                            using (var cmd = targetConn.CreateCommand()) {
                                cmd.CommandText = "CREATE TABLE IF NOT EXISTS SetupKeywords (SetupId INTEGER PRIMARY KEY, Names TEXT, Descriptions TEXT)";
                                await cmd.ExecuteNonQueryAsync(ct);
                            }

                            // Insert keywords
                            var insertCmd = targetConn.CreateCommand();
                            insertCmd.CommandText = "INSERT OR REPLACE INTO SetupKeywords (SetupId, Names, Descriptions) VALUES (@id, @names, @descriptions)";
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
                                    var prog = (float)count / total;
                                    GlobalProgress?.Invoke(this, new IKeywordRepositoryService.KeywordGenerationProgress($"Saving keywords ({count}/{total})...", prog, 0f, 0f));
                                }
                            }
                            await transaction.CommitAsync(ct);
                        }
                    }
                    metadata.KeywordProgress = 1f;
                    SaveRegistry();
                }

                // 2. Vector Initialization
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString)) {
                    await conn.OpenAsync(ct);
                    using (var cmd = conn.CreateCommand()) {
                        cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
                var vectorStore = new SqliteVectorStore(connectionString);
                var collection = vectorStore.GetCollection<int, KeywordVectorRecord>("setup_vectors");
                await collection.EnsureCollectionExistsAsync(ct);

                // 3. Embedding Generation
                var modelDir = Path.Combine(_modelsRoot, "bge-micro-v2");
                if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);
                var modelPath = Path.Combine(modelDir, "model.onnx");
                var vocabPath = Path.Combine(modelDir, "vocab.txt");

                var downloadResult = await EnsureModelDownloadedAsync(modelPath, vocabPath, ct);
                if (downloadResult.IsFailure) {
                    _log.LogWarning("Embedding model missing and download failed. Skipping vector generation. Error: {Error}", downloadResult.Error.Message);
                    GlobalProgress?.Invoke(this, new IKeywordRepositoryService.KeywordGenerationProgress("Embedding generation failed (model missing)", 1f, 1f, 1f));
                    metadata.NameEmbeddingProgress = 1f;
                    metadata.DescEmbeddingProgress = 1f;
                    SaveRegistry();
                    return Result<ManagedKeywordDb>.Success(metadata);
                }

                var kernelBuilder = Kernel.CreateBuilder();
                #pragma warning disable SKEXP0070
                kernelBuilder.AddBertOnnxEmbeddingGenerator(modelPath, vocabPath, new BertOnnxOptions { NormalizeEmbeddings = true, MaximumTokens = 512 });
                var kernel = kernelBuilder.Build();
                var embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
                #pragma warning restore SKEXP0070

                var maxParallelism = Math.Max(1, Environment.ProcessorCount / 2);

                // Get all setups that need embeddings
                var pendingSetups = new List<(uint SetupId, string Names, string Descriptions)>();
                using (var conn = new SqliteConnection(connectionString)) {
                    await conn.OpenAsync(ct);
                    using (var cmd = conn.CreateCommand()) {
                        cmd.CommandText = "SELECT SetupId, Names, Descriptions FROM SetupKeywords";
                        using (var reader = await cmd.ExecuteReaderAsync(ct)) {
                            while (await reader.ReadAsync(ct)) {
                                pendingSetups.Add(((uint)reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
                            }
                        }
                    }
                }

                // Name Embeddings
                if (metadata.NameEmbeddingProgress < 1f) {
                    int total = pendingSetups.Count;
                    int batchSize = 128;
                    int skipCount = (int)(metadata.NameEmbeddingProgress * total);
                    int processed = skipCount;
                    
                    var batches = Enumerable.Range(0, (total - skipCount + batchSize - 1) / batchSize)
                        .Select(i => pendingSetups.Skip(skipCount + i * batchSize).Take(batchSize).ToList());

                    await Parallel.ForEachAsync(batches, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = ct }, async (batch, bct) => {
                        var namesToEmbed = batch.Select(b => string.IsNullOrWhiteSpace(b.Names) ? " " : b.Names).ToList();
                        var embeddings = await embeddingGenerator.GenerateAsync(namesToEmbed, cancellationToken: bct);
                        
                        await _dbLock.WaitAsync(bct);
                        try {
                            for (int j = 0; j < batch.Count; j++) {
                                var record = await collection.GetAsync((int)batch[j].SetupId, cancellationToken: bct) ?? new KeywordVectorRecord { SetupId = (int)batch[j].SetupId, Names = batch[j].Names, Descriptions = batch[j].Descriptions };
                                record.NameEmbedding = embeddings[j].Vector;
                                if (record.DescEmbedding.Length == 0) record.DescEmbedding = _emptyVector;
                                await collection.UpsertAsync(record, cancellationToken: bct);
                            }

                            processed += batch.Count;
                            metadata.NameEmbeddingProgress = (float)processed / total;
                            GlobalProgress?.Invoke(this, new IKeywordRepositoryService.KeywordGenerationProgress($"Generating name embeddings ({processed}/{total})...", 1f, metadata.NameEmbeddingProgress, 0f));
                            if (processed % (batchSize * 5) < batchSize) SaveRegistry();
                        } finally {
                            _dbLock.Release();
                        }
                    });
                    
                    metadata.NameEmbeddingProgress = 1f;
                    SaveRegistry();
                }

                // Description Embeddings
                if (metadata.DescEmbeddingProgress < 1f) {
                    int total = pendingSetups.Count;
                    int batchSize = 128;
                    int skipCount = (int)(metadata.DescEmbeddingProgress * total);
                    int processed = skipCount;

                    var batches = Enumerable.Range(0, (total - skipCount + batchSize - 1) / batchSize)
                        .Select(i => pendingSetups.Skip(skipCount + i * batchSize).Take(batchSize).ToList());

                    await Parallel.ForEachAsync(batches, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = ct }, async (batch, bct) => {
                        var descsToEmbed = batch.Select(b => string.IsNullOrWhiteSpace(b.Descriptions) ? " " : b.Descriptions).ToList();
                        var embeddings = await embeddingGenerator.GenerateAsync(descsToEmbed, cancellationToken: bct);
                        
                        await _dbLock.WaitAsync(bct);
                        try {
                            for (int j = 0; j < batch.Count; j++) {
                                var record = await collection.GetAsync((int)batch[j].SetupId, cancellationToken: bct) ?? new KeywordVectorRecord { SetupId = (int)batch[j].SetupId, Names = batch[j].Names, Descriptions = batch[j].Descriptions };
                                record.DescEmbedding = embeddings[j].Vector;
                                if (record.NameEmbedding.Length == 0) record.NameEmbedding = _emptyVector;
                                await collection.UpsertAsync(record, cancellationToken: bct);
                            }

                            processed += batch.Count;
                            metadata.DescEmbeddingProgress = (float)processed / total;
                            GlobalProgress?.Invoke(this, new IKeywordRepositoryService.KeywordGenerationProgress($"Generating description embeddings ({processed}/{total})...", 1f, 1f, metadata.DescEmbeddingProgress));
                            if (processed % (batchSize * 5) < batchSize) SaveRegistry();
                        } finally {
                            _dbLock.Release();
                        }
                    });

                    metadata.DescEmbeddingProgress = 1f;
                    SaveRegistry();
                }

                metadata.LastGenerated = DateTime.UtcNow;
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

        private async Task<Result<Unit>> EnsureModelDownloadedAsync(string modelPath, string vocabPath, CancellationToken ct) {
            var modelUrl = "https://huggingface.co/TaylorAI/bge-micro-v2/resolve/main/onnx/model.onnx";
            var vocabUrl = "https://huggingface.co/TaylorAI/bge-micro-v2/resolve/main/vocab.txt";

            try {
                if (!File.Exists(modelPath)) {
                    _log.LogInformation("Downloading embedding model from {Url}...", modelUrl);
                    GlobalProgress?.Invoke(this, new IKeywordRepositoryService.KeywordGenerationProgress("Downloading embedding model...", 1f, 0f, 0f));
                    var response = await _httpClient.GetAsync(modelUrl, ct);
                    response.EnsureSuccessStatusCode();
                    await using var fs = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs, ct);
                }

                if (!File.Exists(vocabPath)) {
                    _log.LogInformation("Downloading vocabulary from {Url}...", vocabUrl);
                    GlobalProgress?.Invoke(this, new IKeywordRepositoryService.KeywordGenerationProgress("Downloading model vocabulary...", 1f, 0f, 0f));
                    var response = await _httpClient.GetAsync(vocabUrl, ct);
                    response.EnsureSuccessStatusCode();
                    await using var fs = new FileStream(vocabPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs, ct);
                }

                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to download embedding model");
                return Result<Unit>.Failure($"Failed to download embedding model: {ex.Message}", "DOWNLOAD_FAILED");
            }
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

            var results = new Dictionary<uint, double>();
            try {
                // 1. Traditional Text Search (Highest Priority)
                using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly")) {
                    await connection.OpenAsync(ct);

                    using (var cmd = connection.CreateCommand()) {
                        // Boost exact name matches, then partial name matches, then description matches
                        cmd.CommandText = @"
                            SELECT SetupId, 
                                (CASE 
                                    WHEN Names COLLATE NOCASE = @exactQuery THEN 100.0
                                    WHEN Names COLLATE NOCASE LIKE @exactWord1 OR Names COLLATE NOCASE LIKE @exactWord2 OR Names COLLATE NOCASE LIKE @exactWord3 THEN 90.0 - (length(Names) * 0.01)
                                    WHEN Names COLLATE NOCASE LIKE @startsWith THEN 75.0 - (length(Names) * 0.01)
                                    WHEN Names COLLATE NOCASE LIKE @query THEN 50.0 - (length(Names) * 0.01)
                                    WHEN Descriptions COLLATE NOCASE LIKE @query THEN 10.0
                                    ELSE 1.0
                                END) as Score
                            FROM SetupKeywords 
                            WHERE Names COLLATE NOCASE LIKE @query OR Descriptions COLLATE NOCASE LIKE @query";
                        cmd.Parameters.AddWithValue("@exactQuery", query);
                        cmd.Parameters.AddWithValue("@exactWord1", $"{query} %");
                        cmd.Parameters.AddWithValue("@exactWord2", $"% {query} %");
                        cmd.Parameters.AddWithValue("@exactWord3", $"% {query}");
                        cmd.Parameters.AddWithValue("@startsWith", $"{query}%");
                        cmd.Parameters.AddWithValue("@query", $"%{query}%");
                        
                        using (var reader = await cmd.ExecuteReaderAsync(ct)) {
                            while (await reader.ReadAsync(ct)) {
                                var setupId = (uint)reader.GetInt64(0);
                                var score = reader.GetDouble(1);
                                results[setupId] = score;
                            }
                        }
                    }
                }

                // 2. Vector Search (Hybrid Ranking)
                var modelDir = Path.Combine(_modelsRoot, "bge-micro-v2");
                var modelPath = Path.Combine(modelDir, "model.onnx");
                var vocabPath = Path.Combine(modelDir, "vocab.txt");

                if (File.Exists(modelPath) && File.Exists(vocabPath)) {
                    var kernelBuilder = Kernel.CreateBuilder();
                    #pragma warning disable SKEXP0070
                    kernelBuilder.AddBertOnnxEmbeddingGenerator(modelPath, vocabPath, new BertOnnxOptions { NormalizeEmbeddings = true, MaximumTokens = 512 });
                    var kernel = kernelBuilder.Build();
                    var embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
                    #pragma warning restore SKEXP0070

                    var queryEmbedding = await embeddingGenerator.GenerateAsync(new[] { query }, cancellationToken: ct);
                    var vector = queryEmbedding[0].Vector;

                    var vectorStore = new SqliteVectorStore($"Data Source={path}");
                    var collection = vectorStore.GetCollection<int, KeywordVectorRecord>("setup_vectors");

                    // Search names
                    var nameSearchOptions = new VectorSearchOptions<KeywordVectorRecord> { VectorProperty = r => r.NameEmbedding };
                    await foreach (var result in collection.SearchAsync(vector, 50, nameSearchOptions, ct)) {
                        var score = (result.Score ?? 0) * 20.0; // Boost vector name match
                        var setupId = (uint)result.Record.SetupId;
                        if (results.TryGetValue(setupId, out var existingScore)) {
                            results[setupId] = Math.Max(existingScore, score);
                        } else {
                            results[setupId] = score;
                        }
                    }

                    // Search descriptions
                    var descSearchOptions = new VectorSearchOptions<KeywordVectorRecord> { VectorProperty = r => r.DescEmbedding };
                    await foreach (var result in collection.SearchAsync(vector, 50, descSearchOptions, ct)) {
                        var score = (result.Score ?? 0) * 5.0; // Lower boost for description vector match
                        var setupId = (uint)result.Record.SetupId;
                        if (results.TryGetValue(setupId, out var existingScore)) {
                            results[setupId] = Math.Max(existingScore, score);
                        } else {
                            results[setupId] = score;
                        }
                    }
                }
            }
            catch (OperationCanceledException) {
                // Ignore cancellation
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to search keywords for query {Query}", query);
            }

            return results.OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        }

        [JsonSourceGenerationOptions(WriteIndented = true)]
        [JsonSerializable(typeof(ManagedKeywordDb))]
        [JsonSerializable(typeof(List<ManagedKeywordDb>))]
        internal partial class KeywordSourceGenerationContext : JsonSerializerContext {
        }
    }
}
