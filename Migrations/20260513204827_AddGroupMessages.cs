using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — uses IF NOT EXISTS, same pattern as the
    /// other Family Group migrations.</remarks>
    public partial class AddGroupMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""GroupMessages"" (
                    ""Id""            SERIAL PRIMARY KEY,
                    ""FamilyGroupId"" INTEGER NOT NULL,
                    ""SenderUserId""  TEXT NOT NULL,
                    ""Body""          CHARACTER VARYING(2000) NOT NULL,
                    ""SentAt""        TIMESTAMP WITH TIME ZONE NOT NULL,
                    CONSTRAINT ""FK_GroupMessages_FamilyGroups_FamilyGroupId""
                        FOREIGN KEY (""FamilyGroupId"") REFERENCES ""FamilyGroups""(""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_GroupMessages_AspNetUsers_SenderUserId""
                        FOREIGN KEY (""SenderUserId"") REFERENCES ""AspNetUsers""(""Id"") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ""IX_GroupMessages_FamilyGroupId_SentAt""
                    ON ""GroupMessages"" (""FamilyGroupId"", ""SentAt"");
                CREATE INDEX IF NOT EXISTS ""IX_GroupMessages_SenderUserId""
                    ON ""GroupMessages"" (""SenderUserId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""GroupMessages"";");
        }
    }
}
