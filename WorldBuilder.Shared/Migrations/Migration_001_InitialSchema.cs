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
                .WithColumn("NumLeaves").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("IsDeleted").AsBoolean().WithDefaultValue(false);

            // Table: BuildingPortals
            Create.Table("BuildingPortals")
                .WithColumn("Id").AsInt64().PrimaryKey().Identity()
                .WithColumn("InstanceId").AsInt64().Indexed().ForeignKey("Buildings", "InstanceId").OnDelete(System.Data.Rule.Cascade)
                .WithColumn("Flags").AsInt64().NotNullable()
                .WithColumn("OtherCellId").AsInt32().NotNullable()
                .WithColumn("OtherPortalId").AsInt32().NotNullable();

            // Table: BuildingPortalStabs
            Create.Table("BuildingPortalStabs")
                .WithColumn("PortalId").AsInt64().Indexed().ForeignKey("BuildingPortals", "Id").OnDelete(System.Data.Rule.Cascade)
                .WithColumn("StabId").AsInt32().NotNullable();

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

            // Table: EnvCells (Properties only, objects are in StaticObjects)
            Create.Table("EnvCells")
                .WithColumn("CellId").AsInt64().PrimaryKey()
                .WithColumn("RegionId").AsInt64().Indexed("idx_envcells_regionid")
                .WithColumn("LayerId").AsString().Indexed("idx_envcells_layerid").ForeignKey("LandscapeLayers", "Id")
                .WithColumn("EnvironmentId").AsInt32().NotNullable()
                .WithColumn("Flags").AsInt64().NotNullable()
                .WithColumn("CellStructure").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("PosX").AsFloat().NotNullable().WithDefaultValue(0)
                .WithColumn("PosY").AsFloat().NotNullable().WithDefaultValue(0)
                .WithColumn("PosZ").AsFloat().NotNullable().WithDefaultValue(0)
                .WithColumn("RotW").AsFloat().NotNullable().WithDefaultValue(1)
                .WithColumn("RotX").AsFloat().NotNullable().WithDefaultValue(0)
                .WithColumn("RotY").AsFloat().NotNullable().WithDefaultValue(0)
                .WithColumn("RotZ").AsFloat().NotNullable().WithDefaultValue(0)
                .WithColumn("RestrictionObj").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("Version").AsInt64().NotNullable().WithDefaultValue(1);

            // Table: EnvCellSurfaces
            Create.Table("EnvCellSurfaces")
                .WithColumn("CellId").AsInt64().Indexed().ForeignKey("EnvCells", "CellId").OnDelete(System.Data.Rule.Cascade)
                .WithColumn("SurfaceId").AsInt32().NotNullable();

            // Table: EnvCellPortals
            Create.Table("EnvCellPortals")
                .WithColumn("CellId").AsInt64().Indexed().ForeignKey("EnvCells", "CellId").OnDelete(System.Data.Rule.Cascade)
                .WithColumn("Flags").AsInt64().NotNullable()
                .WithColumn("PolygonId").AsInt32().NotNullable()
                .WithColumn("OtherCellId").AsInt32().NotNullable()
                .WithColumn("OtherPortalId").AsInt32().NotNullable();

            // Table: EnvCellVisibleCells
            Create.Table("EnvCellVisibleCells")
                .WithColumn("CellId").AsInt64().Indexed().ForeignKey("EnvCells", "CellId").OnDelete(System.Data.Rule.Cascade)
                .WithColumn("VisibleCellId").AsInt32().NotNullable();
        }

        public override void Down() {
            Delete.Table("EnvCellVisibleCells");
            Delete.Table("EnvCellPortals");
            Delete.Table("EnvCellSurfaces");
            Delete.Table("EnvCells");
            Delete.Table("BuildingPortalStabs");
            Delete.Table("BuildingPortals");
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
