using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Shared.Migrations;
using WorldBuilder.Shared.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Migrations {
    public class Migration_004_StringInstanceIdsTests {
        [Fact]
        public void Migration_004_SuccessfullyConvertsStaticAndEnvCellObjects() {
            // Arrange
            using var db = new TestDatabase();
            
            // 1. Run migrations up to 003
            var services = new ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(rb => rb
                    .AddSQLite()
                    .WithGlobalConnectionString(db.ConnectionString)
                    .ScanIn(typeof(Migration_001_InitialSchema).Assembly).For.Migrations())
                .BuildServiceProvider();

            using (var scope = services.CreateScope()) {
                var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
                runner.MigrateUp(3);
            }

            // 2. Seed legacy data
            using (var connection = new SqliteConnection(db.ConnectionString)) {
                connection.Open();
                
                // Legacy Constants (matching v3 behavior)
                const byte TypeStatic = 3;
                const byte TypeEnvCellStatic = 7;
                const byte StateOriginal = 0;
                const byte StateAdded = 1;
                const byte StateDeleted = 3;

                // Helper to encode legacy ID
                ulong Encode(byte type, byte state, uint context, ushort index) =>
                    ((ulong)type << 56) | ((ulong)state << 48) | ((ulong)context << 16) | (ulong)index;

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO LandscapeLayers (Id, RegionId, Name, SortOrder, IsExported, IsBase) 
                    VALUES ('base', 1, 'Base', 0, 1, 1);
                    
                    INSERT INTO StaticObjects (InstanceId, RegionId, LayerId, LandblockId, CellId, ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted)
                    VALUES 
                        (@staticOrig, 1, 'base', 32100, NULL, 100, 0,0,0, 1,0,0,0, 0),
                        (@staticTomb, 1, 'base', 32100, NULL, 100, 0,0,0, 1,0,0,0, 1),
                        (@envOrig, 1, 'base', 32100, 2103705909, 200, 0,0,0, 1,0,0,0, 0),
                        (@dbAdded, 1, 'base', 32100, NULL, 300, 10,10,10, 1,0,0,0, 0);
                ";
                
                cmd.Parameters.AddWithValue("@staticOrig", (long)Encode(TypeStatic, StateOriginal, 32100, 5));
                cmd.Parameters.AddWithValue("@staticTomb", (long)Encode(TypeStatic, StateDeleted, 32100, 5));
                cmd.Parameters.AddWithValue("@envOrig", (long)Encode(TypeEnvCellStatic, StateOriginal, 2103705909, 10));
                cmd.Parameters.AddWithValue("@dbAdded", (long)Encode(TypeStatic, StateAdded, 32100, 99));
                
                cmd.ExecuteNonQuery();
            }

            // 3. Run Migration 004
            using (var scope = services.CreateScope()) {
                var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
                runner.MigrateUp(4);
            }

            // Verify results
            using (var connection = new SqliteConnection(db.ConnectionString)) {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT InstanceId, IsDeleted, CellId FROM StaticObjects ORDER BY ModelId";
                using var reader = cmd.ExecuteReader();

                // Model 100 - StaticObject (Original was replaced by Tombstone because they share InstanceId + LayerId)
                Assert.True(reader.Read());
                Assert.Equal("dat:StaticObject:7D64FFFE:0005:0", reader.GetString(0));
                Assert.Equal(1, reader.GetInt32(1)); // Tombstone won

                // Model 200 - EnvCellStaticObject
                Assert.True(reader.Read());
                Assert.Equal("dat:EnvCellStaticObject:7D640135:000A:0", reader.GetString(0)); // 2103705909 is 0x7D640135
                Assert.Equal(0, reader.GetInt32(1));

                // Model 300 - DB Added Object
                Assert.True(reader.Read());
                var dbId = reader.GetString(0);
                Assert.StartsWith("db:StaticObject:", dbId);
                // Ensure state is 0 in the hex part (last byte of high ulong)
                // db:StaticObject:HIGH(16)LOW(16)
                // high byte 5: state.
                Assert.Equal('0', dbId[32]); 
                Assert.Equal('0', dbId[33]);

                Assert.False(reader.Read()); // Only 3 rows total now
            }        }
    }
}
