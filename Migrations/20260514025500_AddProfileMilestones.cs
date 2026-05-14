using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddProfileMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""ProfileMilestones"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""PersonProfileId"" INTEGER NOT NULL,
                    ""Year"" INTEGER NOT NULL,
                    ""Kind"" INTEGER NOT NULL,
                    ""Note"" VARCHAR(200) NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                    CONSTRAINT ""FK_ProfileMilestones_PersonProfiles_PersonProfileId""
                        FOREIGN KEY (""PersonProfileId"")
                        REFERENCES ""PersonProfiles""(""Id"")
                        ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ""IX_ProfileMilestones_PersonProfileId""
                    ON ""ProfileMilestones""(""PersonProfileId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ProfileMilestones"";");
        }
    }
}
