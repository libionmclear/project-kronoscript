using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddFamilyTreeShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""FamilyTreeShares"" (
                    ""Id""            SERIAL PRIMARY KEY,
                    ""UserId""        TEXT NOT NULL,
                    ""FamilyGroupId"" INTEGER NOT NULL,
                    ""CreatedAt""     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                    CONSTRAINT ""UQ_FamilyTreeShares_User_Group"" UNIQUE(""UserId"", ""FamilyGroupId"")
                );
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_FamilyTreeShares_FamilyGroupId""
                    ON ""FamilyTreeShares"" (""FamilyGroupId"");
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_FamilyTreeShares_UserId""
                    ON ""FamilyTreeShares"" (""UserId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""FamilyTreeShares"";");
        }
    }
}
