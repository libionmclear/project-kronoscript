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
            migrationBuilder.DropIndex(
                name: "IX_UserBans_BannedEmail",
                table: "UserBans");

            migrationBuilder.AddColumn<int>(
                name: "ParentCommentId",
                table: "Comments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_AspNetUsers_RecipientUserId",
                table: "Messages",
                column: "RecipientUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_AspNetUsers_SenderUserId",
                table: "Messages",
                column: "SenderUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_AspNetUsers_RecipientUserId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_AspNetUsers_SenderUserId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_UserBans_BannedEmail",
                table: "UserBans",
                column: "BannedEmail");
        }
    }
}
