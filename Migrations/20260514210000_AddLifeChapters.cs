using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddLifeChapters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""LifeChapters"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""OwnerUserId"" TEXT NOT NULL,
                    ""Name"" VARCHAR(120) NOT NULL,
                    ""Category"" INTEGER NOT NULL DEFAULT 9,
                    ""StartYear"" INTEGER NOT NULL,
                    ""EndYear"" INTEGER NULL,
                    ""Color"" VARCHAR(20) NULL,
                    ""Description"" VARCHAR(500) NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                    ""UpdatedAt"" TIMESTAMP WITH TIME ZONE NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_LifeChapters_OwnerUserId""
                    ON ""LifeChapters""(""OwnerUserId"");

                CREATE TABLE IF NOT EXISTS ""LifeChapterMembers"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""LifeChapterId"" INTEGER NOT NULL,
                    ""PersonProfileId"" INTEGER NOT NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                    CONSTRAINT ""FK_LifeChapterMembers_LifeChapters_LifeChapterId""
                        FOREIGN KEY (""LifeChapterId"")
                        REFERENCES ""LifeChapters""(""Id"")
                        ON DELETE CASCADE,
                    CONSTRAINT ""FK_LifeChapterMembers_PersonProfiles_PersonProfileId""
                        FOREIGN KEY (""PersonProfileId"")
                        REFERENCES ""PersonProfiles""(""Id"")
                        ON DELETE CASCADE,
                    CONSTRAINT ""UQ_LifeChapterMembers_Chapter_Profile""
                        UNIQUE (""LifeChapterId"", ""PersonProfileId"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_LifeChapterMembers_LifeChapterId""
                    ON ""LifeChapterMembers""(""LifeChapterId"");
                CREATE INDEX IF NOT EXISTS ""IX_LifeChapterMembers_PersonProfileId""
                    ON ""LifeChapterMembers""(""PersonProfileId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""LifeChapterMembers"";
                DROP TABLE IF EXISTS ""LifeChapters"";
            ");
        }
    }
}
