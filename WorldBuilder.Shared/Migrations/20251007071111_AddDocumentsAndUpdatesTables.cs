using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldBuilder.Shared.Migrations {
    /// <inheritdoc />
    public partial class AddDocumentsAndUpdatesTables : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            // Create Documents table if it doesn't exist
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS Documents (
                    Id TEXT NOT NULL,
                    Type TEXT NOT NULL,
                    Data BLOB NOT NULL,
                    LastModified TEXT NOT NULL,
                    CONSTRAINT PK_Documents PRIMARY KEY (Id)
                )");

            // Create Updates table if it doesn't exist
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS Updates (
                    Id TEXT NOT NULL,
                    DocumentId TEXT NOT NULL,
                    ClientId TEXT NOT NULL,
                    Type TEXT NOT NULL,
                    Data BLOB NOT NULL,
                    Timestamp TEXT NOT NULL,
                    CONSTRAINT PK_Updates PRIMARY KEY (Id),
                    CONSTRAINT FK_Updates_Documents FOREIGN KEY (DocumentId) 
                        REFERENCES Documents (Id) ON DELETE CASCADE
                )");

            // Create indexes for Documents table if they don't exist
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Documents_Id 
                ON Documents (Id)");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Documents_LastModified 
                ON Documents (LastModified)");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Documents_Type 
                ON Documents (Type)");

            // Create indexes for Updates table if they don't exist
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Updates_ClientId 
                ON Updates (ClientId)");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Updates_Timestamp 
                ON Updates (Timestamp)");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Updates_DocumentId_Timestamp 
                ON Updates (DocumentId, Timestamp)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            // Drop indexes for Updates table
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Updates_ClientId");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Updates_Timestamp");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Updates_DocumentId_Timestamp");

            // Drop indexes for Documents table
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Documents_Id");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Documents_LastModified");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Documents_Type");

            // Drop Updates table
            migrationBuilder.Sql("DROP TABLE IF EXISTS Updates");

            // Drop Documents table
            migrationBuilder.Sql("DROP TABLE IF EXISTS Documents");
        }
    }
}