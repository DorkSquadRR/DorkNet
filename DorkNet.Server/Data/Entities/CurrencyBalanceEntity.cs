namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Per-player wallet for a single currency. The 2020 client knows
/// about a small set of <c>CurrencyType</c>s (1=Coins, 2=Tokens, …);
/// each gets its own row keyed by (PlayerId, CurrencyType).
///
/// Updates go through <see cref="DorkNet.Server.Services.LevelService"/>
/// or admin grants; reads back through the
/// <c>storefronts/v4/balance/{currencyType}</c> endpoint.
/// </summary>
public class CurrencyBalanceEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public int CurrencyType { get; set; }
    public long Balance { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
