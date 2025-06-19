using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared
{
    public class DocumentManager : ObservableObject, IDisposable {
        private readonly string connectionString;
        private readonly object dbLock = new object();
        private readonly Dictionary<string, BaseDocument> documents = new Dictionary<string, BaseDocument>();

        public ObservableCollection<BaseDocument> Documents { get; } = new ObservableCollection<BaseDocument>();
        public Project Project { get; private set; }

        public DocumentManager(Project project) {
            Project = project;

            var dbPath = Path.Combine(Path.GetDirectoryName(project.FilePath), "documents.db");
            connectionString = $"Data Source={dbPath};Pooling=True";

            InitializeDatabase();
        }

        private void InitializeDatabase() {
            lock (dbLock) {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT NOT NULL,
                    Value TEXT NOT NULL,
                    PRIMARY KEY (Key)
                );

                CREATE TABLE IF NOT EXISTS Documents (
                    DocumentId TEXT NOT NULL,
                    LatestUpdateId TEXT NOT NULL,
                    DocumentBytes BLOB NOT NULL,
                    PRIMARY KEY (DocumentId)
                );

                CREATE TABLE IF NOT EXISTS Updates (
                    DocumentId TEXT NOT NULL,
                    UpdateId TEXT NOT NULL, 
                    Timestamp INTEGER NOT NULL,
                    UpdateBytes BLOB NOT NULL,
                    AppliedStatus INTEGER NOT NULL DEFAULT 0,
                    SyncedStatus INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (DocumentId, UpdateId)
                );
                ";
                command.ExecuteNonQuery();
            }
        }

        public void Update() {
            foreach (var document in Documents.ToArray()) {
                document.Update();
            }
        }

        public T CreateDocument<T>(string name) where T : BaseDocument, new() {
            if (documents.ContainsKey(name))
                throw new InvalidOperationException($"Document {name} already exists");

            var document = new T();
            document.Initialize(name, this);
            documents[name] = document;
            Documents.Add(document);

            return document;
        }

        internal string StoreUpdate(string documentId, byte[] update, bool isRemote = false) {
            var updateId = Guid.NewGuid().ToString();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            ExecuteWithRetry(() =>
            {
                lock (dbLock) {
                    using var connection = new SqliteConnection(connectionString);
                    connection.Open();
                    using var transaction = connection.BeginTransaction();
                    var command = connection.CreateCommand();
                    command.CommandText = "INSERT INTO Updates (DocumentId, UpdateId, Timestamp, UpdateBytes, AppliedStatus, SyncedStatus) VALUES ($docId, $id, $ts, $bytes, 0)";
                    command.Parameters.AddWithValue("$docId", documentId);
                    command.Parameters.AddWithValue("$id", updateId);
                    command.Parameters.AddWithValue("$ts", timestamp);
                    command.Parameters.AddWithValue("$bytes", update);
                    command.ExecuteNonQuery();
                    transaction.Commit();
                }
            });

            return updateId;
        }

        private void ExecuteWithRetry(Action action, int maxRetries = 3, int delayMs = 100) {
            for (int attempt = 1; attempt <= maxRetries; attempt++) {
                try {
                    action();
                    return;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < maxRetries) {
                    Console.WriteLine($"Database busy, retrying ({attempt}/{maxRetries})...");
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error executing database operation: {ex}");
                    throw;
                }
            }
            throw new SqliteException("Database locked after maximum retries", 5);
        }

        public void Dispose() {
            foreach (var doc in documents.Values) {
                doc.Dispose();
            }
            documents.Clear();
            Documents.Clear();
        }
    }
}
