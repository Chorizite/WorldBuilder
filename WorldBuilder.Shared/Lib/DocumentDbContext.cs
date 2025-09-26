using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Lib {
    public class DocumentDbContext : DbContext {
        public DbSet<DBDocument> Documents { get; set; }
        public DbSet<DBDocumentUpdate> Updates { get; set; }

        public DocumentDbContext(DbContextOptions<DocumentDbContext> options) : base(options) {
            // Optimize for performance
            ChangeTracker.AutoDetectChangesEnabled = false;
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<DBDocument>(entity => {
                entity.HasKey(e => e.Id);

                // Optimize string columns
                entity.Property(e => e.Id)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(e => e.Type)
                    .HasMaxLength(100)
                    .IsRequired();

                // Configure Data column for large binary data (SQLite uses BLOB)
                entity.Property(e => e.Data)
                    .HasColumnType("BLOB");

                // Indexes
                entity.HasIndex(e => e.Id)
                    .IsUnique()
                    .HasDatabaseName("IX_Documents_Id");

                entity.HasIndex(e => e.Type)
                    .HasDatabaseName("IX_Documents_Type");

                entity.HasIndex(e => e.LastModified)
                    .HasDatabaseName("IX_Documents_LastModified");
            });

            modelBuilder.Entity<DBDocumentUpdate>(entity => {
                entity.HasKey(e => e.Id);

                // Optimize string columns
                entity.Property(e => e.DocumentId)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(e => e.Type)
                    .HasMaxLength(100)
                    .IsRequired();

                // Configure Data column for binary data (SQLite uses BLOB)
                entity.Property(e => e.Data)
                    .HasColumnType("BLOB");

                // Composite index for common query patterns
                entity.HasIndex(e => new { e.DocumentId, e.Timestamp })
                    .HasDatabaseName("IX_Updates_DocumentId_Timestamp");

                // Individual indexes for other query patterns
                entity.HasIndex(e => e.Timestamp)
                    .HasDatabaseName("IX_Updates_Timestamp");

                entity.HasIndex(e => e.ClientId)
                    .HasDatabaseName("IX_Updates_ClientId");

                // Relationship to Documents (for referential integrity)
                entity.HasOne<DBDocument>()
                    .WithMany()
                    .HasForeignKey(e => e.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_Updates_Documents");
            });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            if (!optionsBuilder.IsConfigured) {
                // SQLite-specific optimizations
                optionsBuilder.EnableServiceProviderCaching();
                optionsBuilder.EnableSensitiveDataLogging(false);
            }

            // SQLite-specific configuration
            if (optionsBuilder.Options.Extensions.Any(e => e.GetType().Name.Contains("Sqlite"))) {
                // Enable Write-Ahead Logging for better concurrency
                optionsBuilder.UseSqlite(options => {
                    options.CommandTimeout(30);
                });
            }
        }

        /// <summary>
        /// Initialize SQLite database with performance pragmas
        /// </summary>
        public async Task InitializeSqliteAsync() {
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite") {
                await Database.EnsureCreatedAsync();

                // Set SQLite pragmas for better performance
                await Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
                await Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;");
                await Database.ExecuteSqlRawAsync("PRAGMA cache_size = 10000;");
                await Database.ExecuteSqlRawAsync("PRAGMA temp_store = MEMORY;");
                await Database.ExecuteSqlRawAsync("PRAGMA mmap_size = 268435456;"); // 256MB
            }
        }

        /// <summary>
        /// Optimized bulk insert for updates with SQLite batch processing
        /// </summary>
        public async Task BulkInsertUpdatesAsync(IEnumerable<DBDocumentUpdate> updates, CancellationToken cancellationToken = default) {
            var updatesList = updates.ToList();
            if (!updatesList.Any()) return;

            var originalAutoDetect = ChangeTracker.AutoDetectChangesEnabled;
            var originalQueryTracking = ChangeTracker.QueryTrackingBehavior;

            try {
                ChangeTracker.AutoDetectChangesEnabled = false;
                ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

                // SQLite performs better with transactions for bulk operations
                using var transaction = await Database.BeginTransactionAsync(cancellationToken);

                // Process in batches to avoid SQLite limitations
                const int batchSize = 500;
                for (int i = 0; i < updatesList.Count; i += batchSize) {
                    var batch = updatesList.Skip(i).Take(batchSize);
                    Updates.AddRange(batch);
                    await SaveChangesAsync(cancellationToken);
                    ChangeTracker.Clear(); // Clear tracking for next batch
                }

                await transaction.CommitAsync(cancellationToken);
            }
            finally {
                ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
                ChangeTracker.QueryTrackingBehavior = originalQueryTracking;
            }
        }

        /// <summary>
        /// Cleanup old updates with SQLite-optimized deletion
        /// </summary>
        public async Task<int> CleanupOldUpdatesAsync(string documentId, int maxUpdates = 100, TimeSpan? maxAge = null, CancellationToken cancellationToken = default) {
            maxAge ??= TimeSpan.FromDays(30);
            var cutoffTime = DateTime.UtcNow - maxAge.Value;

            // SQLite-compatible SQL without TOP/CTE limitations
            // First, get IDs to keep (most recent maxUpdates)
            var idsToKeepSql = @"
                SELECT Id 
                FROM Updates 
                WHERE DocumentId = $documentId
                ORDER BY Timestamp DESC
                LIMIT $maxUpdates";

            var idsToKeep = await Updates
                .FromSqlRaw(idsToKeepSql,
                    new Microsoft.Data.Sqlite.SqliteParameter("$documentId", documentId),
                    new Microsoft.Data.Sqlite.SqliteParameter("$maxUpdates", maxUpdates))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            if (!idsToKeep.Any()) {
                // No updates to keep, delete all old ones
                return await Updates
                    .Where(u => u.DocumentId == documentId && u.Timestamp < cutoffTime)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // Delete old updates not in the keep list
            var deleteSql = @"
                DELETE FROM Updates 
                WHERE DocumentId = $documentId 
                    AND Timestamp < $cutoffTime
                    AND Id NOT IN (" + string.Join(",", idsToKeep.Select((_, i) => $"$id{i}")) + ")";

            var parameters = new List<Microsoft.Data.Sqlite.SqliteParameter> {
                new("$documentId", documentId),
                new("$cutoffTime", cutoffTime)
            };

            for (int i = 0; i < idsToKeep.Count; i++) {
                parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter($"$id{i}", idsToKeep[i]));
            }

            return await Database.ExecuteSqlRawAsync(deleteSql, parameters.ToArray(), cancellationToken);
        }

        /// <summary>
        /// Get document statistics efficiently
        /// </summary>
        public async Task<DocumentStats> GetDocumentStatsAsync(string documentId, CancellationToken cancellationToken = default) {
            var stats = await Updates
                .Where(u => u.DocumentId == documentId)
                .GroupBy(u => u.DocumentId)
                .Select(g => new DocumentStats {
                    DocumentId = g.Key,
                    UpdateCount = g.Count(),
                    LastUpdateTime = g.Max(u => u.Timestamp),
                    FirstUpdateTime = g.Min(u => u.Timestamp),
                    TotalDataSize = g.Sum(u => (long)u.Data.Length)
                })
                .FirstOrDefaultAsync(cancellationToken);

            return stats ?? new DocumentStats { DocumentId = documentId };
        }

        /// <summary>
        /// Get recent updates efficiently with pagination
        /// </summary>
        public async Task<List<DBDocumentUpdate>> GetRecentUpdatesAsync(string documentId, int take = 50, int skip = 0, CancellationToken cancellationToken = default) {
            return await Updates
                .AsNoTracking()
                .Where(u => u.DocumentId == documentId)
                .OrderByDescending(u => u.Timestamp)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// SQLite-specific vacuum operation for database maintenance
        /// </summary>
        public async Task VacuumDatabaseAsync() {
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite") {
                await Database.ExecuteSqlRawAsync("VACUUM;");
            }
        }

        /// <summary>
        /// Analyze database statistics for query optimization
        /// </summary>
        public async Task AnalyzeDatabaseAsync() {
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite") {
                await Database.ExecuteSqlRawAsync("ANALYZE;");
            }
        }
    }

    public class DocumentStats {
        public string DocumentId { get; set; } = string.Empty;
        public int UpdateCount { get; set; }
        public DateTime? LastUpdateTime { get; set; }
        public DateTime? FirstUpdateTime { get; set; }
        public long TotalDataSize { get; set; }
    }
}