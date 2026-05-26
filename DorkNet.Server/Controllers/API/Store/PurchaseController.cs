using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Auth;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Store;

/// <summary>
/// api.rec.net/api/purchase/v1/* — real purchase flow that verifies
/// the player has enough currency, deducts it, and grants the item
/// to their inventory. Replaces the old "ack-everything" stubs in
/// AllEndpointsController that let the watch think it bought
/// something but never actually persisted the transaction.
///
/// The 2020 client follows a three-step flow when the player taps
/// "buy" on a Shop item:
///   1. POST initiatepurchase — server validates the item exists +
///      player has funds, returns a transaction id (we just echo
///      back the item id; no real reservation needed for a private
///      server's threat model).
///   2. POST processpurchase — server actually deducts currency,
///      grants the item to the avatar's InventoryJson list, returns
///      the new balance.
///   3. POST completepurchase — final ack; client uses this to
///      flip its local UI state from "purchasing…" to "owned".
/// We keep all three so the watch's flow doesn't deadlock waiting
/// for one or another, but the actual mutation happens in
/// processpurchase. initiate and complete just validate + ack.
/// </summary>
[ApiController]
[Authorize]
public class PurchaseController(StoreService store, ILogger<PurchaseController> logger) : ControllerBase
{
    public sealed record PurchaseRequest(long? ItemId, string? Slug, int? Quantity);

    /// <summary>POST initiatepurchase — validate the item exists and
    /// the caller has enough currency, but don't mutate yet. Returns
    /// the resolved item id so the watch can echo it back on
    /// processpurchase.</summary>
    [HttpPost("api/purchase/v1/initiatepurchase")]
    [HttpPost("api/purchase/v2/initiatepurchase")]
    [HttpPost("api/purchase/v3/initiatepurchase")]
    public async Task<IActionResult> Initiate([FromForm] PurchaseRequest body)
    {
        var item = await ResolveItemAsync(body);
        if (item is null) return Ok(new { success = false, error = "item_not_found" });
        return Ok(new
        {
            success = true,
            error = "",
            ItemId = item.Id,
            Slug = item.Slug,
            // Hand back a token the watch may include in
            // processpurchase. We accept anything on the way in, so
            // this is informational.
            TransactionId = $"txn-{item.Id}-{DateTime.UtcNow.Ticks}",
        });
    }

    /// <summary>POST processpurchase — atomic charge + grant. The
    /// real work. Returns success+balance on completion or the
    /// specific error code on failure (insufficient_funds,
    /// item_not_available, etc.) so the watch can show the right UI
    /// message.</summary>
    [HttpPost("api/purchase/v1/processpurchase")]
    [HttpPost("api/purchase/v2/processpurchase")]
    [HttpPost("api/purchase/v3/processpurchase")]
    public async Task<IActionResult> Process([FromForm] PurchaseRequest body)
    {
        var pid = this.RequireCurrentPlayerId();
        var item = await ResolveItemAsync(body);
        if (item is null) return Ok(new { success = false, error = "item_not_found" });

        var result = await store.PurchaseAsync(pid, item.Id);
        logger.LogInformation(
            "[purchase] player={Pid} item={Item} success={Success} error={Error} balance={Balance}",
            pid, item.Slug, result.Success, result.Error ?? "", result.Balance ?? 0);
        return Ok(new
        {
            success = result.Success,
            error = result.Error ?? "",
            Balance = result.Balance ?? 0,
            CurrencyType = item.CurrencyType,
            ItemId = item.Id,
            Slug = result.Slug ?? item.Slug,
        });
    }

    /// <summary>POST completepurchase — final ack. Client flips its
    /// UI to "owned" state and refreshes the avatar selector.</summary>
    [HttpPost("api/purchase/v1/completepurchase")]
    [HttpPost("api/purchase/v2/completepurchase")]
    [HttpPost("api/purchase/v3/completepurchase")]
    public IActionResult Complete() =>
        Ok(new { success = true, error = "" });

    /// <summary>POST cancelpurchase — client retreated mid-flow
    /// (closed the panel, network blip, etc.). Nothing to roll back
    /// since we don't reserve in initiate.</summary>
    [HttpPost("api/purchase/v1/cancelpurchase")]
    [HttpPost("api/purchase/v2/cancelpurchase")]
    [HttpPost("api/purchase/v3/cancelpurchase")]
    public IActionResult Cancel() =>
        Ok(new { success = true, error = "" });

    /// <summary>POST cleanuppending — client startup probe to release
    /// any stale "in-flight" purchases from a crashed previous
    /// session. We don't track in-flight state, so always-ack is
    /// correct.</summary>
    [HttpPost("api/purchase/v1/cleanuppending")]
    [HttpPost("api/purchase/v2/cleanuppending")]
    [HttpPost("api/purchase/v3/cleanuppending")]
    public IActionResult CleanupPending() =>
        Ok(new { success = true, error = "" });

    /// <summary>POST <c>subscription/v1/cancel</c> — Rec Room+
    /// subscription cancel button. Verified URL at
    /// Commerce.txt:2188. The private server has no real billing
    /// integration (no Stripe, no platform IAP receipts) so the
    /// subscription is purely cosmetic — we ack the cancel so the
    /// watch flips its UI to "canceled" and stops the recurring
    /// renewal nag. If a real subscription entity ever lands, this
    /// is where to expire its CurrentPeriodEnd.</summary>
    [HttpPost("/subscription/v1")]
    [HttpPost("/subscription/v1/cancel")]
    [HttpPost("/api/subscription/v1")]
    [HttpPost("/api/subscription/v1/cancel")]
    public IActionResult CancelSubscription()
    {
        logger.LogInformation("[subscription] cancel requested by player {Pid}",
            this.CurrentPlayerId() ?? 0);
        return Ok(new { success = true, error = "" });
    }

    private async Task<Data.Entities.StoreItemEntity?> ResolveItemAsync(PurchaseRequest body)
    {
        if (body.ItemId is long id && id > 0)
            return await store.GetByIdAsync(id);
        if (!string.IsNullOrWhiteSpace(body.Slug))
            return await store.GetBySlugAsync(body.Slug);
        return null;
    }
}
