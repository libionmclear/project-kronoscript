using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HISTORY NOTE: this migration originally re-generated dozens of
    /// pre-existing tables and columns because the model snapshot had
    /// drifted from the actual deployed schema. On Azure that giant Up()
    /// failed at the first "CREATE TABLE Channels" (the table already
    /// exists), which cascaded into Family Tree / Family Groups returning
    /// 500 because the new tables/columns were never created. This
    /// rewritten Up() is idempotent and ONLY touches the three genuinely
    /// new FamilyGroup* tables. Legacy tables are left alone — they exist
    /// on Azure either from earlier migrations or runtime CREATE-IF-NOT-EXISTS.
    /// The companion .Designer.cs still reflects the old (full) snapshot;
    /// that's only a design-time concern, MigrateAsync just runs Up().
    /// </remarks>
    public partial class AddFamilyGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""FamilyGroups"" (
                    ""Id""              SERIAL PRIMARY KEY,
                    ""Name""            CHARACTER VARYING(120) NOT NULL,
                    ""Description""     CHARACTER VARYING(500) NULL,
                    ""CreatorUserId""   TEXT NOT NULL,
                    ""CreatedAt""       TIMESTAMP WITH TIME ZONE NOT NULL,
                    ""UpdatedAt""       TIMESTAMP WITH TIME ZONE NULL,
                    CONSTRAINT ""FK_FamilyGroups_AspNetUsers_CreatorUserId""
                        FOREIGN KEY (""CreatorUserId"") REFERENCES ""AspNetUsers""(""Id"") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS ""IX_FamilyGroups_CreatorUserId""
                    ON ""FamilyGroups"" (""CreatorUserId"");

                CREATE TABLE IF NOT EXISTS ""FamilyGroupMembers"" (
                    ""Id""              SERIAL PRIMARY KEY,
                    ""FamilyGroupId""   INTEGER NOT NULL,
                    ""UserId""          TEXT NOT NULL,
                    ""Role""            INTEGER NOT NULL,
                    ""JoinedAt""        TIMESTAMP WITH TIME ZONE NOT NULL,
                    CONSTRAINT ""FK_FamilyGroupMembers_FamilyGroups_FamilyGroupId""
                        FOREIGN KEY (""FamilyGroupId"") REFERENCES ""FamilyGroups""(""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_FamilyGroupMembers_AspNetUsers_UserId""
                        FOREIGN KEY (""UserId"") REFERENCES ""AspNetUsers""(""Id"") ON DELETE RESTRICT
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FamilyGroupMembers_FamilyGroupId_UserId""
                    ON ""FamilyGroupMembers"" (""FamilyGroupId"", ""UserId"");
                CREATE INDEX IF NOT EXISTS ""IX_FamilyGroupMembers_UserId""
                    ON ""FamilyGroupMembers"" (""UserId"");

                CREATE TABLE IF NOT EXISTS ""FamilyGroupPosts"" (
                    ""Id""              SERIAL PRIMARY KEY,
                    ""FamilyGroupId""   INTEGER NOT NULL,
                    ""LifeEventPostId"" INTEGER NOT NULL,
                    ""AddedByUserId""   TEXT NOT NULL,
                    ""AddedAt""         TIMESTAMP WITH TIME ZONE NOT NULL,
                    CONSTRAINT ""FK_FamilyGroupPosts_FamilyGroups_FamilyGroupId""
                        FOREIGN KEY (""FamilyGroupId"") REFERENCES ""FamilyGroups""(""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_FamilyGroupPosts_LifeEventPosts_LifeEventPostId""
                        FOREIGN KEY (""LifeEventPostId"") REFERENCES ""LifeEventPosts""(""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_FamilyGroupPosts_AspNetUsers_AddedByUserId""
                        FOREIGN KEY (""AddedByUserId"") REFERENCES ""AspNetUsers""(""Id"") ON DELETE RESTRICT
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FamilyGroupPosts_FamilyGroupId_LifeEventPostId""
                    ON ""FamilyGroupPosts"" (""FamilyGroupId"", ""LifeEventPostId"");
                CREATE INDEX IF NOT EXISTS ""IX_FamilyGroupPosts_LifeEventPostId""
                    ON ""FamilyGroupPosts"" (""LifeEventPostId"");
                CREATE INDEX IF NOT EXISTS ""IX_FamilyGroupPosts_AddedByUserId""
                    ON ""FamilyGroupPosts"" (""AddedByUserId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""FamilyGroupPosts"";
                DROP TABLE IF EXISTS ""FamilyGroupMembers"";
                DROP TABLE IF EXISTS ""FamilyGroups"";
            ");
        }
    }
}
