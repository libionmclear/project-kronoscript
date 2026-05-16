using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddFamilyPlanCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""AspNetUsers""
                    ADD COLUMN IF NOT EXISTS ""CoveredByFamilyPlanOwnerId"" VARCHAR(450);
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_AspNetUsers_CoveredByFamilyPlanOwnerId""
                    ON ""AspNetUsers"" (""CoveredByFamilyPlanOwnerId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AspNetUsers_CoveredByFamilyPlanOwnerId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""CoveredByFamilyPlanOwnerId"";");
        }
    }
}
