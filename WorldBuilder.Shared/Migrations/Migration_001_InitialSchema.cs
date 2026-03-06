using FluentMigrator;
using System;

namespace WorldBuilder.Shared.Migrations {
    /// <summary>
    /// Initial database schema migration for the Hybrid Storage Architecture.
    /// </summary>
    [Migration(1, "Initial schema for Hybrid Storage Architecture")]
    public class Migration_001_InitialSchema : Migration {
        public override void Up() {

            // Table: TerrainPatches (Optimized for terrain data)
            Create.Table("TerrainPatches")
                .WithColumn("Id").AsString().PrimaryKey()
                .WithColumn("RegionId").AsInt64().Indexed("idx_terrainpatches_regionid")
                .WithColumn("Data").AsBinary().NotNullable()
                .WithColumn("Version").AsInt64().NotNullable()
                .WithColumn("LastModified").AsDateTime().WithDefault(SystemMethods.CurrentDateTime);

            // Table: LandscapeGroups
            Create.Table("LandscapeGroups")
                .WithColumn("Id").AsString().PrimaryKey()
                .WithColumn("RegionId").AsInt64().Indexed("idx_landscapegroups_regionid")
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("ParentId").AsString().Nullable().Indexed("idx_landscapegroups_parentid").ForeignKey("LandscapeGroups", "Id").OnDelete(System.Data.Rule.Cascade)
                .WithColumn("IsExported").AsBoolean().WithDefaultValue(true)
                .WithColumn("SortOrder").AsInt32().NotNullable();

            // Table: LandscapeLayers
            Create.Table("LandscapeLayers")
                .WithColumn("Id").AsString().PrimaryKey()
                .WithColumn("RegionId").AsInt64().Indexed("idx_landscapelayers_regionid")
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("ParentId").AsString().Nullable().Indexed("idx_landscapelayers_parentid").ForeignKey("LandscapeGroups", "Id").OnDelete(System.Data.Rule.Cascade)
                .WithColumn("IsExported").AsBoolean().WithDefaultValue(true)
                .WithColumn("IsBase").AsBoolean().WithDefaultValue(false)
                .WithColumn("SortOrder").AsInt32().NotNullable();

            // Table: StaticObjects
            Create.Table("StaticObjects")
                .WithColumn("InstanceId").AsInt64().PrimaryKey()
                .WithColumn("RegionId").AsInt64().Indexed("idx_staticobjects_regionid")
                .WithColumn("LayerId").AsString().Indexed("idx_staticobjects_layerid").ForeignKey("LandscapeLayers", "Id")
                .WithColumn("LandblockId").AsInt64().Indexed("idx_staticobjects_landblockid")
                .WithColumn("CellId").AsInt64().Nullable().Indexed("idx_staticobjects_cellid")
                .WithColumn("ModelId").AsInt64().NotNullable()
                .WithColumn("PosX").AsFloat().NotNullable()
                .WithColumn("PosY").AsFloat().NotNullable()
                .WithColumn("PosZ").AsFloat().NotNullable()
                .WithColumn("RotW").AsFloat().NotNullable()
                .WithColumn("RotX").AsFloat().NotNullable()
                .WithColumn("RotY").AsFloat().NotNullable()
                .WithColumn("RotZ").AsFloat().NotNullable()
                .WithColumn("IsDeleted").AsBoolean().WithDefaultValue(false);

            // Table: Buildings
            Create.Table("Buildings")
                .WithColumn("InstanceId").AsInt64().PrimaryKey()
                .WithColumn("RegionId").AsInt64().Indexed()
                .WithColumn("LayerId").AsString().Indexed().ForeignKey("LandscapeLayers", "Id")
                .WithColumn("LandblockId").AsInt64().Indexed()
                .WithColumn("ModelId").AsInt64().NotNullable()
                .WithColumn("PosX").AsFloat().NotNullable()
                .WithColumn("PosY").AsFloat().NotNullable()
                .WithColumn("PosZ").AsFloat().NotNullable()
                .WithColumn("RotW").AsFloat().NotNullable()
                .WithColumn("RotX").AsFloat().NotNullable()
                .WithColumn("RotY").AsFloat().NotNullable()
                .WithColumn("RotZ").AsFloat().NotNullable()
                .WithColumn("IsDeleted").AsBoolean().WithDefaultValue(false);

            // Table: Events (Retained for sync)
            Create.Table("Events")
                .WithColumn("Id").AsString().PrimaryKey()
                .WithColumn("Type").AsString().NotNullable()
                .WithColumn("Data").AsBinary().NotNullable()
                .WithColumn("UserId").AsString().NotNullable().Indexed("idx_events_userid")
                .WithColumn("Created").AsDateTime().WithDefault(SystemMethods.CurrentDateTime)
                .WithColumn("ServerTimestamp").AsInt64().Nullable().Indexed("idx_events_servertimestamp");

            // Table: UserKeyValues (Retained for settings)
            Create.Table("UserKeyValues")
                .WithColumn("Key").AsString().PrimaryKey()
                .WithColumn("Value").AsString().Nullable();
        }

        public override void Down() {
            Delete.Table("Buildings");
            Delete.Table("StaticObjects");
            Delete.Table("LandscapeLayers");
            Delete.Table("LandscapeGroups");
            Delete.Table("TerrainPatches");
            Delete.Table("Events");
            Delete.Table("UserKeyValues");
        }
    }
}
