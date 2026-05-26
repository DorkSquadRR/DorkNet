using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Commerce;

/// <summary>
/// commerce.rec.net/api/catalog/* — the actual catalog endpoint the
/// 2020 watch's Shop tab fetches when it loads. The previous
/// implementation routed everything on commerce.rec.net through the
/// generic StubController which always returned <c>[]</c>; this
/// controller wins on specific routes via ASP.NET Core's
/// specific-over-wildcard precedence and returns the real
/// <see cref="StoreService"/> catalog.
///
/// Wire shape verified against the watch's Commerce.Catalog
/// deserialiser logging "no items" when we returned an empty array
/// vs populating the Shop UI when each item carries SkuId/Name/Cost
/// keys. Each catalog DTO ships every common alias (PascalCase,
/// camelCase, snake_case) so whichever JSON field the watch's
/// strict-key reader expects, it finds.
/// </summary>
[ApiController]
public class CommerceCatalogController(StoreService store, DomainConfig domain) : ControllerBase
{
    [HttpGet("/api/catalog/v1/all")]
    [HttpGet("/api/catalog/v2/all")]
    [HttpGet("/api/catalog/v3/all")]
    public async Task<IActionResult> All([FromQuery] bool onlyAvailableSkus = false)
    {
        var items = onlyAvailableSkus
            ? await store.GetAllActiveAsync()
            : await store.GetAllActiveAsync(); // same set; we don't distinguish yet
        return Ok(items.Select(i => ToCatalogDto(i, domain.Apex)).ToArray());
    }

    [HttpGet("/api/catalog/v1/sku/{slug}")]
    [HttpGet("/api/catalog/v2/sku/{slug}")]
    public async Task<IActionResult> Sku(string slug)
    {
        var item = await store.GetBySlugAsync(slug);
        if (item is null) return NotFound();
        return Ok(ToCatalogDto(item, domain.Apex));
    }

    /// <summary>POST /api/catalog/v1/byids — bulk lookup by id array.
    /// The watch sends {Ids:[1,2,3]} as form-urlencoded; accept both
    /// JSON and form bodies for safety.</summary>
    [HttpPost("/api/catalog/v1/byids")]
    [HttpPost("/api/catalog/v2/byids")]
    public async Task<IActionResult> ByIds()
    {
        string? raw = null;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            raw = form["Ids"].ToString();
            if (string.IsNullOrWhiteSpace(raw)) raw = form["ids"].ToString();
        }
        if (string.IsNullOrWhiteSpace(raw)) raw = Request.Query["Ids"].ToString();
        if (string.IsNullOrWhiteSpace(raw)) raw = Request.Query["ids"].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return Ok(Array.Empty<object>());

