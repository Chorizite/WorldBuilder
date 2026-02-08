using FluentMigrator;

namespace WorldBuilder.Shared.Migrations {
    /// <summary>
    /// Migration to add ServerTimestamp to the Events table.
    /// </summary>
    [Migration(2, "Add ServerTimestamp to Events table for sync tracking")]
    public class Migration_002_ServerTimestamp : Migration {
        public override void Up() {
            Execute.Sql(@"
                ALTER TABLE Events ADD COLUMN ServerTimestamp INTEGER NULL;
                CREATE INDEX idx_events_unsynced ON Events(ServerTimestamp) WHERE ServerTimestamp IS NULL;
            ");
        }

        public override void Down() {
            Execute.Sql(@"
                DROP INDEX IF EXISTS idx_events_unsynced;
                ALTER TABLE Events DROP COLUMN ServerTimestamp;
            ");
        }
    }
}
