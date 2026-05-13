using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>Idempotent — see the note on AddFamilyGroups.</remarks>
    public partial class AddGroupOwnedProfilesAndMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- Group-owned People Profiles (any admin / co-admin of
                -- the linked group can collaboratively edit the NPC).
                ALTER TABLE ""PersonProfiles""
                    ADD COLUMN IF NOT EXISTS ""FamilyGroupId"" INTEGER NULL;
                CREATE INDEX IF NOT EXISTS ""IX_PersonProfiles_FamilyGroupId""
                    ON ""PersonProfiles"" (""FamilyGroupId"");
                DO $$ BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints
                        WHERE constraint_name = 'FK_PersonProfiles_FamilyGroups_FamilyGroupId'
                    ) THEN
                        ALTER TABLE ""PersonProfiles""
                            ADD CONSTRAINT ""FK_PersonProfiles_FamilyGroups_FamilyGroupId""
                            FOREIGN KEY (""FamilyGroupId"") REFERENCES ""FamilyGroups""(""Id"") ON DELETE NO ACTION;
                    END IF;
                END $$;

                -- Shared photo archive — one row per uploaded photo, with
                -- size on disk so the quota check stays fast.
                CREATE TABLE IF NOT EXISTS ""FamilyGroupMedia"" (
                    ""Id""             SERIAL PRIMARY KEY,
                    ""FamilyGroupId""  INTEGER NOT NULL,
                    ""UploaderUserId"" TEXT NOT NULL,
                    ""Url""            CHARACTER VARYING(500) NOT NULL,
                    ""ContentType""    CHARACTER VARYING(120) NULL,
                    ""ByteSize""       BIGINT NOT NULL,
                    ""Caption""        CHARACTER VARYING(500) NULL,
                    ""UploadedAt""     TIMESTAMP WITH TIME ZONE NOT NULL,
                    CONSTRAINT ""FK_FamilyGroupMedia_FamilyGroups_FamilyGroupId""
                        FOREIGN KEY (""FamilyGroupId"") REFERENCES ""FamilyGroups""(""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_FamilyGroupMedia_AspNetUsers_UploaderUserId""
                        FOREIGN KEY (""UploaderUserId"") REFERENCES ""AspNetUsers""(""Id"") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS ""IX_FamilyGroupMedia_FamilyGroupId_UploadedAt""
                    ON ""FamilyGroupMedia"" (""FamilyGroupId"", ""UploadedAt"");
                CREATE INDEX IF NOT EXISTS ""IX_FamilyGroupMedia_UploaderUserId""
                    ON ""FamilyGroupMedia"" (""UploaderUserId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""FamilyGroupMedia"";
                ALTER TABLE ""PersonProfiles"" DROP CONSTRAINT IF EXISTS ""FK_PersonProfiles_FamilyGroups_FamilyGroupId"";
                DROP INDEX IF EXISTS ""IX_PersonProfiles_FamilyGroupId"";
                ALTER TABLE ""PersonProfiles"" DROP COLUMN IF EXISTS ""FamilyGroupId"";
            ");
        }
    }
}
