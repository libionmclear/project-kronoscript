using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddTimeCapsuleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""LifeEventPosts""
                    ADD COLUMN IF NOT EXISTS ""ReleaseAt"" TIMESTAMP WITH TIME ZONE;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE ""LifeEventPosts""
                    ADD COLUMN IF NOT EXISTS ""ReleaseToUserId"" VARCHAR(450);
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE ""LifeEventPosts""
                    ADD COLUMN IF NOT EXISTS ""ReleaseToFamilyGroupId"" INTEGER;
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_LifeEventPosts_ReleaseAt""
                    ON ""LifeEventPosts"" (""ReleaseAt"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LifeEventPosts_ReleaseAt"";");
            migrationBuilder.Sql(@"ALTER TABLE ""LifeEventPosts"" DROP COLUMN IF EXISTS ""ReleaseToFamilyGroupId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""LifeEventPosts"" DROP COLUMN IF EXISTS ""ReleaseToUserId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""LifeEventPosts"" DROP COLUMN IF EXISTS ""ReleaseAt"";");
        }
    }
}
