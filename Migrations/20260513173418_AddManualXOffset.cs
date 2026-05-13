using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent variant — see the history note on
    /// AddFamilyGroups for why these migrations were rewritten with raw
    /// SQL and IF NOT EXISTS clauses.</remarks>
    public partial class AddManualXOffset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""FamilyTreeNodes""
                    ADD COLUMN IF NOT EXISTS ""ManualXOffset"" DOUBLE PRECISION NOT NULL DEFAULT 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""FamilyTreeNodes"" DROP COLUMN IF EXISTS ""ManualXOffset"";
            ");
        }
    }
}
