using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Kills the Friendship-graph feature entirely: drops the
    /// ProfileMilestones table along with the data the user logged
    /// against it. The graph proved too data-entry-heavy to earn its
    /// place; the Life Chapters / Life Map view covers the
    /// "who was in my life and when" question without the per-friend
    /// milestone burden.
    /// Idempotent — DROP TABLE IF EXISTS.
    /// </remarks>
    public partial class DropProfileMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ProfileMilestones"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate the table empty so a rollback doesn't crash the
            // app if a stale build of the EF model still references it.
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""ProfileMilestones"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""PersonProfileId"" INTEGER NOT NULL,
                    ""Year"" INTEGER NOT NULL,
                    ""Kind"" INTEGER NOT NULL,
                    ""Note"" VARCHAR(200) NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS ""IX_ProfileMilestones_PersonProfileId""
                    ON ""ProfileMilestones""(""PersonProfileId"");
            ");
        }
    }
}
