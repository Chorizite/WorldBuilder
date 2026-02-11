using Microsoft.Data.Sqlite;
using System;

namespace WorldBuilder.Shared.Tests {
    public class TestDatabase : IDisposable {
        private readonly SqliteConnection _keepAlive;
        public string ConnectionString { get; }

        public TestDatabase() {
            var dbName = Guid.NewGuid().ToString();
            ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(ConnectionString);
            _keepAlive.Open();
        }

        public void Dispose() => _keepAlive.Dispose();
    }
}