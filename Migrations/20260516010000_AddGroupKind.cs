using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddGroupKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kind: 0 = Family, 1 = Friends, 2 = Mixed. Default 0 keeps
            // existing rows behaving as family groups.
            migrationBuilder.Sql(@"
                ALTER TABLE ""FamilyGroups""
                    ADD COLUMN IF NOT EXISTS ""Kind"" INTEGER NOT NULL DEFAULT 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""FamilyGroups"" DROP COLUMN IF EXISTS ""Kind"";
            ");
        }
    }
}
