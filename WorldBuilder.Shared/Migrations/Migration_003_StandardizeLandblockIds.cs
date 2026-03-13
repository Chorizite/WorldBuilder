using FluentMigrator;

namespace WorldBuilder.Shared.Migrations {
    [Migration(3, "Standardize LandblockId to ushort and remove unused tables")]
    public class Migration_003_StandardizeLandblockIds : Migration {
        public override void Up() {
            // Remove unused tables
            if (Schema.Table("UserValues").Exists()) {
                Delete.Table("UserValues");
            }
            if (Schema.Table("UserKeyValues").Exists()) {
                Delete.Table("UserKeyValues");
            }

            // Standardize StaticObjects.LandblockId
            if (Schema.Table("StaticObjects").Exists() && Schema.Table("StaticObjects").Column("LandblockId").Exists()) {
                Execute.Sql("UPDATE StaticObjects SET LandblockId = (LandblockId >> 16) & 0xFFFF");
            }

            // Standardize Buildings.LandblockId
            if (Schema.Table("Buildings").Exists() && Schema.Table("Buildings").Column("LandblockId").Exists()) {
                Execute.Sql("UPDATE Buildings SET LandblockId = (LandblockId >> 16) & 0xFFFF");
            }

            // Ensure EnvCells has LandblockId
            if (Schema.Table("EnvCells").Exists()) {
                if (!Schema.Table("EnvCells").Column("LandblockId").Exists()) {
                    Alter.Table("EnvCells").AddColumn("LandblockId").AsInt32().Nullable().Indexed();
                    // Populate it from CellId (high 16 bits)
                    Execute.Sql("UPDATE EnvCells SET LandblockId = (CellId >> 16) & 0xFFFF");
                }
                else {
                    Execute.Sql("UPDATE EnvCells SET LandblockId = (LandblockId >> 16) & 0xFFFF");
                }
            }
        }

        public override void Down() {
            // Restore unused tables
            Create.Table("UserKeyValues")
                .WithColumn("Key").AsString().PrimaryKey()
                .WithColumn("Value").AsString().Nullable();

            // Reverse
            Execute.Sql("UPDATE StaticObjects SET LandblockId = (LandblockId << 16) | 0xFFFE");
            Execute.Sql("UPDATE Buildings SET LandblockId = (LandblockId << 16) | 0xFFFE");
            Execute.Sql("UPDATE EnvCells SET LandblockId = (LandblockId << 16) | 0xFFFE");
        }
    }
}
