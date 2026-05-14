using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddBookChapterParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""BookChapters""
                    ADD COLUMN IF NOT EXISTS ""ParentChapterId"" INTEGER NULL;
                CREATE INDEX IF NOT EXISTS ""IX_BookChapters_ParentChapterId""
                    ON ""BookChapters""(""ParentChapterId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""BookChapters"" DROP COLUMN IF EXISTS ""ParentChapterId"";
            ");
        }
    }
}