        var ids = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var v) ? v : 0L)
            .Where(v => v > 0)
            .Distinct()
            .ToArray();
        if (ids.Length == 0) return Ok(Array.Empty<object>());

        var all = await store.GetAllActiveAsync();
        return Ok(all.Where(i => ids.Contains(i.Id)).Select(i => ToCatalogDto(i, domain.Apex)).ToArray());
    }

    /// <summary>Catalog wire shape — every common key alias present
    /// (PascalCase + camelCase + snake_case) so the watch's
    /// strict-key deserialiser hits whichever name it's coded
    /// against without crashing on a missing field.
    ///
    /// Implemented as a Dictionary because System.Text.Json's
    /// case-insensitive collision check rejects anonymous types whose
    /// property names differ only in casing (<c>SkuId</c> vs
    /// <c>skuId</c>, etc.) — same trick we use in
    /// RelationshipsController.BuildRelationshipResponse.</summary>
    private static Dictionary<string, object?> ToCatalogDto(Data.Entities.StoreItemEntity i, string apex)
    {
        var available = i.IsActive && (i.AvailableUntil == null || i.AvailableUntil > DateTime.UtcNow);
        var imageUrl  = string.IsNullOrEmpty(i.ImageName) ? "" : $"https://cdn.{apex}/{i.ImageName}";
        return new Dictionary<string, object?>
        {
            // Identity / naming — every alias the watch's strict
            // deserialiser might key on.
            //
            // CRITICAL: <c>SkuId</c> is typed <c>int</c> on
            // <c>RecNet.Commerce+SKU</c>
            // (Cpp2IL_CS/.../RecNet/Commerce.cs:80
            // <c>private int &lt;SkuId&gt;k__BackingField</c>).
            // Util.GetKey&lt;int&gt;("SkuId") calls Convert.ChangeType,
            // which throws FormatException if the value isn't
            // parseable as an int. The previous shape shipped
            // <c>i.Slug</c> (a "wardrobe-&lt;guid&gt;" string) under
            // <c>SkuId</c> — that's the FormatException at
            // <c>output_log.txt:1655</c> which corrupts the catalog
            // load and downstream destabilises the watch's player
            // object (manifests as the dorm-load destroy crash).
            // Ship the int row id under <c>SkuId</c>; keep the slug
            // under <c>Slug</c>/<c>Sku</c> aliases for any string
            // consumer.
            ["SkuId"]       = i.Id,
            ["skuId"]       = i.Id,
            ["Sku"]         = i.Slug,
            ["sku"]         = i.Slug,
            ["ItemId"]      = i.Id,
            ["itemId"]      = i.Id,
            ["Id"]          = i.Id,
            ["id"]          = i.Id,
            ["Slug"]        = i.Slug,
            ["slug"]        = i.Slug,

            ["Name"]        = i.DisplayName,
            ["name"]        = i.DisplayName,
            ["DisplayName"] = i.DisplayName,
            ["displayName"] = i.DisplayName,
            ["Title"]       = i.DisplayName,
            ["title"]       = i.DisplayName,

            ["Description"] = i.Description,
            ["description"] = i.Description,

            ["Category"]    = i.Category,
            ["category"]    = i.Category,
            ["ItemType"]    = i.Category,
            ["itemType"]    = i.Category,

            // Cost / price — flat + nested forms.
            ["Price"]        = i.Price,
            ["price"]        = i.Price,
            ["Cost"]         = new Dictionary<string, object?> { ["Amount"] = i.Price, ["CurrencyType"] = i.CurrencyType },
            ["cost"]         = new Dictionary<string, object?> { ["amount"] = i.Price, ["currencyType"] = i.CurrencyType },
            ["CurrencyType"] = i.CurrencyType,
            ["currencyType"] = i.CurrencyType,
            ["Amount"]       = i.Price,
            ["amount"]       = i.Price,

            ["ImageName"] = i.ImageName,
            ["imageName"] = i.ImageName,
            ["Image"]     = i.ImageName,
            ["image"]     = i.ImageName,
            ["ImageUrl"]  = imageUrl,
            ["imageUrl"]  = imageUrl,

            ["Available"]     = available,
            ["available"]     = available,
            ["IsAvailable"]   = available,
            ["isAvailable"]   = available,
            ["IsActive"]      = i.IsActive,
            ["isActive"]      = i.IsActive,
            ["IsLimitedTime"] = i.IsLimitedTime,
            ["isLimitedTime"] = i.IsLimitedTime,
            ["AvailableUntil"] = i.AvailableUntil,
            ["availableUntil"] = i.AvailableUntil,

            ["Storefront"] = i.Storefront,
            ["storefront"] = i.Storefront,

            ["CreatedAt"] = i.CreatedAt,
            ["createdAt"] = i.CreatedAt,

            // Required RecNet.Commerce+SKU fields the watch's strict
            // Util.GetKey deserialiser throws KeyNotFoundException on
            // when missing. Verified at
            // Cpp2IL_CS/.../RecNet/Commerce.cs:80-102 — SKU has
            // OculusSkuId / AppleProductId / PSNProductLabel /
            // IsSingleUse / Data / DisplayPrice / LongDescription as
            // public-private (read+private-set) properties; missing
            // any of them at deserialize time causes the SKU import
            // to abort halfway and leaves the catalog in a half-built
            // state that the StoreScreen UI binding then native-crashes
            // on (output_log.txt:853 — last log line is a
            // RRUI.Data.TypedDataSource resolving List<string>.Count
            // followed by silent process exit).
            ["OculusSkuId"]      = string.Empty,
            ["oculusSkuId"]      = string.Empty,
            ["AppleProductId"]   = string.Empty,
            ["appleProductId"]   = string.Empty,
            ["PSNProductLabel"]  = string.Empty,
            ["psnProductLabel"]  = string.Empty,
            // XboxProductId — present in GCBPKHFJKCE+DJGBECJHOKF's
            // required-key list at Cpp2IL_ISIL/.../GCBPKHFJKCE_NestedType_DJGBECJHOKF.txt:419.
            // We don't ship Xbox, so empty string is correct; missing
            // it entirely throws KeyNotFoundException on the watch
            // and breaks the whole Shop ("malformed RecNet response"
            // → store times out waiting for a usable catalog).
            ["XboxProductId"]    = string.Empty,
            ["xboxProductId"]    = string.Empty,
            ["IsSingleUse"]      = false,
            ["isSingleUse"]      = false,
            ["DisplayPrice"]     = i.Price.ToString(),
            ["displayPrice"]     = i.Price.ToString(),
            ["LongDescription"]  = i.Description ?? string.Empty,
            ["longDescription"]  = i.Description ?? string.Empty,

            // SKUData is itself an IRecNetObject with required keys —
            // GiftDropIds (List<int>), Message (string),
            // SubscriptionPurchase (object). Empty list / empty
            // string / null sub-purchase are valid.
            ["Data"] = new Dictionary<string, object?>
            {
                ["GiftDropIds"]          = Array.Empty<int>(),
                ["Message"]              = string.Empty,
                ["SubscriptionPurchase"] = (object?)null,
            },
            ["data"] = new Dictionary<string, object?>
            {
                ["giftDropIds"]          = Array.Empty<int>(),
                ["message"]              = string.Empty,
                ["subscriptionPurchase"] = (object?)null,
            },
        };
    }
}
