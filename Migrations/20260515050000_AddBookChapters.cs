using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddBookChapters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""BookChapters"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""OwnerUserId"" TEXT NOT NULL,
                    ""Year"" INTEGER NOT NULL,
                    ""Title"" VARCHAR(200) NOT NULL,
                    ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                    ""UpdatedAt"" TIMESTAMP WITH TIME ZONE NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_BookChapters_OwnerUserId""
                    ON ""BookChapters""(""OwnerUserId"");
                CREATE INDEX IF NOT EXISTS ""IX_BookChapters_OwnerUserId_Year""
                    ON ""BookChapters""(""OwnerUserId"", ""Year"");

                ALTER TABLE ""LifeEventPosts""
                    ADD COLUMN IF NOT EXISTS ""BookChapterId"" INTEGER NULL;
                CREATE INDEX IF NOT EXISTS ""IX_LifeEventPosts_BookChapterId""
                    ON ""LifeEventPosts""(""BookChapterId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""LifeEventPosts"" DROP COLUMN IF EXISTS ""BookChapterId"";
                DROP TABLE IF EXISTS ""BookChapters"";
            ");
        }
    }
}
