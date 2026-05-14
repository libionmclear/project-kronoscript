using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddPostIsFinalised : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""LifeEventPosts""
                    ADD COLUMN IF NOT EXISTS ""IsFinalised"" BOOLEAN NOT NULL DEFAULT FALSE;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""LifeEventPosts"" DROP COLUMN IF EXISTS ""IsFinalised"";
            ");
        }
    }
}
