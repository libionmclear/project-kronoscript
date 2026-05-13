using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups for why
    /// migrations in this project use raw SQL with IF NOT EXISTS.</remarks>
    public partial class AddMarriageYearToRelativeConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""RelativeConnections""
                    ADD COLUMN IF NOT EXISTS ""MarriageYear"" INTEGER NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""RelativeConnections"" DROP COLUMN IF EXISTS ""MarriageYear"";
            ");
        }
    }
}
