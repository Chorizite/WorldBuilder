using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using DatReaderWriter.DBObjs;
using WorldBuilder.Shared.Lib;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Onnx;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using Microsoft.Extensions.VectorData;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Services {
    public partial class KeywordRepositoryService : IKeywordRepositoryService {
        private record SetupKeywordData(
            HashSet<string> Names,
            HashSet<string> Tags,
            HashSet<string> Descriptions,
            string? WeenieType,
            string? CreatureType,
            string? ItemType
        ) {
            public SetupKeywordData() : this(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                null, null, null) { }
        }

        private const uint SetupIdPrefix    = 0x02000000;
        private const uint SetupIdMask      = 0xFF000000;
        private const int  EmbeddingDim     = 384;
        private const int  EmbeddingBatch   = 64;
        private const int  VectorTopK       = 250;

        // Keyword scoring
        private const double ScoreExactName       = 100.0;
        private const double ScoreWholeWordName   = 85.0;
        private const double ScoreStartsWithName  = 65.0;
        private const double ScorePhraseInName    = 45.0;
        private const double ScoreTermCoverage    = 40.0;  // max bonus for multi-term coverage in Names
        private const double ScoreExactTag        = 25.0;
        private const double ScoreTagCoverage     = 15.0;  // max bonus for multi-term coverage in Tags
        private const double ScoreDescPhrase      = 8.0;
        private const double ScoreDescCoverage    = 4.0;   // max bonus for multi-term coverage in Descriptions
        private const double ScoreNameLenPenalty  = 0.01;
        private const double AllTermsMultiplier   = 1.5;   // bonus when every query term is present

        // Vector scoring
        private const double VectorDistanceThreshold = 0.35;
        private const double VectorBoost             = 1.0;   // applied after normalization to [0,1]

        // Hybrid blending — how much each source contributes to the final score.
        // Both are normalised to [0, 1] before being combined, so these are true weights.
        private const double HybridKeywordWeight = 0.55;
        private const double HybridVectorWeight  = 0.45;

        private readonly ILogger<KeywordRepositoryService>  _log;
        private readonly IDatRepositoryService              _datRepository;
        private readonly IAceRepositoryService              _aceRepository;
        private readonly System.Net.Http.HttpClient         _httpClient;

        private string _repositoryRoot = string.Empty;
        private string _modelsRoot     = string.Empty;
        private List<ManagedKeywordDb> _managedKeywordDbs = [];

        private readonly string              _registryFileName = "managed_keywords.json";
        private readonly SemaphoreSlim       _dbLock           = new(1, 1);

        public event EventHandler<IKeywordRepositoryService.KeywordGenerationProgress>? GlobalProgress;
        public string RepositoryRoot => _repositoryRoot;

        public KeywordRepositoryService(
            ILogger<KeywordRepositoryService> log,
            IDatRepositoryService datRepository,
            IAceRepositoryService aceRepository,
            System.Net.Http.HttpClient httpClient)
        {
            _log           = log;
            _datRepository = datRepository;
            _aceRepository = aceRepository;
            _httpClient    = httpClient;
        }

        public void SetRepositoryRoot(string rootDirectory) {
            _repositoryRoot = rootDirectory;
            if (!Directory.Exists(_repositoryRoot)) Directory.CreateDirectory(_repositoryRoot);
            LoadRegistry();
        }

        public void SetModelsRoot(string modelsDirectory) {
            _modelsRoot = modelsDirectory;
            if (!Directory.Exists(_modelsRoot)) Directory.CreateDirectory(_modelsRoot);
        }

        private void LoadRegistry() {
            var path = Path.Combine(_repositoryRoot, _registryFileName);
            if (!File.Exists(path)) return;
            try {
                var json = File.ReadAllText(path);
                _managedKeywordDbs = JsonSerializer.Deserialize(json, KeywordSourceGenerationContext.Default.ListManagedKeywordDb) ?? [];
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to load managed keyword registry");
                _managedKeywordDbs = [];
            }
        }

        private void SaveRegistry() {
            var path    = Path.Combine(_repositoryRoot, _registryFileName);
            var tmpPath = path + ".tmp";
            try {
                var json = JsonSerializer.Serialize(_managedKeywordDbs, KeywordSourceGenerationContext.Default.ListManagedKeywordDb);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to save managed keyword registry");
            }
        }

        public IReadOnlyList<ManagedKeywordDb> GetManagedKeywordDbs() => _managedKeywordDbs.AsReadOnly();

        public ManagedKeywordDb? GetManagedKeywordDb(Guid datId, Guid aceId) =>
            _managedKeywordDbs.FirstOrDefault(d => d.DatSetId == datId && d.AceDbId == aceId);

        public string GetKeywordDbPath(Guid datId, Guid aceId) =>
            Path.Combine(_repositoryRoot, $"keywords_{datId}_{aceId}.db");

        public bool AreKeywordsValid(Guid datId, Guid aceId) {
            var db = GetManagedKeywordDb(datId, aceId);
            if (db == null) {
                _log.LogWarning("Keywords not valid for {DatId}/{AceId}: Not found in registry", datId, aceId);
                return false;
            }
            if (db.GeneratorVersion != IKeywordRepositoryService.CurrentGeneratorVersion) {
                _log.LogWarning("Keywords not valid for {DatId}/{AceId}: Version mismatch (found {V}, expected {E})", datId, aceId, db.GeneratorVersion, IKeywordRepositoryService.CurrentGeneratorVersion);
                return false;
            }
            if (!db.IsComplete) {
                _log.LogWarning("Keywords not valid for {DatId}/{AceId}: Incomplete (Kw:{K} Name:{N} Desc:{D})", datId, aceId, db.KeywordProgress, db.NameEmbeddingProgress, db.DescEmbeddingProgress);
                return false;
            }
            var exists = File.Exists(GetKeywordDbPath(datId, aceId));
            if (!exists) _log.LogWarning("Keywords not valid for {DatId}/{AceId}: DB file not found", datId, aceId);
            return exists;
        }

        public bool CanSearchKeywords(Guid datId, Guid aceId) {
            var db = GetManagedKeywordDb(datId, aceId);
            return db != null
                && db.GeneratorVersion == IKeywordRepositoryService.CurrentGeneratorVersion
                && db.KeywordProgress >= 1f
                && File.Exists(GetKeywordDbPath(datId, aceId));
        }

        public bool IsEmbeddingSearchActive(Guid datId, Guid aceId) {
            var db = GetManagedKeywordDb(datId, aceId);
            return db != null
                && db.NameEmbeddingProgress >= 1f
                && EmbeddingModelExists();
        }

        public async Task<Result<ManagedKeywordDb>> GenerateAsync(Guid datId, Guid aceId, bool forceRegenerate, CancellationToken ct) {
            _log.LogInformation("Generating keyword database for DatSet {DatId} / AceDb {AceId}...", datId, aceId);
            try {
                var acePath = _aceRepository.GetAceDbPath(aceId, string.Empty);
                if (!File.Exists(acePath))
                    return Result<ManagedKeywordDb>.Failure($"ACE database not found at {acePath}", "ACE_DB_NOT_FOUND");

                var targetPath      = GetKeywordDbPath(datId, aceId);
                var connectionString = $"Data Source={targetPath}";
                var metadata        = GetOrResetMetadata(datId, aceId, targetPath, forceRegenerate);

                // Phase 1 – keyword extraction
                if (metadata.KeywordProgress < 1f || !await TableExistsAsync(connectionString, "SetupKeywords", ct)) {
                    ReportProgress("Extracting keywords from ACE database...", 0.1f, 0f, 0f);

                    var sceneryIds    = await ExtractScenerySetupIdsAsync(datId, ct);
                    var setupKeywords = await ExtractAceKeywordsAsync(acePath, sceneryIds, ct);

                    await SaveKeywordsAsync(connectionString, setupKeywords, ct);
                    metadata.KeywordProgress = 1f;
                    SaveRegistry();
                }

                // Phase 2 – vector store init
                await ApplyWalModeAsync(connectionString, ct);
                await EnsureVectorCollectionAsync(connectionString, ct);

                // Phase 3 – embedding generation
                var modelDownload = await EnsureModelDownloadedAsync(ct);
                if (modelDownload.IsFailure)
                    return Result<ManagedKeywordDb>.Failure($"Embedding model unavailable: {modelDownload.Error.Message}", "MODEL_DOWNLOAD_FAILED");

                if (metadata.NameEmbeddingProgress < 1f || metadata.DescEmbeddingProgress < 1f) {
                    var pendingSetups = await LoadPendingSetupsAsync(connectionString, ct);
                    await GenerateEmbeddingsAsync(connectionString, pendingSetups, metadata, ct);
                    metadata.NameEmbeddingProgress = 1f;
                    metadata.DescEmbeddingProgress = 1f;
                    SaveRegistry();
                }

                metadata.LastGenerated = DateTime.UtcNow;
                SaveRegistry();
                ReportProgress("Keyword generation complete.", 1f, 1f, 1f);
                _log.LogInformation("Successfully generated keyword database for {DatId}/{AceId}.", datId, aceId);
                return Result<ManagedKeywordDb>.Success(metadata);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to generate keyword database");
                return Result<ManagedKeywordDb>.Failure(ex.Message, "GENERATION_FAILED");
            }
        }

        public async Task<Result<Unit>> DeleteAsync(Guid datId, Guid aceId, CancellationToken ct) {
            var path = GetKeywordDbPath(datId, aceId);
            if (File.Exists(path)) {
                try { File.Delete(path); }
                catch (Exception ex) {
                    _log.LogError(ex, "Failed to delete keyword database file");
                    return Result<Unit>.Failure($"Failed to delete file: {ex.Message}", "DELETE_FAILED");
                }
            }
            var db = GetManagedKeywordDb(datId, aceId);
            if (db != null) {
                _managedKeywordDbs.Remove(db);
                SaveRegistry();
            }
            return Result<Unit>.Success(Unit.Value);
        }

        public async Task<(string Names, string Tags, string Descriptions)?> GetKeywordsForSetupAsync(Guid datId, Guid aceId, uint setupId, CancellationToken ct) {
            var path = GetKeywordDbPath(datId, aceId);
            if (!File.Exists(path)) return null;
            try {
                await using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Names, Tags, Descriptions FROM SetupKeywords WHERE SetupId = @id";
                cmd.Parameters.AddWithValue("@id", setupId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                    return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to retrieve keywords for setup {SetupId}", setupId);
            }
            return null;
        }

        public async Task<List<uint>> SearchSetupsAsync(Guid datId, Guid aceId, string query, SearchType searchType, CancellationToken ct) {
            if (string.IsNullOrWhiteSpace(query)) return [];

            var path = GetKeywordDbPath(datId, aceId);
            if (!File.Exists(path)) return [];

            query = query.Trim();
            var queryTerms       = TokenizeQuery(query);
            var keywordScores    = new Dictionary<uint, double>();
            var vectorScores     = new Dictionary<uint, double>();
            var connectionString = $"Data Source={path}";
            bool canVector       = IsEmbeddingSearchActive(datId, aceId);

            bool doKeyword  = searchType == SearchType.Keyword  || searchType == SearchType.Hybrid || !canVector;
            bool doSemantic = (searchType == SearchType.Semantic || searchType == SearchType.Hybrid) && canVector;

            try {
                if (doKeyword)
                    await RunKeywordSearchAsync(connectionString, query, queryTerms, keywordScores, ct);

                if (doSemantic)
                    await RunSemanticSearchAsync(connectionString, query, vectorScores, ct);

                var combined = MergeScores(keywordScores, vectorScores, doKeyword, doSemantic);

                if (_log.IsEnabled(LogLevel.Information) && combined.Count > 0)
                    await LogTopMatchesAsync(datId, aceId, combined, query, searchType, ct);

                return combined.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            }
            catch (OperationCanceledException) {
                return [];
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to search keywords for query '{Query}'", query);
                return [];
            }
        }

        private ManagedKeywordDb GetOrResetMetadata(Guid datId, Guid aceId, string targetPath, bool forceRegenerate) {
            var existing = GetManagedKeywordDb(datId, aceId);
            if (!forceRegenerate && existing != null && existing.GeneratorVersion == IKeywordRepositoryService.CurrentGeneratorVersion)
                return existing;

            if (File.Exists(targetPath)) File.Delete(targetPath);
            _managedKeywordDbs.RemoveAll(d => d.DatSetId == datId && d.AceDbId == aceId);

            var metadata = new ManagedKeywordDb {
                DatSetId         = datId,
                AceDbId          = aceId,
                GeneratorVersion = IKeywordRepositoryService.CurrentGeneratorVersion,
                LastGenerated    = DateTime.UtcNow,
                KeywordProgress  = 0,
                NameEmbeddingProgress = 0,
                DescEmbeddingProgress = 0
            };
            _managedKeywordDbs.Add(metadata);
            SaveRegistry();
            return metadata;
        }

        private async Task<HashSet<uint>> ExtractScenerySetupIdsAsync(Guid datId, CancellationToken ct) {
            var ids        = new HashSet<uint>();
            var datSetPath = _datRepository.GetDatSetPath(datId, string.Empty);
            if (!Directory.Exists(datSetPath)) return ids;

            using var datReader = _datRepository.GetDatReaderWriter(datSetPath);

            foreach (var sceneId in datReader.Portal.GetAllIdsOfType<Scene>()) {
                if (datReader.Portal.TryGet<Scene>(sceneId, out var scene))
                    foreach (var obj in scene.Objects)
                        if ((obj.ObjectId & SetupIdMask) == SetupIdPrefix)
                            ids.Add(obj.ObjectId);
            }

            foreach (var cellRegion in datReader.CellRegions.Values) {
                foreach (var lbInfoId in cellRegion.GetAllIdsOfType<LandBlockInfo>()) {
                    if (cellRegion.TryGet<LandBlockInfo>(lbInfoId, out var lbInfo))
                        foreach (var obj in lbInfo.Objects)
                            if ((obj.Id & SetupIdMask) == SetupIdPrefix)
                                ids.Add(obj.Id);
                }
            }

            return ids;
        }

        private async Task<Dictionary<uint, SetupKeywordData>> ExtractAceKeywordsAsync(string acePath, HashSet<uint> sceneryIds, CancellationToken ct) {
            var result = new Dictionary<uint, SetupKeywordData>();

            // Seed scenery entries
            foreach (var id in sceneryIds) {
                var data = new SetupKeywordData();
                data.Tags.Add("scenery");
                result[id] = data;
            }

            var options = new DbContextOptionsBuilder<WorldDbContext>()
                .UseSqlite($"Data Source={acePath}")
                .Options;

            using var context = new WorldDbContext(options);
            var rows = await context.Weenie
                .Where(w => !w.WeeniePropertiesFloat.Any(wpf => wpf.Type == (ushort)PropertyFloat.GeneratorRadius))
                .SelectMany(
                    w => w.WeeniePropertiesDID.Where(wpd => wpd.Type == (ushort)PropertyDataId.Setup),
                    (w, wpd) => new {
                        SetupId = wpd.Value,
                        Type    = (WeenieType)w.Type,
                        Strings = w.WeeniePropertiesString.Select(s => new { s.Type, s.Value }),
                        Ints    = w.WeeniePropertiesInt
                            .Where(i => i.Type == (ushort)PropertyInt.CreatureType || i.Type == (ushort)PropertyInt.ItemType)
                            .Select(i => new { i.Type, i.Value })
                    })
                .ToListAsync(ct);

            foreach (var row in rows) {
                if (!result.TryGetValue(row.SetupId, out var kw)) kw = new SetupKeywordData();

                kw = kw with { WeenieType = row.Type.ToString() };
                kw.Tags.Add(row.Type.ToString());
                if (sceneryIds.Contains(row.SetupId)) kw.Tags.Add("scenery");

                foreach (var i in row.Ints) {
                    if (i.Type == (ushort)PropertyInt.CreatureType) {
                        var name = Enum.GetName(typeof(CreatureType), (uint)i.Value);
                        if (name != null) { kw = kw with { CreatureType = name }; kw.Tags.Add(name); }
                    }
                    else if (i.Type == (ushort)PropertyInt.ItemType) {
                        var name = Enum.GetName(typeof(ItemType), (uint)i.Value);
                        if (name != null) { kw = kw with { ItemType = name }; kw.Tags.Add(name); }
                    }
                }

                foreach (var s in row.Strings) {
                    if (string.IsNullOrWhiteSpace(s.Value)) continue;
                    if      (s.Type == (ushort)PropertyString.Name)      kw.Names.Add(s.Value);
                    else if (s.Type == (ushort)PropertyString.ShortDesc ||
                             s.Type == (ushort)PropertyString.LongDesc)  kw.Descriptions.Add(s.Value);
                }

                result[row.SetupId] = kw;
            }

            _log.LogTrace("Extracted keywords for {Count} setups.", result.Count);
            return result;
        }

        private async Task SaveKeywordsAsync(string connectionString, Dictionary<uint, SetupKeywordData> keywords, CancellationToken ct) {
            using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS SetupKeywords (SetupId INTEGER PRIMARY KEY, Names TEXT, Tags TEXT, Descriptions TEXT)";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT OR REPLACE INTO SetupKeywords (SetupId, Names, Tags, Descriptions) VALUES (@id, @names, @tags, @desc)";
            var pId    = insert.Parameters.Add("@id",    SqliteType.Integer);
            var pNames = insert.Parameters.Add("@names", SqliteType.Text);
            var pTags  = insert.Parameters.Add("@tags",  SqliteType.Text);
            var pDesc  = insert.Parameters.Add("@desc",  SqliteType.Text);

            int count = 0, total = keywords.Count;
            foreach (var (setupId, kw) in keywords) {
                pId.Value    = setupId;
                pNames.Value = string.Join(" ", kw.Names);
                pTags.Value  = string.Join(" ", kw.Tags);
                pDesc.Value  = BuildDescriptionText(kw);
                await insert.ExecuteNonQueryAsync(ct);

                count++;
                if (count % 100 == 0)
                    ReportProgress($"Saving keywords ({count}/{total})...", (float)count / total, 0f, 0f);
            }

            await tx.CommitAsync(ct);
            await conn.CloseAsync();
        }

        private static string BuildDescriptionText(SetupKeywordData kw) {
            var sb = new StringBuilder();
            var category = kw.Tags.Contains("scenery") ? "Scenery" : kw.WeenieType;
            if (!string.IsNullOrEmpty(category))  sb.AppendLine($"Category: {category}");
            if (!string.IsNullOrEmpty(kw.CreatureType)) sb.AppendLine($"CreatureType: {kw.CreatureType}");
            if (!string.IsNullOrEmpty(kw.ItemType))     sb.AppendLine($"ItemType: {kw.ItemType}");
            if (kw.Descriptions.Count > 0)              sb.AppendLine("Description: " + string.Join(" ", kw.Descriptions));
            return sb.ToString().Trim();
        }

        private static async Task EnsureVectorCollectionAsync(string connectionString, CancellationToken ct) {
            var store      = new SqliteVectorStore(connectionString);
            var collection = store.GetCollection<int, KeywordVectorRecord>("setup_vectors");
            await collection.EnsureCollectionExistsAsync(ct);
        }

        private async Task GenerateEmbeddingsAsync(
            string connectionString,
            List<(uint SetupId, string Names, string Tags, string Descriptions)> pending,
            ManagedKeywordDb metadata,
            CancellationToken ct)
        {
            var store      = new SqliteVectorStore(connectionString);
            var collection = store.GetCollection<int, KeywordVectorRecord>("setup_vectors");
            var embeddingGenerator = BuildEmbeddingGenerator();
            int total      = pending.Count;
            int skipCount  = (int)(metadata.NameEmbeddingProgress * total);
            int processed  = skipCount;
            int parallelism = Math.Max(1, System.Environment.ProcessorCount / 2);

            var batches = Enumerable
                .Range(0, (total - skipCount + EmbeddingBatch - 1) / EmbeddingBatch)
                .Select(i => pending.Skip(skipCount + i * EmbeddingBatch).Take(EmbeddingBatch).ToList());

            await Parallel.ForEachAsync(batches, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct }, async (batch, bct) => {
                var inputs = batch.Select(b => BuildCombinedEmbeddingText(b.Names, b.Tags, b.Descriptions)).ToList();
                var embeddings = await embeddingGenerator.GenerateAsync(inputs, cancellationToken: bct);

                await _dbLock.WaitAsync(bct);
                try {
                    for (int j = 0; j < batch.Count; j++) {
                        await collection.UpsertAsync(new KeywordVectorRecord {
                            SetupId   = (int)batch[j].SetupId,
                            Names     = batch[j].Names,
                            Tags      = batch[j].Tags,
                            Descriptions = batch[j].Descriptions,
                            Embedding = embeddings[j].Vector
                        }, cancellationToken: bct);
                    }

                    processed += batch.Count;
                    var prog = (float)processed / total;
                    metadata.NameEmbeddingProgress = prog;
                    metadata.DescEmbeddingProgress = prog;
                    ReportProgress($"Generating embeddings ({processed}/{total})...", 1f, prog, prog);
                    if (processed % (EmbeddingBatch * 5) < EmbeddingBatch) SaveRegistry();
                }
                finally {
                    _dbLock.Release();
                }
            });
        }

        /// <summary>
        /// Builds the text that gets embedded for a single game object.
        /// Name is placed first for positional prominence; tags and description follow as context.
        /// </summary>
        private static string BuildCombinedEmbeddingText(string names, string tags, string descriptions) {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(names))        parts.Add(names.Trim());
            if (!string.IsNullOrWhiteSpace(tags))         parts.Add(tags.Trim());
            if (!string.IsNullOrWhiteSpace(descriptions)) parts.Add(descriptions.Trim());
            return parts.Count > 0 ? string.Join(" | ", parts) : " ";
        }

        // ── Search helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Splits the query into distinct lower-case tokens for multi-term scoring.
        /// The full phrase is always kept as the first element when it has >1 word.
        /// </summary>
        private static string[] TokenizeQuery(string query) {
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Select(t => t.ToLowerInvariant())
                             .Distinct()
                             .ToArray();
            return terms;
        }

        /// <summary>
        /// Pulls candidate rows from SQLite (matching ANY query term) then scores in C#.
        /// </summary>
        private async Task RunKeywordSearchAsync(
            string connectionString,
            string query,
            string[] queryTerms,
            Dictionary<uint, double> scores,
            CancellationToken ct)
        {
            _log.LogTrace("Keyword search for '{Query}'", query);

            // Build a broad SQL filter: match the phrase OR any individual term
            var sb     = new StringBuilder("SELECT SetupId, Names, Tags, Descriptions FROM SetupKeywords WHERE ");
            var clauses = new List<string>();
            clauses.Add("Names COLLATE NOCASE LIKE @phrase OR Tags COLLATE NOCASE LIKE @phrase OR Descriptions COLLATE NOCASE LIKE @phrase");
            for (int i = 0; i < queryTerms.Length; i++)
                clauses.Add($"Names COLLATE NOCASE LIKE @term{i} OR Tags COLLATE NOCASE LIKE @term{i}");

            sb.Append(string.Join(" OR ", clauses));

            await using var conn = new SqliteConnection($"{connectionString};Mode=ReadOnly");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("@phrase", $"%{query}%");
            for (int i = 0; i < queryTerms.Length; i++)
                cmd.Parameters.AddWithValue($"@term{i}", $"%{queryTerms[i]}%");

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) {
                var setupId = (uint)reader.GetInt64(0);
                var names   = reader.GetString(1);
                var tags    = reader.GetString(2);
                var descs   = reader.GetString(3);
                scores[setupId] = ComputeKeywordScore(names, tags, descs, query, queryTerms);
            }
        }

        /// <summary>
        /// Computes a keyword relevance score for a single row in C#.
        /// Handles both exact/phrase matching and per-term coverage for multi-word queries.
        /// </summary>
        private static double ComputeKeywordScore(string names, string tags, string descriptions, string query, string[] terms) {
            double score = 0;

            // ── Name scoring ──────────────────────────────────────────────────────
            if (names.Equals(query, StringComparison.OrdinalIgnoreCase)) {
                score += ScoreExactName;
            }
            else if (IsWholeWordMatch(names, query)) {
                score += ScoreWholeWordName - names.Length * ScoreNameLenPenalty;
            }
            else if (names.StartsWith(query, StringComparison.OrdinalIgnoreCase)) {
                score += ScoreStartsWithName - names.Length * ScoreNameLenPenalty;
            }
            else if (names.Contains(query, StringComparison.OrdinalIgnoreCase)) {
                score += ScorePhraseInName - names.Length * ScoreNameLenPenalty;
            }

            // Multi-term coverage in names
            if (terms.Length > 1) {
                int hits = terms.Count(t => names.Contains(t, StringComparison.OrdinalIgnoreCase));
                score += (double)hits / terms.Length * ScoreTermCoverage;
                if (hits == terms.Length) score *= AllTermsMultiplier;
            }

            // ── Tag scoring ───────────────────────────────────────────────────────
            if (tags.Contains(query, StringComparison.OrdinalIgnoreCase))
                score += ScoreExactTag;
            if (terms.Length > 1) {
                int hits = terms.Count(t => tags.Contains(t, StringComparison.OrdinalIgnoreCase));
                score += (double)hits / terms.Length * ScoreTagCoverage;
            }

            // ── Description scoring ───────────────────────────────────────────────
            if (descriptions.Contains(query, StringComparison.OrdinalIgnoreCase))
                score += ScoreDescPhrase;
            if (terms.Length > 1) {
                int hits = terms.Count(t => descriptions.Contains(t, StringComparison.OrdinalIgnoreCase));
                score += (double)hits / terms.Length * ScoreDescCoverage;
            }

            return score;
        }

        /// <summary>Returns true if <paramref name="text"/> contains <paramref name="word"/> as a whole word.</summary>
        private static bool IsWholeWordMatch(string text, string word) {
            int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            bool leftBound  = idx == 0                          || !char.IsLetterOrDigit(text[idx - 1]);
            bool rightBound = idx + word.Length == text.Length  || !char.IsLetterOrDigit(text[idx + word.Length]);
            return leftBound && rightBound;
        }

        private async Task RunSemanticSearchAsync(
            string connectionString,
            string query,
            Dictionary<uint, double> scores,
            CancellationToken ct)
        {
            _log.LogTrace("Semantic search for '{Query}'", query);
            var embeddingGenerator = BuildEmbeddingGenerator();
            var queryEmbeddings    = await embeddingGenerator.GenerateAsync(new[] { query }, cancellationToken: ct);
            var vector             = queryEmbeddings[0].Vector;

            var vectorStore = new SqliteVectorStore(connectionString);
            var collection  = vectorStore.GetCollection<int, KeywordVectorRecord>("setup_vectors");

            await foreach (var result in collection.SearchAsync(vector, VectorTopK, cancellationToken: ct)) {
                double distance = result.Score ?? 1.0;
                if (distance > VectorDistanceThreshold) continue;
                var setupId = (uint)result.Record.SetupId;
                var boost   = (1.0 - distance) * VectorBoost;
                scores[setupId] = scores.TryGetValue(setupId, out var ex) ? ex + boost : boost;
                _log.LogTrace("Vec match '{Q}': 0x{Id:X8} dist={D:F3} +{B:F3}", query, setupId, distance, boost);
            }
        }

        /// <summary>
        /// Merges keyword and vector score dictionaries into a single normalised score.
        /// Each source is independently normalised to [0, 1] before being blended
        /// </summary>
        private static Dictionary<uint, double> MergeScores(
            Dictionary<uint, double> keywordScores,
            Dictionary<uint, double> vectorScores,
            bool hasKeyword,
            bool hasVector)
        {
            if (!hasVector) return keywordScores;
            if (!hasKeyword) return vectorScores;

            double kwMax  = keywordScores.Count > 0 ? keywordScores.Values.Max() : 1.0;
            double vecMax = vectorScores.Count > 0  ? vectorScores.Values.Max()  : 1.0;
            if (kwMax  == 0) kwMax  = 1.0;
            if (vecMax == 0) vecMax = 1.0;

            var all = new HashSet<uint>(keywordScores.Keys);
            all.UnionWith(vectorScores.Keys);

            var merged = new Dictionary<uint, double>(all.Count);
            foreach (var id in all) {
                double kw  = keywordScores.TryGetValue(id, out var k) ? k / kwMax  : 0.0;
                double vec = vectorScores.TryGetValue(id,  out var v) ? v / vecMax : 0.0;
                merged[id] = kw * HybridKeywordWeight + vec * HybridVectorWeight;
            }
            return merged;
        }

        private bool EmbeddingModelExists() {
            var (modelPath, vocabPath) = GetModelPaths();
            return File.Exists(modelPath) && File.Exists(vocabPath);
        }

        private (string modelPath, string vocabPath) GetModelPaths() {
            var dir = Path.Combine(_modelsRoot, "bge-micro-v2");
            return (Path.Combine(dir, "model.onnx"), Path.Combine(dir, "vocab.txt"));
        }

        #pragma warning disable SKEXP0070
        private IEmbeddingGenerator<string, Embedding<float>> BuildEmbeddingGenerator() {
            var (modelPath, vocabPath) = GetModelPaths();
            var builder = Kernel.CreateBuilder();
            builder.AddBertOnnxEmbeddingGenerator(modelPath, vocabPath, new BertOnnxOptions {
                NormalizeEmbeddings = true,
                MaximumTokens       = 512,
                CaseSensitive       = false
            });
            return builder.Build().GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        }
        #pragma warning restore SKEXP0070

        private async Task<Result<Unit>> EnsureModelDownloadedAsync(CancellationToken ct) {
            var (modelPath, vocabPath) = GetModelPaths();
            var dir = Path.GetDirectoryName(modelPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // TODO: allow configurable models, either from huggingface or openapi compatible endpoints for generating embeddings.
            const string modelUrl = "https://huggingface.co/TaylorAI/bge-micro-v2/resolve/main/onnx/model.onnx";
            const string vocabUrl = "https://huggingface.co/TaylorAI/bge-micro-v2/resolve/main/vocab.txt";

            try {
                if (!File.Exists(modelPath)) await DownloadFileAsync(modelUrl, modelPath, "embedding model", ct);
                if (!File.Exists(vocabPath)) await DownloadFileAsync(vocabUrl, vocabPath, "model vocabulary", ct);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to download embedding model");
                return Result<Unit>.Failure($"Failed to download embedding model: {ex.Message}", "DOWNLOAD_FAILED");
            }
        }

        private async Task DownloadFileAsync(string url, string destPath, string label, CancellationToken ct) {
            _log.LogTrace("Downloading {Label} from {Url}...", label, url);
            ReportProgress($"Downloading {label}...", 1f, 0f, 0f);
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs, ct);
        }

        private static async Task<bool> TableExistsAsync(string connectionString, string tableName, CancellationToken ct) {
            using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name";
            cmd.Parameters.AddWithValue("@name", tableName);
            return await cmd.ExecuteScalarAsync(ct) != null;
        }

        private static async Task ApplyWalModeAsync(string connectionString, CancellationToken ct) {
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<List<(uint SetupId, string Names, string Tags, string Descriptions)>> LoadPendingSetupsAsync(string connectionString, CancellationToken ct) {
            var result = new List<(uint, string, string, string)>();
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SetupId, Names, Tags, Descriptions FROM SetupKeywords WHERE (Names IS NOT NULL AND Names != '') OR (Descriptions IS NOT NULL AND Descriptions != '')";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(((uint)reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            return result;
        }

        private void ReportProgress(string message, float keyword, float name, float desc) =>
            GlobalProgress?.Invoke(this, new IKeywordRepositoryService.KeywordGenerationProgress(message, keyword, name, desc));

        private async Task LogTopMatchesAsync(Guid datId, Guid aceId, Dictionary<uint, double> combined, string query, SearchType searchType, CancellationToken ct) {
            _log.LogTrace("Search '{Query}' ({Type}): {Count} matches.", query, searchType, combined.Count);
            foreach (var (id, score) in combined.OrderByDescending(kv => kv.Value).Take(10)) {
                var kw = await GetKeywordsForSetupAsync(datId, aceId, id, ct);
                _log.LogTrace("  0x{Id:X8} score={Score:F3} name='{Name}'", id, score, kw?.Names ?? "?");
            }
        }

        [JsonSourceGenerationOptions(WriteIndented = true)]
        [JsonSerializable(typeof(ManagedKeywordDb))]
        [JsonSerializable(typeof(List<ManagedKeywordDb>))]
        internal partial class KeywordSourceGenerationContext : JsonSerializerContext { }
    }
}