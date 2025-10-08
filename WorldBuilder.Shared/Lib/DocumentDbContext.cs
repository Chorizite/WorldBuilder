using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Documents {
    public class DocumentDbContext : DbContext {
        private readonly ILogger<DocumentDbContext>? _logger;

        public DbSet<DBDocument> Documents { get; set; }
        public DbSet<DBDocumentUpdate> Updates { get; set; }
        public DbSet<DBSnapshot> Snapshots { get; set; }

        public DocumentDbContext() {
        
        }

        public DocumentDbContext(DbContextOptions<DocumentDbContext> options, ILogger<DocumentDbContext>? logger = null)
            : base(options) {
            _logger = logger;
            ChangeTracker.AutoDetectChangesEnabled = false;
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<DBDocument>(entity => {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(e => e.Type)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(e => e.Data)
                    .HasColumnType("BLOB");

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

                entity.Property(e => e.DocumentId)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(e => e.Type)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(e => e.Data)
                    .HasColumnType("BLOB");

                entity.HasIndex(e => new { e.DocumentId, e.Timestamp })
                    .HasDatabaseName("IX_Updates_DocumentId_Timestamp");

                entity.HasIndex(e => e.Timestamp)
                    .HasDatabaseName("IX_Updates_Timestamp");

                entity.HasIndex(e => e.ClientId)
                    .HasDatabaseName("IX_Updates_ClientId");

                entity.HasOne<DBDocument>()
                    .WithMany()
                    .HasForeignKey(e => e.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_Updates_Documents");
            });

            modelBuilder.Entity<DBSnapshot>(entity => {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.DocumentId)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(e => e.Data)
                    .HasColumnType("BLOB");

                entity.HasIndex(e => new { e.DocumentId, e.Timestamp })
                    .HasDatabaseName("IX_Snapshots_DocumentId_Timestamp");

                entity.HasOne<DBDocument>()
                    .WithMany()
                    .HasForeignKey(e => e.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_Snapshots_Documents");
            });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            if (!optionsBuilder.IsConfigured) {
                // Fallback connection string for design-time operations (e.g., migrations)
                string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorldBuilder", "worldbuilder.db");
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!); // Ensure directory exists
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
                optionsBuilder.EnableServiceProviderCaching();
                optionsBuilder.EnableSensitiveDataLogging(false);
            }

            if (optionsBuilder.Options.Extensions.Any(e => e.GetType().Name.Contains("Sqlite"))) {
                optionsBuilder.UseSqlite(options => {
                    options.CommandTimeout(30);
                });
            }
        }

        public async Task InitializeSqliteAsync(CancellationToken cancellationToken = default) {
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite") {
                try {
                    _logger?.LogInformation("Applying database migrations for DocumentDbContext...");
                    await Database.MigrateAsync(cancellationToken);
                    _logger?.LogInformation("Database migrations applied successfully.");

                    await Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
                    await Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;", cancellationToken);
                    await Database.ExecuteSqlRawAsync("PRAGMA cache_size = 10000;", cancellationToken);
                    await Database.ExecuteSqlRawAsync("PRAGMA temp_store = MEMORY;", cancellationToken);
                    await Database.ExecuteSqlRawAsync("PRAGMA mmap_size = 268435456;", cancellationToken);
                    _logger?.LogInformation("SQLite performance pragmas set successfully.");
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) {
                    _logger?.LogError(ex, "Failed to initialize SQLite database or apply migrations.");
                    throw new InvalidOperationException("Unable to initialize SQLite database. Please check the database file and permissions.", ex);
                }
                catch (Exception ex) {
                    _logger?.LogError(ex, "Unexpected error during SQLite database initialization.");
                    throw;
                }
            }
        }

        public async Task BulkInsertUpdatesAsync(IEnumerable<DBDocumentUpdate> updates, CancellationToken cancellationToken = default) {
            var updatesList = updates.ToList();
            if (!updatesList.Any()) return;

            var originalAutoDetect = ChangeTracker.AutoDetectChangesEnabled;
            var originalQueryTracking = ChangeTracker.QueryTrackingBehavior;

            try {
                ChangeTracker.AutoDetectChangesEnabled = false;
                ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

                using var transaction = await Database.BeginTransactionAsync(cancellationToken);

                const int batchSize = 500;
                for (int i = 0; i < updatesList.Count; i += batchSize) {
                    var batch = updatesList.Skip(i).Take(batchSize);
                    Updates.AddRange(batch);
                    await SaveChangesAsync(cancellationToken);
                    ChangeTracker.Clear();
                }

                await transaction.CommitAsync(cancellationToken);
                _logger?.LogInformation("Inserted {Count} updates in batch.", updatesList.Count);
            }
            finally {
                ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
                ChangeTracker.QueryTrackingBehavior = originalQueryTracking;
            }
        }
    }
}