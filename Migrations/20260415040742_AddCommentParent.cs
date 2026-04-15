using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only add the new column, and do it idempotently — the safety
            // net in Program.cs may already have added it on an earlier
            // startup, so AddColumn would fail. Other snapshot drift
            // (UserBans index, Messages FKs) refers to objects that were
            // created via raw SQL on prod and don't exist in the real DB.
            migrationBuilder.Sql(@"ALTER TABLE ""Comments"" ADD COLUMN IF NOT EXISTS ""ParentCommentId"" INTEGER");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "Comments");
        }
    }
}
