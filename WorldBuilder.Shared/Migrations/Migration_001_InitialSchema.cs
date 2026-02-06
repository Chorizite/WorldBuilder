using FluentMigrator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Migrations {
    [Migration(1, "Initial schema with all tables and indexes")]
    public class Migration_001_InitialSchema : Migration {
        public override void Up() {
            Execute.Sql(@"
            CREATE TABLE Documents (
                Id TEXT PRIMARY KEY,
                Type TEXT,
                Data BLOB NOT NULL,
                Version INTEGER NOT NULL,
                LastModified DATETIME DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT chk_version CHECK (Version >= 0)
            );

            CREATE TABLE Events (
                Id TEXT NOT NULL PRIMARY KEY, -- Ensure UNIQUE
                Type TEXT NOT NULL,
                Data BLOB NOT NULL,
                UserId TEXT NOT NULL,
                Created DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE UserKeyValues (
                Key TEXT PRIMARY KEY,
                Value TEXT
            );

            CREATE INDEX idx_events_userid ON Events(UserId);
        ");

            Console.WriteLine("Created initial schema");
        }

        public override void Down() {
            Execute.Sql(@"
            DROP INDEX IF EXISTS idx_events_userid;

            DROP TABLE IF EXISTS Documents;
            DROP TABLE IF EXISTS Events;
            DROP TABLE IF EXISTS UserKeyValues;
        ");
        }
    }
}