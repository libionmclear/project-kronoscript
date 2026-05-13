using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddFamilyGroupIdToComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""Comments""
                    ADD COLUMN IF NOT EXISTS ""FamilyGroupId"" INTEGER NULL;
                CREATE INDEX IF NOT EXISTS ""IX_Comments_FamilyGroupId""
                    ON ""Comments"" (""FamilyGroupId"");
                DO $$ BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints
                        WHERE constraint_name = 'FK_Comments_FamilyGroups_FamilyGroupId'
                    ) THEN
                        ALTER TABLE ""Comments""
                            ADD CONSTRAINT ""FK_Comments_FamilyGroups_FamilyGroupId""
                            FOREIGN KEY (""FamilyGroupId"") REFERENCES ""FamilyGroups""(""Id"") ON DELETE NO ACTION;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""Comments"" DROP CONSTRAINT IF EXISTS ""FK_Comments_FamilyGroups_FamilyGroupId"";
                DROP INDEX IF EXISTS ""IX_Comments_FamilyGroupId"";
                ALTER TABLE ""Comments"" DROP COLUMN IF EXISTS ""FamilyGroupId"";
            ");
        }
    }
}
