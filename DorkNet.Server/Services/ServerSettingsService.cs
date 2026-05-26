using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// Single-row settings store. Same Postgres-backed pattern as
/// <see cref="CommunityBoardService"/>: every read is one SELECT,
/// every write is one UPDATE/INSERT, and the row is visible to every
/// replica immediately so admin toggles propagate without a cache
/// invalidation dance.
///
/// Scoped (not singleton) because it holds a DbContext reference. The
/// toggles it backs (signups, etc.) are checked on rare paths —
/// account creation hits this once per signup attempt, which is
/// dwarfed by everything else the request does.
/// </summary>
public class ServerSettingsService(DorkNetDbContext db)
{
    private const int RowId = 1;

    public async Task<ServerSettingsEntity> GetAsync()
    {
        var row = await db.ServerSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RowId);
        return row ?? new ServerSettingsEntity { Id = RowId };
    }

    public async Task<bool> AreSignupsDisabledAsync()
    {
        var row = await db.ServerSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RowId);
        return row?.SignupsDisabled ?? false;
    }

    public async Task<ServerSettingsEntity> SetSignupsDisabledAsync(bool disabled)
    {
        var existing = await db.ServerSettings.FirstOrDefaultAsync(s => s.Id == RowId);
        if (existing is null)
        {
            existing = new ServerSettingsEntity
            {
                Id = RowId,
                SignupsDisabled = disabled,
                UpdatedAt = DateTime.UtcNow,
            };
            db.ServerSettings.Add(existing);
        }
        else
        {
            existing.SignupsDisabled = disabled;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return existing;
    }
}
