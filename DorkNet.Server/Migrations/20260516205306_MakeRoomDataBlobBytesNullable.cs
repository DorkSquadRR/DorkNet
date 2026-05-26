using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class MakeRoomDataBlobBytesNullable : Migration
    {
        // CRITICAL: this migration must NOT drop or recreate the Bytes
        // column. The column physically exists on every prod DB and
        // holds the legacy bytes that haven't been backfilled to S3
        // yet. A DropColumn here would obliterate every pre-cutover
        // room save. We only flip NOT NULL -> NULL so new inserts
        // (which leave the property null because writers go S3-only
        // now) don't crash on the constraint. Run the
        // /api/admin/v1/storage/backfill admin endpoint to copy the
        // bytes from this column into S3 row-by-row; a follow-up
        // migration drops the column once every row has been moved.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "Bytes",
                table: "RoomDataBlobs",
                type: "BLOB",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "BLOB");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-tighten to NOT NULL. Any rows that became NULL during
            // the backfill won't pass this constraint — caller's
            // responsibility to repopulate or delete those rows
            // before rolling back. Defaulting to empty BLOB keeps
            // the constraint satisfiable.
            migrationBuilder.AlterColumn<byte[]>(
                name: "Bytes",
                table: "RoomDataBlobs",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldNullable: true);
        }
    }
}
