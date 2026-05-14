using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddPostIncludeInBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""LifeEventPosts""
                    ADD COLUMN IF NOT EXISTS ""IncludeInBook"" BOOLEAN NOT NULL DEFAULT TRUE;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""LifeEventPosts"" DROP COLUMN IF EXISTS ""IncludeInBook"";
            ");
        }
    }
}
