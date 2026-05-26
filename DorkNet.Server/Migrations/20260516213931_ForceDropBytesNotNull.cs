using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DorkNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class ForceDropBytesNotNull : Migration
    {
        // The previous migration (MakeRoomDataBlobBytesNullable) used
        // EF's AlterColumn helper, which on Postgres produced a no-op
        // because EF's diff saw oldType == newType and elided the
        // statement — leaving the column still NOT NULL even though
        // the model said it was nullable.
        //
        // Symptom: backfill UPDATEs failed with 23502 "null value in
        // column Bytes violates not-null constraint" for every row.
        //
        // This migration runs the explicit DROP NOT NULL via raw SQL,
        // gated on the active provider so SQLite (which doesn't speak
        // that syntax and was already correctly fixed by the previous
        // migration's table-rebuild dance) isn't affected. IF NOT
        // EXISTS isn't supported here, but DROP NOT NULL when the
        // column is already nullable is a Postgres no-op — safe to
        // re-run.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"ALTER TABLE ""RoomDataBlobs"" ALTER COLUMN ""Bytes"" DROP NOT NULL;");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // Re-tightening would fail if any rows had been nulled
                // by the backfill — defensive UPDATE first puts them
                // back to empty BYTEA, then the constraint can be
                // re-applied. Rolling back this far is unusual.
                migrationBuilder.Sql(@"UPDATE ""RoomDataBlobs"" SET ""Bytes"" = ''::bytea WHERE ""Bytes"" IS NULL;");
                migrationBuilder.Sql(@"ALTER TABLE ""RoomDataBlobs"" ALTER COLUMN ""Bytes"" SET NOT NULL;");
            }
        }
    }
}
