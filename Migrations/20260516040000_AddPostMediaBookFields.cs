using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddPostMediaBookFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Book-view-only photo controls. Each photo can be hidden
            // from the book without removing it from the post, and the
            // writer can choose a wrap mode + size inline in the book
            // view (no detour back to the editor).
            migrationBuilder.Sql(@"
                ALTER TABLE ""PostMedia""
                    ADD COLUMN IF NOT EXISTS ""HideFromBook"" BOOLEAN NOT NULL DEFAULT FALSE;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE ""PostMedia""
                    ADD COLUMN IF NOT EXISTS ""BookWrap"" VARCHAR(8);
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE ""PostMedia""
                    ADD COLUMN IF NOT EXISTS ""BookSize"" VARCHAR(8);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""PostMedia"" DROP COLUMN IF EXISTS ""BookSize"";");
            migrationBuilder.Sql(@"ALTER TABLE ""PostMedia"" DROP COLUMN IF EXISTS ""BookWrap"";");
            migrationBuilder.Sql(@"ALTER TABLE ""PostMedia"" DROP COLUMN IF EXISTS ""HideFromBook"";");
        }
    }
}
