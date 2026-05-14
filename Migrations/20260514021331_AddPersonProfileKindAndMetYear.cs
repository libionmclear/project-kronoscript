using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddPersonProfileKindAndMetYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""PersonProfiles""
                    ADD COLUMN IF NOT EXISTS ""Kind"" INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE ""PersonProfiles""
                    ADD COLUMN IF NOT EXISTS ""MetYear"" INTEGER NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""PersonProfiles"" DROP COLUMN IF EXISTS ""Kind"";
                ALTER TABLE ""PersonProfiles"" DROP COLUMN IF EXISTS ""MetYear"";
            ");
        }
    }
}
