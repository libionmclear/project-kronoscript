using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddUserEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""UserEvents"" (
                    ""Id""         BIGSERIAL PRIMARY KEY,
                    ""UserId""     VARCHAR(450) NULL,
                    ""EventType""  VARCHAR(80) NOT NULL,
                    ""EventData""  TEXT NULL,
                    ""OccurredAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
                );
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_UserEvents_EventType_OccurredAt""
                    ON ""UserEvents"" (""EventType"", ""OccurredAt"" DESC);
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_UserEvents_UserId_OccurredAt""
                    ON ""UserEvents"" (""UserId"", ""OccurredAt"" DESC);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_UserEvents_UserId_OccurredAt"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_UserEvents_EventType_OccurredAt"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""UserEvents"";");
        }
    }
}
