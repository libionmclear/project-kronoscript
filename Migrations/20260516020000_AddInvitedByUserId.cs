using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddInvitedByUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Permanent referral attribution: who brought this user in.
            // Nullable — existing users predate this column. No FK so the
            // value survives if the inviter later deletes their account.
            migrationBuilder.Sql(@"
                ALTER TABLE ""AspNetUsers""
                    ADD COLUMN IF NOT EXISTS ""InvitedByUserId"" VARCHAR(450);
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_AspNetUsers_InvitedByUserId""
                    ON ""AspNetUsers"" (""InvitedByUserId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AspNetUsers_InvitedByUserId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""InvitedByUserId"";");
        }
    }
}
