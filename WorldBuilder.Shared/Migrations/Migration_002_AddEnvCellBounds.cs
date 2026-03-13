using FluentMigrator;

namespace WorldBuilder.Shared.Migrations {
    [Migration(2, "Add MinBounds and MaxBounds to EnvCells")]
    public class Migration_002_AddEnvCellBounds : Migration {
        public override void Up() {
            Alter.Table("EnvCells")
                .AddColumn("MinX").AsFloat().NotNullable().WithDefaultValue(0)
                .AddColumn("MinY").AsFloat().NotNullable().WithDefaultValue(0)
                .AddColumn("MinZ").AsFloat().NotNullable().WithDefaultValue(0)
                .AddColumn("MaxX").AsFloat().NotNullable().WithDefaultValue(0)
                .AddColumn("MaxY").AsFloat().NotNullable().WithDefaultValue(0)
                .AddColumn("MaxZ").AsFloat().NotNullable().WithDefaultValue(0);
        }

        public override void Down() {
            Delete.Column("MinX").FromTable("EnvCells");
            Delete.Column("MinY").FromTable("EnvCells");
            Delete.Column("MinZ").FromTable("EnvCells");
            Delete.Column("MaxX").FromTable("EnvCells");
            Delete.Column("MaxY").FromTable("EnvCells");
            Delete.Column("MaxZ").FromTable("EnvCells");
        }
    }
}
