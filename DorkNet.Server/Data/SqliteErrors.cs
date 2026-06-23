using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Data;

internal static class SqliteErrors
{
    public static bool IsBusy(DbUpdateException ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite &&
                (sqlite.SqliteErrorCode == 5 || sqlite.SqliteErrorCode == 6))
                return true;
        }

        return false;
    }
}
