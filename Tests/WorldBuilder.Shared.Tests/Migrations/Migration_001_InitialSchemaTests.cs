using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Migrations;

namespace WorldBuilder.Shared.Tests.Migrations {
    public class Migration_001_InitialSchemaTests {
        [Fact]
        public void Migration_001_RunsSuccessfully() {
            // Arrange
            using var db = new TestDatabase();

            var services = new ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(rb => rb
                    .AddSQLite()
                    .WithGlobalConnectionString(db.ConnectionString)
                    .ScanIn(typeof(Migration_001_InitialSchema).Assembly).For.Migrations())
                .BuildServiceProvider();

            using var scope = services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            var tables = GetTableNames(connection);
            Assert.Contains("Documents", tables);
            Assert.Contains("Events", tables);

            var indexes = GetIndexNames(connection);
            Assert.Contains("idx_events_userid", indexes);
        }

        private static List<string> GetTableNames(SqliteConnection conn) {
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            var reader = cmd.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names;
        }

        private static List<string> GetIndexNames(SqliteConnection conn) {
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';";
            var reader = cmd.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names;
        }
    }
}