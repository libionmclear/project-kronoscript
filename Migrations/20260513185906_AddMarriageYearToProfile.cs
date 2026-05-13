using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — uses ADD COLUMN IF NOT EXISTS so it
    /// survives running against a DB where the column already lives.</remarks>
    public partial class AddMarriageYearToProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""PersonProfiles""
                    ADD COLUMN IF NOT EXISTS ""MarriageYear"" INTEGER NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""PersonProfiles"" DROP COLUMN IF EXISTS ""MarriageYear"";
            ");
        }
    }
}
