using FluentMigrator;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Migrations {
    [Migration(4, "Convert InstanceId from INTEGER to TEXT")]
    public class Migration_004_StringInstanceIds : Migration {
        public override void Up() {
            // SQLite doesn't support ALTER COLUMN, so we must recreate the tables
            
            ConvertTable("StaticObjects", 
                @"CREATE TABLE StaticObjects_New (
                    InstanceId TEXT PRIMARY KEY,
                    RegionId INTEGER,
                    LayerId TEXT REFERENCES LandscapeLayers(Id),
                    LandblockId INTEGER,
                    CellId INTEGER NULL,
                    ModelId INTEGER NOT NULL,
                    PosX REAL NOT NULL,
                    PosY REAL NOT NULL,
                    PosZ REAL NOT NULL,
                    RotW REAL NOT NULL,
                    RotX REAL NOT NULL,
                    RotY REAL NOT NULL,
                    RotZ REAL NOT NULL,
                    IsDeleted BOOLEAN DEFAULT 0
                )",
                new[] {
                    "CREATE INDEX idx_staticobjects_regionid ON StaticObjects(RegionId)",
                    "CREATE INDEX idx_staticobjects_layerid ON StaticObjects(LayerId)",
                    "CREATE INDEX idx_staticobjects_landblockid ON StaticObjects(LandblockId)",
                    "CREATE INDEX idx_staticobjects_cellid ON StaticObjects(CellId)"
                });

            ConvertTable("Buildings", 
                @"CREATE TABLE Buildings_New (
                    InstanceId TEXT PRIMARY KEY,
                    RegionId INTEGER,
                    LayerId TEXT REFERENCES LandscapeLayers(Id),
                    LandblockId INTEGER,
                    ModelId INTEGER NOT NULL,
                    PosX REAL NOT NULL,
                    PosY REAL NOT NULL,
                    PosZ REAL NOT NULL,
                    RotW REAL NOT NULL,
                    RotX REAL NOT NULL,
                    RotY REAL NOT NULL,
                    RotZ REAL NOT NULL,
                    NumLeaves INTEGER NOT NULL DEFAULT 0,
                    IsDeleted BOOLEAN DEFAULT 0
                )",
                new[] {
                    "CREATE INDEX idx_buildings_regionid ON Buildings(RegionId)",
                    "CREATE INDEX idx_buildings_layerid ON Buildings(LayerId)",
                    "CREATE INDEX idx_buildings_landblockid ON Buildings(LandblockId)"
                });
        }

        private void ConvertTable(string tableName, string createTableSql, string[] indexSqls) {
            // 1. Create the new table
            Execute.Sql(createTableSql);

            // 2. Migrate data
            Execute.WithConnection((conn, tx) => {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"SELECT * FROM {tableName}";
                
                using var reader = cmd.ExecuteReader();
                var columns = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++) columns[i] = reader.GetName(i);
                
                while (reader.Read()) {
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    
                    // Convert InstanceId
                    var oldIdValue = values[0];
                    if (oldIdValue is long oldIdLong) {
                        var oldId = (ulong)oldIdLong;

                        // Check for CellId/LandblockId to help reconstruct the correct type and context
                        uint? cellId = null;
                        uint? landblockId = null;
                        if (tableName == "StaticObjects") {
                            var cellIdIdx = reader.GetOrdinal("CellId");
                            if (!reader.IsDBNull(cellIdIdx)) {
                                cellId = (uint)reader.GetInt64(cellIdIdx);
                            }
                            var lbIdIdx = reader.GetOrdinal("LandblockId");
                            if (!reader.IsDBNull(lbIdIdx)) {
                                landblockId = (uint)reader.GetInt64(lbIdIdx);
                            }
                        } else if (tableName == "Buildings") {
                            var lbIdIdx = reader.GetOrdinal("LandblockId");
                            if (!reader.IsDBNull(lbIdIdx)) {
                                landblockId = (uint)reader.GetInt64(lbIdIdx);
                            }
                        }

                        var newIdStr = ConvertLegacyId(oldId, cellId, landblockId);
                        values[0] = newIdStr;
                    }

                    var insertSql = $"INSERT INTO {tableName}_New ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select(c => "@" + c))})";
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = insertSql;
                    for (int i = 0; i < columns.Length; i++) {
                        var param = insertCmd.CreateParameter();
                        param.ParameterName = "@" + columns[i];
                        param.Value = values[i] ?? DBNull.Value;
                        insertCmd.Parameters.Add(param);
                    }
                    insertCmd.ExecuteNonQuery();
                }
            });

            // 3. Drop old table (also drops its indices)
            Delete.Table(tableName);

            // 4. Rename new table to old name
            Execute.Sql($"ALTER TABLE {tableName}_New RENAME TO {tableName}");

            // 5. Create new indices on renamed table
            foreach (var indexSql in indexSqls) {
                Execute.Sql(indexSql);
            }
        }

        private string ConvertLegacyId(ulong oldId, uint? cellId = null, uint? landblockId = null) {
            byte typeVal = (byte)((oldId >> 56) & 0xFF);
            byte stateVal = (byte)((oldId >> 48) & 0xFF);
            uint context = (uint)((oldId >> 16) & 0xFFFFFFFF);
            ushort index = (ushort)(oldId & 0xFFFF);

            var type = MapLegacyType(typeVal);
            
            // Reconstruct correct type and context based on database table structure
            if (cellId.HasValue) {
                type = ObjectType.EnvCellStaticObject;
                context = cellId.Value;
            } else if (landblockId.HasValue) {
                if (type != ObjectType.Building) {
                    type = ObjectType.StaticObject;
                }
                // Landblock context is (landblockId << 16) | 0xFFFE
                context = (landblockId.Value << 16) | 0xFFFE;
            }

            if (stateVal == 1) { // Added (Legacy DB)
                // New system uses state 0 for DB objects (see ObjectId.NewDb)
                // FromLegacyDbId takes the oldId and extracts state, let's just pass it with state cleared to 0
                var cleanOldId = (oldId & ~(0xFFUL << 48)); 
                var newId = ObjectId.FromLegacyDbId(type, cleanOldId);
                return newId.ToString();
            } else { // Original or Modified (Legacy DAT-based)
                // New system expects state 0 to match DAT objects (see LandscapeDataProvider)
                return $"dat:{type}:{context:X8}:{index:X4}:0";
            }
        }

        private ObjectType MapLegacyType(byte typeVal) {
            return typeVal switch {
                2 => ObjectType.Building,
                3 => ObjectType.StaticObject,
                7 => ObjectType.EnvCellStaticObject,
                _ => ObjectType.None
            };
        }

        public override void Down() {
            throw new NotImplementedException("Downgrade not supported for ObjectId conversion");
        }
    }
}
