using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent variant — see the history note on
    /// AddFamilyGroups for why these migrations were rewritten with raw
    /// SQL and IF NOT EXISTS clauses.</remarks>
    public partial class AddGroupScopeToTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""FamilyTreeNodes""
                    ADD COLUMN IF NOT EXISTS ""FamilyGroupId"" INTEGER NULL;
                ALTER TABLE ""FamilyRelationships""
                    ADD COLUMN IF NOT EXISTS ""FamilyGroupId"" INTEGER NULL;

                CREATE INDEX IF NOT EXISTS ""IX_FamilyTreeNodes_FamilyGroupId""
                    ON ""FamilyTreeNodes"" (""FamilyGroupId"");
                CREATE INDEX IF NOT EXISTS ""IX_FamilyRelationships_FamilyGroupId""
                    ON ""FamilyRelationships"" (""FamilyGroupId"");

                DO $$ BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints
                        WHERE constraint_name = 'FK_FamilyTreeNodes_FamilyGroups_FamilyGroupId'
                    ) THEN
                        ALTER TABLE ""FamilyTreeNodes""
                            ADD CONSTRAINT ""FK_FamilyTreeNodes_FamilyGroups_FamilyGroupId""
                            FOREIGN KEY (""FamilyGroupId"") REFERENCES ""FamilyGroups""(""Id"") ON DELETE NO ACTION;
                    END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints
                        WHERE constraint_name = 'FK_FamilyRelationships_FamilyGroups_FamilyGroupId'
                    ) THEN
                        ALTER TABLE ""FamilyRelationships""
                            ADD CONSTRAINT ""FK_FamilyRelationships_FamilyGroups_FamilyGroupId""
                            FOREIGN KEY (""FamilyGroupId"") REFERENCES ""FamilyGroups""(""Id"") ON DELETE NO ACTION;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""FamilyTreeNodes""
                    DROP CONSTRAINT IF EXISTS ""FK_FamilyTreeNodes_FamilyGroups_FamilyGroupId"";
                ALTER TABLE ""FamilyRelationships""
                    DROP CONSTRAINT IF EXISTS ""FK_FamilyRelationships_FamilyGroups_FamilyGroupId"";
                DROP INDEX IF EXISTS ""IX_FamilyTreeNodes_FamilyGroupId"";
                DROP INDEX IF EXISTS ""IX_FamilyRelationships_FamilyGroupId"";
                ALTER TABLE ""FamilyTreeNodes"" DROP COLUMN IF EXISTS ""FamilyGroupId"";
                ALTER TABLE ""FamilyRelationships"" DROP COLUMN IF EXISTS ""FamilyGroupId"";
            ");
        }
    }
}
