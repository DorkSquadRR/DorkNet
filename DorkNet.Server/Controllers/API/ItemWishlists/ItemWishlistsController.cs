using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.ItemWishlists;

/// <summary>
/// api.rec.net/api/itemWishlists — consumable/gift-drop wishlist
/// (RecNet.Runtime BPAIBFBKKCN). Wire contract verified against the
/// 2023.03.21 ISIL decompile:
///
///   GET  api/itemWishlists/v1/wishlist/{accountId|me}
///     - BPAIBFBKKCN.txt:700-763 (ELPBIBPOBJF) — URL is
///       String.Concat("api/itemWishlists/v1/wishlist/", idOrMe) where
///       idOrMe is "me" for the local account, else the numeric account
///       id as a PATH SEGMENT (not a query param). Missing this route
///       404s with an empty body → CHGIJBGGJAG "Response was empty" →
///       "Failed to get wishlist for player" (Player.log:2832-2872).
///     - Return type FGLDKEJLAKB&lt;List&lt;BFJNGMGONED&gt;&gt; with no converter
///       Func → the body is a BARE ARRAY of BFJNGMGONED.
///     - BFJNGMGONED = { Guid WishlistItemId, Int32 AccountId,
///       Int32 PurchasableItemId, DateTime CreatedAt }
///       (property types BFJNGMGONED.txt:3-97; JSON keys registered by
///       reader MCDADMNCDEI.txt:343-434, Pascal/camel/lower probes).
///
///   PUT  api/itemWishlists/v1/wishlist/me/{purchasableItemId}
///     - BPAIBFBKKCN.txt:161-215 (OOPPFFPOAFH) — format string
///       "{0}/v1/wishlist/me/{1}", verb field = 3 = PUT
///       (verb table HNLCIDLIIBO.txt:878-903). Response is parsed as the
///       AECMPGPHAII&lt;BFJNGMGONED&gt; envelope {"Value","Success","Error"}
///       (envelope reader keys LEEAJGDIOHI.txt:243-286).
///
///   DELETE api/itemWishlists/v1/wishlist/me/{purchasableItemId}
///     - BPAIBFBKKCN.txt:375-419 (IBLKDCAIODF) — same URL, verb 4 =
///       DELETE, fire-and-forget (LDGADANDBIO) — body ignored.
///
/// Storage: rows live in ItemWishlists with the purchasable item id
/// serialized into the existing ItemKey column (no schema change);
/// WishlistItemId is derived deterministically from the row id.
/// </summary>
[ApiController]
[Authorize]
public class ItemWishlistsController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet("api/itemWishlists/v1/wishlist/me")]
    public async Task<IActionResult> Mine() => await ForPlayer(Me);

    [HttpGet("api/itemWishlists/v1/wishlist/{accountId:long}")]
    public async Task<IActionResult> ForPlayer(long accountId)
    {
        var rows = await db.ItemWishlists
            .Where(w => w.PlayerId == accountId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire).Where(w => w is not null).ToArray());
    }

    [HttpPut("api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
    [HttpPost("api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
    public async Task<IActionResult> Add(int purchasableItemId)
    {
        var key = purchasableItemId.ToString();
        var row = await db.ItemWishlists.FirstOrDefaultAsync(w =>
            w.PlayerId == Me && w.ItemKey == key);
        if (row is null)
        {
            row = new ItemWishlistEntity
            {
                PlayerId = Me,
                ItemKey = key,
                ItemType = 0,
            };
            db.ItemWishlists.Add(row);
            await db.SaveChangesAsync();
        }

        // AECMPGPHAII<BFJNGMGONED> envelope: {"Value","Success","Error"}
        // (LEEAJGDIOHI.txt:243-286).
        return Ok(new
        {
            Value = ToWire(row),
            Success = true,
            Error = (string?)null,
        });
    }

    [HttpDelete("api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
    public async Task<IActionResult> Remove(int purchasableItemId)
    {
        var key = purchasableItemId.ToString();
        await db.ItemWishlists
            .Where(w => w.PlayerId == Me && w.ItemKey == key)
            .ExecuteDeleteAsync();
        // Fire-and-forget on the client (LDGADANDBIO) — body ignored.
        return Ok(new { Success = true });
    }

    /// <summary>BFJNGMGONED wire item. Rows whose ItemKey isn't a
    /// purchasable-item integer (legacy free-form keys) are skipped.</summary>
    private static object? ToWire(ItemWishlistEntity row)
    {
        if (!int.TryParse(row.ItemKey, out var purchasableItemId)) return null;
        return new
        {
            WishlistItemId = StableGuid(row.Id, row.PlayerId),
            AccountId = (int)row.PlayerId,
            PurchasableItemId = purchasableItemId,
            row.CreatedAt,
        };
    }

    /// <summary>Deterministic per-row Guid — the client only uses
    /// WishlistItemId as an opaque identity, but it must stay stable
    /// across requests.</summary>
    private static Guid StableGuid(long rowId, long playerId)
    {
        Span<byte> input = stackalloc byte[16];
        BitConverter.TryWriteBytes(input, rowId);
        BitConverter.TryWriteBytes(input[8..], playerId);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
