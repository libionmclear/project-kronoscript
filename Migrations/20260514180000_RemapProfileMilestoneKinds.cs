using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Remaps ProfileMilestone.Kind from the old 6-value enum
    /// (Met / Close / Drifted / Estranged / Reconnected / Lost = 0..5)
    /// to the new 7-value enum (Best / Close / Friend / Connected /
    /// Drifted / Estranged / LostContact = 10..16). The old "Met" and
    /// "Reconnected" both fold into the new "Friend" band — the new
    /// scheme doesn't model an event-vs-state distinction; each kind
    /// IS the resulting closeness level.
    /// Idempotent — the new values never collide with old ones, so a
    /// row that has already been remapped takes the ELSE branch and
    /// stays put.
    /// </remarks>
    public partial class RemapProfileMilestoneKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""ProfileMilestones""
                SET ""Kind"" = CASE ""Kind""
                    WHEN 0 THEN 12  -- old Met         -> new Friend
                    WHEN 1 THEN 11  -- old Close       -> new Close
                    WHEN 2 THEN 14  -- old Drifted     -> new Drifted
                    WHEN 3 THEN 15  -- old Estranged   -> new Estranged
                    WHEN 4 THEN 12  -- old Reconnected -> new Friend
                    WHEN 5 THEN 16  -- old Lost        -> new LostContact
                    ELSE ""Kind""
                END
                WHERE ""Kind"" BETWEEN 0 AND 5;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reverse: pick the nearest old kind for each
            // new value. Best/Close both collapse to old Close; Friend
            // becomes old Met; Connected becomes old Drifted (closest
            // mid-band); LostContact becomes old Lost.
            migrationBuilder.Sql(@"
                UPDATE ""ProfileMilestones""
                SET ""Kind"" = CASE ""Kind""
                    WHEN 10 THEN 1  -- Best        -> Close
                    WHEN 11 THEN 1  -- Close       -> Close
                    WHEN 12 THEN 0  -- Friend      -> Met
                    WHEN 13 THEN 2  -- Connected   -> Drifted
                    WHEN 14 THEN 2  -- Drifted     -> Drifted
                    WHEN 15 THEN 3  -- Estranged   -> Estranged
                    WHEN 16 THEN 5  -- LostContact -> Lost
                    ELSE ""Kind""
                END
                WHERE ""Kind"" BETWEEN 10 AND 16;
            ");
        }
    }
}
