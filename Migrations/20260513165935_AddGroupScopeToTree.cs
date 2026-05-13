using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupScopeToTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FamilyGroupId",
                table: "FamilyTreeNodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FamilyGroupId",
                table: "FamilyRelationships",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyTreeNodes_FamilyGroupId",
                table: "FamilyTreeNodes",
                column: "FamilyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRelationships_FamilyGroupId",
                table: "FamilyRelationships",
                column: "FamilyGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyRelationships_FamilyGroups_FamilyGroupId",
                table: "FamilyRelationships",
                column: "FamilyGroupId",
                principalTable: "FamilyGroups",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyTreeNodes_FamilyGroups_FamilyGroupId",
                table: "FamilyTreeNodes",
                column: "FamilyGroupId",
                principalTable: "FamilyGroups",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FamilyRelationships_FamilyGroups_FamilyGroupId",
                table: "FamilyRelationships");

            migrationBuilder.DropForeignKey(
                name: "FK_FamilyTreeNodes_FamilyGroups_FamilyGroupId",
                table: "FamilyTreeNodes");

            migrationBuilder.DropIndex(
                name: "IX_FamilyTreeNodes_FamilyGroupId",
                table: "FamilyTreeNodes");

            migrationBuilder.DropIndex(
                name: "IX_FamilyRelationships_FamilyGroupId",
                table: "FamilyRelationships");

            migrationBuilder.DropColumn(
                name: "FamilyGroupId",
                table: "FamilyTreeNodes");

            migrationBuilder.DropColumn(
                name: "FamilyGroupId",
                table: "FamilyRelationships");
        }
    }
}
