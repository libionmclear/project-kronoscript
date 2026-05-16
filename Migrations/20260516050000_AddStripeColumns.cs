using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddStripeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""AspNetUsers""
                    ADD COLUMN IF NOT EXISTS ""StripeCustomerId"" VARCHAR(64);
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE ""AspNetUsers""
                    ADD COLUMN IF NOT EXISTS ""StripeSubscriptionId"" VARCHAR(64);
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_AspNetUsers_StripeCustomerId""
                    ON ""AspNetUsers"" (""StripeCustomerId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AspNetUsers_StripeCustomerId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""StripeSubscriptionId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""StripeCustomerId"";");
        }
    }
}
