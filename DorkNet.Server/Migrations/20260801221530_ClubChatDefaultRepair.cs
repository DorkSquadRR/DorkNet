using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <summary>One-shot data repair for databases that applied
    /// ClubPermissionsGalleryAndModeration while it carried the wrong column
    /// defaults (ClubChatEnabled false, CommentsJson ""). Those defaults
    /// contradicted the entity contract — every pre-existing club had its
    /// chat silently disabled and every pre-existing test case held invalid
    /// JSON. A migration (rather than a bootstrap statement) because the flip
    /// must run exactly once: re-running it on every boot would override any
    /// club that later disables chat on purpose. Fresh databases run the
    /// corrected AddColumn defaults first, so both UPDATEs no-op there.</summary>
    public partial class ClubChatDefaultRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"UPDATE ""Clubs"" SET ""ClubChatEnabled"" = TRUE WHERE ""ClubChatEnabled"" = FALSE;");
            migrationBuilder.Sql(
                @"UPDATE ""TestCases"" SET ""CommentsJson"" = '[]' WHERE ""CommentsJson"" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data repair — nothing meaningful to undo.
        }
    }
}
