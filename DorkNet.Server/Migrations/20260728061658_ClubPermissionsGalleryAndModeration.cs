using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class ClubPermissionsGalleryAndModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults must match the entity contract (TestCaseEntity inits
            // CommentsJson to "[]", ClubEntity inits ClubChatEnabled to true):
            // existing rows get the column default, and "" is not valid JSON
            // while false silently muted every pre-existing club's chat. The
            // ClubChatDefaultRepair migration fixes databases that applied
            // this migration before the defaults were corrected.
            migrationBuilder.AddColumn<string>(
                name: "CommentsJson",
                table: "TestCases",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "ClubChatEnabled",
                table: "Clubs",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MinLevel",
                table: "Clubs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ModerationState",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ClubAdditionalImages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClubId = table.Column<long>(type: "INTEGER", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubAdditionalImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubRolePermissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClubId = table.Column<long>(type: "INTEGER", nullable: false),
                    MembershipType = table.Column<int>(type: "INTEGER", nullable: false),
                    EditDetails = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApproveMember = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreateEvent = table.Column<bool>(type: "INTEGER", nullable: false),
                    PostAnnouncement = table.Column<bool>(type: "INTEGER", nullable: false),
                    EditPermissionSettings = table.Column<bool>(type: "INTEGER", nullable: false),
                    BanUnban = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubRolePermissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubAdditionalImages_ClubId_Slot",
                table: "ClubAdditionalImages",
                columns: new[] { "ClubId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubRolePermissions_ClubId_MembershipType",
                table: "ClubRolePermissions",
                columns: new[] { "ClubId", "MembershipType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubAdditionalImages");

            migrationBuilder.DropTable(
                name: "ClubRolePermissions");

            migrationBuilder.DropColumn(
                name: "CommentsJson",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ClubChatEnabled",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "MinLevel",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "ModerationState",
                table: "ChatMessages");
        }
    }
}
