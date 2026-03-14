using FluentMigrator;

namespace WorldBuilder.Shared.Migrations {
    [Migration(5, "Add KeyValues table for project settings")]
    public class Migration_005_ProjectSettings : Migration {
        public override void Up() {
            if (!Schema.Table("KeyValues").Exists()) {
                Create.Table("KeyValues")
                    .WithColumn("Key").AsString().PrimaryKey()
                    .WithColumn("Value").AsString().Nullable();
            }
        }

        public override void Down() {
            if (Schema.Table("KeyValues").Exists()) {
                Delete.Table("KeyValues");
            }
        }
    }
}
