using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.CustomAvatarItems;

[ApiController]
[Route("api/customAvatarItems")]
[Route("api/customAvatarItems/v1")]
[Route("api/customAvatarItems/v2")]
public class CustomAvatarItemsController(
    DorkNetDbContext db,
    LevelService level,
    DomainConfig domain,
    IObjectStorage storage,
    ILogger<CustomAvatarItemsController> logger) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(
        [FromQuery] string? q = null,
        [FromQuery] string? query = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var needle = (q ?? query ?? string.Empty).Trim().ToLowerInvariant();
        IQueryable<CustomAvatarItemEntity> rows = db.CustomAvatarItems
            .Where(i => i.IsPublic);
        if (needle.Length > 0)
        {
            rows = rows.Where(i =>
                i.Name.ToLower().Contains(needle) ||
                i.Description.ToLower().Contains(needle));
        }

        var result = await rows
            .OrderByDescending(i => i.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(result.Select(ToWire));
    }

    /// <summary>POST api/customAvatarItems/v1 — publish a shirt from the
    /// in-game designer. The collection route was GET-only, so every
    /// create 405'd and the designer's "publish" button could never work.
    ///
    /// The 2023-03-21 client sends <c>multipart/form-data</c> (verb ordinal
    /// rdx=2=POST at RecNet.Runtime/MPBLNLMCEDL.txt:1352-1354): a named
    /// param <c>"metadata"</c> carrying JsonConvert.SerializeObject of the
    /// DCKDCBDHDKI request object, then TWO file parts, <c>"thumbnailImage"</c>
    /// and <c>"design"</c>, both with filename <c>"file.bin"</c>
    /// (:1362-1390). DCKDCBDHDKI's field order is pinned by the arg-store
    /// sequence at :1096-1133 — +0x10 Name, +0x18 Description, +0x20 Price,
    /// +0x24 BaseAvatarItemId(int?), +0x30 Color ("#RRGGBB", built via
    /// ColorUtility.ToHtmlStringRGB at :1118-1131), +0x38 the permission
    /// enum we store as ItemType. The metadata JSON KEY names live in
    /// attribute metadata the dump doesn't render, so every field is read
    /// through an alias list rather than a single guessed key.</summary>
    [HttpPost]
    [Authorize]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded", "application/json")]
    public async Task<IActionResult> Create()
    {
        var pid = this.RequireCurrentPlayerId();
        var req = await ReadDesignRequestAsync();

        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length < 3) return BadRequest(new { error = "name_too_short" });
        if (name.Length > 128) name = name[..128];
        var description = (req.Description ?? string.Empty).Trim();
        if (description.Length > 1024) description = description[..1024];

        var item = new CustomAvatarItemEntity
        {
            CreatorPlayerId = pid,
            Name = name,
            Description = description,
            Price = Math.Max(MinPublicItemPrice, req.Price ?? MinPublicItemPrice),
            ItemType = req.ItemType ?? 0,
            BaseAvatarItemId = req.BaseAvatarItemId ?? 0,
            Color = Trim(req.Color, 64),
            // No boolean lives in DCKDCBDHDKI, so a freshly created item is
            // private until the permission enum (ItemType) says otherwise.
            IsPublic = req.IsPublic ?? false,
        };

        // "thumbnailImage" → ImageName (card art), "design" → AssetName (the
        // shirt texture itself). Fall back to positional order for any client
        // that names the parts differently.
        var (thumbnail, asset) = ReadItemFileParts();
        if (thumbnail is not null)
            item.ImageName = await StoreImageBlobAsync(pid, thumbnail, "custom item thumbnail");
        if (asset is not null)
            item.AssetName = await StoreImageBlobAsync(pid, asset, "custom item design");
        if (item.ImageName.Length == 0) item.ImageName = Trim(req.ImageName, 256);
        if (item.AssetName.Length == 0) item.AssetName = Trim(req.AssetName, 256);

        db.CustomAvatarItems.Add(item);
        await db.SaveChangesAsync();
        await EnsureOwnershipAsync(pid, item.Id);

        logger.LogInformation(
            "[customAvatarItems] created {PublicId} by {Pid} name='{Name}' image={Image} asset={Asset}",
            item.PublicId, pid, item.Name, item.ImageName, item.AssetName);
        return Ok(ToWire(item));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id)
    {
        var item = await db.CustomAvatarItems.FirstOrDefaultAsync(i => i.PublicId == id);
        if (item is null) return NotFound();
        if (!item.IsPublic && item.CreatorPlayerId != this.CurrentPlayerId()) return Forbid();
        return Ok(ToWire(item));
    }

    /// <summary>PUT api/customAvatarItems/v1/{id} — edit an existing item's
    /// metadata. Only GET was registered on the id route, so the details
    /// screen's Save button 405'd ("Unable to update custom avatar item").
    ///
    /// The client builds the URL with String.Format("{0}/v1/{1}") over
    /// "api/customAvatarItems" and issues verb ordinal 3 = PUT
    /// (RecNet.Runtime/MPBLNLMCEDL.txt:1899-1913). The body is RAW JSON —
    /// JsonConvert.SerializeObject of MOBHOFPIIIJ handed to
    /// BNDIAONDFFF.FJLLPHFOOJJ at :1890-1920 — NOT form-urlencoded, so this
    /// reads the body itself rather than model-binding it. MOBHOFPIIIJ
    /// carries exactly four properties whose accessors are shared with
    /// NOAAFNFJPFB (MOBHOFPIIIJ.txt:3-77): Name, Description, Price(int?)
    /// and the permission enum stored here as ItemType. Every field is
    /// optional — absent members leave the stored value alone.</summary>
    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Update(Guid id)
    {
        var pid = this.RequireCurrentPlayerId();
        var item = await db.CustomAvatarItems.FirstOrDefaultAsync(i => i.PublicId == id);
        if (item is null) return NotFound();
        if (item.CreatorPlayerId != pid) return Forbid();

        var req = await ReadDesignRequestAsync();
        if (req.Name is { } rawName)
        {
            var name = rawName.Trim();
            if (name.Length < 3) return BadRequest(new { error = "name_too_short" });
            item.Name = name.Length > 128 ? name[..128] : name;
        }
        if (req.Description is { } rawDescription)
        {
            var description = rawDescription.Trim();
            item.Description = description.Length > 1024 ? description[..1024] : description;
        }
        if (req.Price is { } price) item.Price = Math.Max(MinPublicItemPrice, price);
        if (req.ItemType is { } itemType) item.ItemType = itemType;
        if (req.BaseAvatarItemId is { } baseItemId) item.BaseAvatarItemId = baseItemId;
        if (req.Color is { } color) item.Color = Trim(color, 64);
        if (req.ImageName is { } imageName) item.ImageName = Trim(imageName, 256);
        if (req.AssetName is { } assetName) item.AssetName = Trim(assetName, 256);
        if (req.IsPublic is { } isPublic) item.IsPublic = isPublic;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(item));
    }

    /// <summary>DELETE api/customAvatarItems/v1/{id} — verb ordinal 4 on the
    /// same String.Format("{0}/v1/{1}") URL
    /// (RecNet.Runtime/MPBLNLMCEDL.txt:2188-2199); the client is
    /// fire-and-forget, so the missing route made deleted shirts silently
    /// reappear on the next list refresh.
    ///
    /// <see cref="CustomAvatarItemEntity"/> has no IsDeleted column (unlike
    /// UgcPurchasableEntity), and adding one is a schema migration, so this
    /// is a real delete: the ownership rows go first so nobody keeps the
    /// item in their wardrobe, then the item row itself.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var pid = this.RequireCurrentPlayerId();
        var item = await db.CustomAvatarItems.FirstOrDefaultAsync(i => i.PublicId == id);
        if (item is null) return Ok();
        if (item.CreatorPlayerId != pid) return Forbid();

        var ownership = await db.CustomAvatarItemOwnership
            .Where(o => o.CustomAvatarItemId == item.Id)
            .ToListAsync();
        if (ownership.Count > 0) db.CustomAvatarItemOwnership.RemoveRange(ownership);
        db.CustomAvatarItems.Remove(item);
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[customAvatarItems] deleted {PublicId} by {Pid} ({Owners} ownership rows removed)",
            id, pid, ownership.Count);
        return Ok();
    }

    /// <summary>GET/POST api/customAvatarItems/v1/bulk — resolve the custom
    /// items other players are wearing. The guid-list parameter key the
    /// client uses is the literal <c>"customAvatarItemIds"</c>
    /// (RecNet.Runtime/MPBLNLMCEDL_NestedType_KKEBBAAGHAM.txt:232-244, POST
    /// ordinal rdx=2), which no read path here honoured, and the old
    /// <c>[FromBody] BulkRequest</c> parameter made ASP.NET reject a
    /// form-urlencoded body with 415 before the handler ran. Result: an
    /// empty array, remote players' shirts never rendered, and the client
    /// re-queried the same ids forever — the same failure the ugcPurchasables
    /// bulk endpoint hit. The binding is gone; <see cref="ReadIdsAsync"/>
    /// now reads the key from the JSON body, the form and the query.</summary>
    [HttpGet("bulk")]
    [HttpPost("bulk")]
    [AllowAnonymous]
    public async Task<IActionResult> Bulk()
    {
        var ids = await ReadIdsAsync();
        if (ids.Count == 0) return Ok(Array.Empty<object>());
        var rows = await db.CustomAvatarItems
            .Where(i => ids.Contains(i.PublicId) && (i.IsPublic || i.CreatorPlayerId == this.CurrentPlayerId()))
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("featured")]
    [AllowAnonymous]
    public async Task<IActionResult> Featured([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.CustomAvatarItems
            .Where(i => i.IsPublic && i.IsFeatured)
            .OrderByDescending(i => i.UpdatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("hot")]
    [AllowAnonymous]
    public async Task<IActionResult> Hot([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.CustomAvatarItems
            .Where(i => i.IsPublic)
            .OrderByDescending(i => i.CheerCount)
            .ThenByDescending(i => i.PurchaseCount)
            .ThenByDescending(i => i.UpdatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("new")]
    [AllowAnonymous]
    public async Task<IActionResult> New([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.CustomAvatarItems
            .Where(i => i.IsPublic)
            .OrderByDescending(i => i.CreatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("fromCreator/{creatorId:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> FromCreator(
        long creatorId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);
        var currentPlayerId = this.CurrentPlayerId();
        var query = db.CustomAvatarItems
            .Where(i => i.CreatorPlayerId == creatorId
                        && (i.IsPublic || i.CreatorPlayerId == currentPlayerId));
        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(i => i.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        // Paged container, not a bare array — the client deserializes this into
        // CKFLFJCNEPH {Results, TotalResults}, same as Owned() below.
        return Ok(new { Results = rows.Select(ToWire), TotalResults = total });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me([FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var pid = this.RequireCurrentPlayerId();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var createdQuery = db.CustomAvatarItems
            .Where(i => i.CreatorPlayerId == pid)
            .OrderByDescending(i => i.UpdatedAt);
        var total = await createdQuery.CountAsync();
        var created = await createdQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        var ownedIds = await db.CustomAvatarItemOwnership
            .Where(o => o.PlayerId == pid)
            .Select(o => o.CustomAvatarItemId)
            .ToListAsync();
        var owned = ownedIds.Count == 0
            ? new List<CustomAvatarItemEntity>()
            : await db.CustomAvatarItems
                .Where(i => ownedIds.Contains(i.Id))
                .OrderByDescending(i => i.UpdatedAt)
                .ToListAsync();

        return Ok(new
        {
            Results = created.Select(ToWire),
            TotalResults = total,
            Created = created.Select(ToWire),
            Owned = owned.Select(ToWire),
        });
    }

    [HttpGet("owned")]
    [Authorize]
    public async Task<IActionResult> Owned([FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var pid = this.RequireCurrentPlayerId();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var query = db.CustomAvatarItemOwnership
            .Where(o => o.PlayerId == pid)
            .Join(db.CustomAvatarItems, o => o.CustomAvatarItemId, i => i.Id, (o, i) => i)
            .OrderByDescending(i => i.UpdatedAt);
        var total = await query.CountAsync();
        var rows = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(new
        {
            Results = rows.Select(ToWire),
            TotalResults = total,
        });
    }

    [HttpGet("isCreationAllowedForAccount")]
    [Authorize]
    public async Task<IActionResult> IsCreationAllowedForAccount()
    {
        var pid = this.RequireCurrentPlayerId();
        var player = await db.Players
            .Where(p => p.Id == pid)
            .Select(p => new { p.IsJunior, p.BannedUntil })
            .FirstOrDefaultAsync();
        var allowed = player is not null
            && !player.IsJunior
            && (player.BannedUntil is null || player.BannedUntil <= DateTime.UtcNow);
        // Bare JSON boolean, like the two sibling gates below: the client's
        // issuing method is FGLDKEJLAKB<System.Boolean> GGMDLMLJDDK()
        // (RecNet.Runtime/MPBLNLMCEDL.txt:6048, route literal on :6205), so an
        // object body threw and the can-create gate blocked shirt creation.
        return Content(allowed ? "true" : "false", "application/json");
    }

    [HttpGet("isCreationEnabled")]
    [AllowAnonymous]
    public IActionResult IsCreationEnabled() => Content("true", "application/json");

    [HttpGet("isRenderingEnabled")]
    [AllowAnonymous]
    public IActionResult IsRenderingEnabled() => Content("true", "application/json");

    /// <summary>The client DTO (IHLMDLKOLNB) is an object with exactly one
    /// Int32 property, but its accessor name (GFGCCNBDKAK) appears nowhere
    /// else in the dump, so — unlike Price/BaseAvatarItemId/Color, which are
    /// pinned by accessors shared with NOAAFNFJPFB — the JSON key is NOT
    /// recoverable. All four spellings below are UNPROVEN hedges; Newtonsoft
    /// ignores the ones that don't match, and a total miss falls back to the
    /// DTO's static default (IHLMDLKOLNB.txt:75-117) rather than throwing.</summary>
    [HttpGet("minPriceForPublicItem")]
    [AllowAnonymous]
    public IActionResult MinPriceForPublicItem() => Ok(new
    {
        MinPrice = MinPublicItemPrice,
        Price = MinPublicItemPrice,
        MinimumPrice = MinPublicItemPrice,
        MinPriceForPublicItem = MinPublicItemPrice,
    });

    /// <summary>GET api/customAvatarItems/v1/design — hand back the caller's
    /// saved in-progress shirt design. Verb ordinal 0 = GET
    /// (RecNet.Runtime/MPBLNLMCEDL.txt:4782-4783); only POST was registered,
    /// so the designer logged "Unable to get custom avatar design info" every
    /// time it opened.
    ///
    /// The response DTO NCMMAGDIBND is {Int32 @0x10, Int32? @0x14, String
    /// @0x20, String @0x28} (NCMMAGDIBND.txt:3-145). The obfuscator's name
    /// map is per-property-name rather than per-member, so those four
    /// accessors identify the properties: IDELLAGIFAE/MOKDHHGDMJB is the same
    /// pair used for BaseAvatarItemId on GIOBJIGEMNG and DCKDCBDHDKI,
    /// GJEFKPNEFGI is the Color set from ColorUtility.ToHtmlStringRGB, and
    /// DPJMMBLGKGF / BADCCGPNLMN are the leading int and the first string of
    /// NOAAFNFJPFB — the creator id and the image name. Hence
    /// {CreatorPlayerId, BaseAvatarItemId, ImageName, Color}. The extra keys
    /// are inert aliases.
    ///
    /// Returns an empty design rather than 404 when nothing is saved so the
    /// designer opens clean instead of showing the error toast.</summary>
    [HttpGet("design")]
    [Authorize]
    public async Task<IActionResult> GetDesign()
    {
        var pid = this.RequireCurrentPlayerId();
        var design = await LoadDesignAsync(pid) ?? new StoredDesign();
        return Ok(new
        {
            CreatorPlayerId = (int)pid,
            CreatorId = (int)pid,
            design.BaseAvatarItemId,
            design.ImageName,
            ImageUrl = string.IsNullOrWhiteSpace(design.ImageName)
                ? string.Empty
                : $"https://{domain.Sub("cdn")}/{design.ImageName}",
            design.Color,
        });
    }

    /// <summary>PUT api/customAvatarItems/v1/design — save the in-progress
    /// shirt texture. The route only accepted POST, so the designer could
    /// never persist a design ("Unable to save custom avatar design").
    ///
    /// Verb ordinal 3 = PUT with a multipart body: named param
    /// <c>"metadata"</c> plus a file part <c>"design"</c> with filename
    /// <c>"file.bin"</c>
    /// (RecNet.Runtime/MPBLNLMCEDL_NestedType_OPEGIAOIHMA.txt:200-227). The
    /// metadata object GIOBJIGEMNG holds just two properties — a
    /// Nullable&lt;Int32&gt; base avatar item id at +0x10 and the "#RRGGBB"
    /// colour string at +0x18 (GIOBJIGEMNG.txt:3-43), written from the
    /// FKCJDHHJAFF(texture, color, baseItemId) args at MPBLNLMCEDL.txt:4652-4654.
    /// The client's return type is status-only (LDGADANDBIO), so a bare 200
    /// is the whole contract.</summary>
    [HttpPut("design")]
    [Authorize]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded", "application/json")]
    public async Task<IActionResult> SaveDesign()
    {
        var pid = this.RequireCurrentPlayerId();
        var req = await ReadDesignRequestAsync();
        var existing = await LoadDesignAsync(pid);

        var design = new StoredDesign
        {
            BaseAvatarItemId = req.BaseAvatarItemId ?? existing?.BaseAvatarItemId,
            Color = Trim(req.Color, 64) is { Length: > 0 } color ? color : existing?.Color ?? string.Empty,
            ImageName = existing?.ImageName ?? string.Empty,
        };

        // The texture arrives as the "design" part; accept any single file
        // part as a fallback for a client that names it differently.
        var file = Request.HasFormContentType
            ? Request.Form.Files.GetFile("design") ?? Request.Form.Files.FirstOrDefault()
            : null;
        if (file is not null && file.Length > 0)
        {
            var bytes = await ReadFileBytesAsync(file);
            var blobName = DesignImageBlobName(pid);
            await PutBlobAsync(blobName, bytes, SniffImageContentType(bytes), "custom avatar design");
            design.ImageName = blobName;
        }

        await SaveDesignAsync(pid, design);
        return Ok();
    }

    /// <summary>DELETE api/customAvatarItems/v1/design — discard the saved
    /// design. Verb ordinal 4 on the same literal
    /// (RecNet.Runtime/MPBLNLMCEDL.txt:4862-4864); without it the client
    /// logged "Unable to delete custom avatar item design".
    ///
    /// <see cref="IObjectStorage"/> has no delete operation, so the record is
    /// tombstoned (a Deleted marker that <see cref="LoadDesignAsync"/> reads
    /// as "no design") and the texture object is truncated to zero bytes so
    /// the pixels really are gone.</summary>
    [HttpDelete("design")]
    [Authorize]
    public async Task<IActionResult> DeleteDesign()
    {
        var pid = this.RequireCurrentPlayerId();
        await SaveDesignAsync(pid, new StoredDesign { Deleted = true });
        await PutBlobAsync(DesignImageBlobName(pid), Array.Empty<byte>(), "image/png", "custom avatar design");
        return Ok();
    }

    [HttpPost("design")]
    [Authorize]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> Design()
    {
        var pid = this.RequireCurrentPlayerId();
        var req = await ReadDesignRequestAsync();
        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length < 3) return BadRequest(new { error = "name_too_short" });
        if (name.Length > 128) name = name[..128];

        var description = (req.Description ?? string.Empty).Trim();
        if (description.Length > 1024) description = description[..1024];

        CustomAvatarItemEntity item;
        if (req.Id is Guid id)
        {
            item = await db.CustomAvatarItems.FirstOrDefaultAsync(i => i.PublicId == id)
                   ?? new CustomAvatarItemEntity { PublicId = id, CreatorPlayerId = pid };
            if (item.Id != 0 && item.CreatorPlayerId != pid) return Forbid();
            if (item.Id == 0) db.CustomAvatarItems.Add(item);
        }
        else
        {
            item = new CustomAvatarItemEntity { CreatorPlayerId = pid };
            db.CustomAvatarItems.Add(item);
        }

        item.Name = name;
        item.Description = description;
        item.Price = Math.Max(100, req.Price ?? item.Price);
        item.ItemType = req.ItemType ?? item.ItemType;
        item.BaseAvatarItemId = req.BaseAvatarItemId ?? item.BaseAvatarItemId;
        item.Color = Trim(req.Color, 64);
        item.ImageName = Trim(req.ImageName, 256);
        item.AssetName = Trim(req.AssetName, 256);
        item.IsPublic = req.IsPublic ?? item.IsPublic;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await EnsureOwnershipAsync(pid, item.Id);
        return Ok(ToWire(item));
    }

    /// <summary>The 2023-03-21 client buys custom items through the
    /// <c>econ/</c> host prefix — <c>econ/customAvatarItems/v1/{id}/purchase</c>
    /// — which was never registered, so every custom-shirt purchase 404'd. The
    /// rooted route below is an alias onto the same handler. <c>requestedPrice</c>
    /// arrives as a query param and is validated against the item's real price
    /// so a tampered client can't buy at its own number.</summary>
    [HttpPost("/econ/customAvatarItems/v1/{id:guid}/purchase")]
    [HttpPost("{id:guid}/purchase")]
    [Authorize]
    public async Task<IActionResult> Purchase(Guid id)
    {
        var pid = this.RequireCurrentPlayerId();
        var item = await db.CustomAvatarItems.FirstOrDefaultAsync(i => i.PublicId == id && i.IsPublic);
        if (item is null) return NotFound();
        if (await db.CustomAvatarItemOwnership.AnyAsync(o => o.PlayerId == pid && o.CustomAvatarItemId == item.Id))
            return Ok(new { Success = true, AlreadyOwned = true, Item = ToWire(item) });

        // requestedPrice is baked into the URL the client formats —
        // "{0}/v1/{1}/purchase/?requestedPrice={2}"
        // (RecNet.Runtime/MPBLNLMCEDL.txt:5084). It is the price the player was
        // SHOWN, so a value below the real price means the listing moved under
        // them (or the request was tampered with); reject instead of silently
        // charging more than the confirmation dialog said.
        var requestedPrice = int.TryParse(Request.Query["requestedPrice"].FirstOrDefault(), out var rp) ? rp : (int?)null;
        if (requestedPrice is { } shown && shown < item.Price)
            return Ok(new { Success = false, Error = "price_changed", Price = item.Price, Item = ToWire(item) });

        var balance = await level.GetBalanceAsync(pid, 2);
        if (balance < item.Price)
            return Ok(new { Success = false, Error = "insufficient_funds", Balance = balance });

        var newBalance = await level.GrantCurrencyAsync(pid, 2, -item.Price, $"customAvatarItem:{item.PublicId}");
        await EnsureOwnershipAsync(pid, item.Id);
        item.PurchaseCount += 1;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Balance = newBalance, Item = ToWire(item) });
    }

    private async Task EnsureOwnershipAsync(long playerId, long itemId)
    {
        if (await db.CustomAvatarItemOwnership.AnyAsync(o =>
            o.PlayerId == playerId && o.CustomAvatarItemId == itemId))
        {
            return;
        }

        db.CustomAvatarItemOwnership.Add(new CustomAvatarItemOwnershipEntity
        {
            PlayerId = playerId,
            CustomAvatarItemId = itemId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Collect the bulk request's guid list. The 2023 client's key is
    /// <c>"customAvatarItemIds"</c> (MPBLNLMCEDL_NestedType_KKEBBAAGHAM.txt:242);
    /// older callers use ids/itemIds. Which transport the request builder picks
    /// for a POST collection param isn't decidable from ISIL, so all three are
    /// read: raw JSON body, form fields and query pairs. The body is read
    /// manually — a [FromBody] parameter would 415 a form-encoded POST before
    /// the handler ran.</summary>
    private async Task<List<Guid>> ReadIdsAsync()
    {
        var result = new List<Guid>();
        void AddCsv(string? value)
        {
            foreach (var part in (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Guid.TryParse(part, out var id)) result.Add(id);
        }

        if ((Request.ContentLength ?? 0) > 0
            && Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                var root = doc.RootElement;
                // Either a bare guid array or an object carrying one under any
                // of the accepted key spellings.
                if (root.ValueKind == JsonValueKind.Array)
                {
                    AddJsonGuids(root, result);
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (!IdListKeys.Contains(prop.Name)) continue;
                        if (prop.Value.ValueKind == JsonValueKind.Array) AddJsonGuids(prop.Value, result);
                        else if (prop.Value.ValueKind == JsonValueKind.String) AddCsv(prop.Value.GetString());
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        foreach (var pair in Request.Query)
        foreach (var value in pair.Value)
            AddCsv(value);

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var field in form)
            {
                if (!IdListKeys.Contains(field.Key)) continue;
                foreach (var value in field.Value) AddCsv(value);
            }
        }

        return result.Distinct().Take(200).ToList();
    }

    private static void AddJsonGuids(JsonElement array, List<Guid> into)
    {
        foreach (var el in array.EnumerateArray())
            if (el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var id))
                into.Add(id);
    }

    /// <summary>Accepted spellings of the bulk guid-list key. Case-insensitive
    /// set so "customAvatarItemIds" / "CustomAvatarItemIds" both land.</summary>
    private static readonly HashSet<string> IdListKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "customAvatarItemIds", "ids", "itemIds",
    };

    /// <summary>Read the fields of a create / update / design-save request.
    /// Three encodings have to work: the designer's multipart bodies wrap
    /// everything in a single <c>"metadata"</c> form field holding JSON
    /// (MPBLNLMCEDL.txt:1362 and MPBLNLMCEDL_NestedType_OPEGIAOIHMA.txt:210),
    /// PUT {id} sends a raw JSON body, and older/admin callers post flat form
    /// fields. The JSON key names are not in the dump — they live in attribute
    /// metadata Cpp2IL doesn't render — so each field is matched against an
    /// alias list rather than one guessed spelling. Only the field ORDER is
    /// proven (from the arg-store sequence); the names are inferred.</summary>
    private async Task<DesignRequest> ReadDesignRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var metadata = form["metadata"].FirstOrDefault() ?? form["Metadata"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(metadata))
                return ParseDesignJson(metadata);

            string? Field(params string[] keys)
            {
                foreach (var key in keys)
                    if (form[key].FirstOrDefault() is { } v) return v;
                return null;
            }

            return new DesignRequest
            {
                Id = Guid.TryParse(Field("id", "Id"), out var id) ? id : null,
                Name = Field("name", "Name"),
                Description = Field("description", "Description"),
                Price = int.TryParse(Field("price", "Price"), out var price) ? price : null,
                ItemType = int.TryParse(Field("itemType", "ItemType", "permission", "Permission"), out var itemType) ? itemType : null,
                BaseAvatarItemId = int.TryParse(Field("baseAvatarItemId", "BaseAvatarItemId"), out var baseId) ? baseId : null,
                Color = Field("color", "Color"),
                ImageName = Field("imageName", "ImageName"),
                AssetName = Field("assetName", "AssetName"),
                IsPublic = bool.TryParse(Field("isPublic", "IsPublic"), out var pub) ? pub : null,
            };
        }

        using var reader = new StreamReader(Request.Body);
        return ParseDesignJson(await reader.ReadToEndAsync());
    }

    /// <summary>Lenient JSON → <see cref="DesignRequest"/>. Members that are
    /// absent stay null so callers can tell "not sent" from "sent empty" —
    /// PUT {id} relies on that to leave untouched fields alone.</summary>
    private static DesignRequest ParseDesignJson(string json)
    {
        var req = new DesignRequest();
        if (string.IsNullOrWhiteSpace(json)) return req;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return req;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value;
                switch (prop.Name.ToLowerInvariant())
                {
                    case "id" or "customavataritemid":
                        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id)) req.Id = id;
                        break;
                    case "name" or "itemname" or "title":
                        req.Name = AsString(value);
                        break;
                    case "description" or "desc":
                        req.Description = AsString(value);
                        break;
                    case "price":
                        req.Price = AsInt(value);
                        break;
                    // The enum at DCKDCBDHDKI+0x38 / NOAAFNFJPFB+0x3C is the
                    // item's permission (the 06.21 dump names its config row
                    // CustomAvatarItemPermissionFriendlyNameMapping); this
                    // schema calls that column ItemType, hence both aliases.
                    case "itemtype" or "type" or "permission" or "avataritemtype":
                        req.ItemType = AsInt(value);
                        break;
                    case "baseavataritemid" or "baseitemid" or "avataritemid":
                        req.BaseAvatarItemId = AsInt(value);
                        break;
                    case "color" or "colorhex" or "tint":
                        req.Color = AsString(value);
                        break;
                    case "imagename" or "thumbnail" or "thumbnailimage":
                        req.ImageName = AsString(value);
                        break;
                    case "assetname" or "design" or "designname":
                        req.AssetName = AsString(value);
                        break;
                    case "ispublic" or "public":
                        req.IsPublic = value.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.String => bool.TryParse(value.GetString(), out var b) ? b : null,
                            _ => null,
                        };
                        break;
                }
            }
        }
        catch (JsonException)
        {
        }

        return req;
    }

    private static string? AsString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.ToString(),
        _ => null,
    };

    private static int? AsInt(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.TryGetInt32(out var n) ? n : null,
        JsonValueKind.String => int.TryParse(value.GetString(), out var s) ? s : null,
        _ => null,
    };

    /// <summary>Pick out the create request's two file parts. Named lookup
    /// first ("thumbnailImage" / "design", both sent with filename
    /// "file.bin" — MPBLNLMCEDL.txt:1372-1390), then positional order as a
    /// fallback.</summary>
    private (IFormFile? Thumbnail, IFormFile? Asset) ReadItemFileParts()
    {
        if (!Request.HasFormContentType) return (null, null);
        var files = Request.Form.Files;
        if (files.Count == 0) return (null, null);
        var thumbnail = files.GetFile("thumbnailImage") ?? files.GetFile("thumbnail");
        var asset = files.GetFile("design") ?? files.GetFile("asset");
        if (thumbnail is null && asset is null)
        {
            thumbnail = files[0];
            asset = files.Count > 1 ? files[1] : null;
        }

        return (thumbnail?.Length > 0 ? thumbnail : null, asset?.Length > 0 ? asset : null);
    }

    /// <summary>Persist one uploaded image part and return its BlobName. Same
    /// shape as the images controller: S3 (or the disk fallback) holds the
    /// bytes, a RoomDataBlobs row keeps the uploader audit trail, and the
    /// cdn serves it straight off the BlobName.</summary>
    private async Task<string> StoreImageBlobAsync(long playerId, IFormFile file, string what)
    {
        var bytes = await ReadFileBytesAsync(file);
        var contentType = SniffImageContentType(bytes);
        var blobName = $"img_p{playerId}_{Guid.NewGuid():N}.{(contentType == "image/jpeg" ? "jpg" : "png")}";
        await PutBlobAsync(blobName, bytes, contentType, what);
        db.RoomDataBlobs.Add(new RoomDataBlobEntity
        {
            RoomId = 0,
            BlobName = blobName,
            UploadedByPlayerId = playerId,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = string.Empty,
        });
        return blobName;
    }

    private static async Task<byte[]> ReadFileBytesAsync(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>The parts are sent with filename "file.bin" and no usable
    /// content type, so sniff the magic bytes instead of trusting either.</summary>
    private static string SniffImageContentType(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 ? "image/png"
        : bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? "image/jpeg"
        : "image/png";

    private async Task PutBlobAsync(string blobName, byte[] bytes, string contentType, string what)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var (bucket, key) = BlobRouter.Route(blobName);
            await storage.PutAsync(bucket, key, bytes, contentType, timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[customAvatarItems] storing {What} blob {Blob} failed", what, blobName);
        }
    }

    // ── In-progress shirt design ────────────────────────────────────────────
    // The designer's draft (base item + colour + texture) has no table in this
    // schema and adding one is a migration, so it lives in object storage
    // under two deterministic per-player blob names: overwrite-in-place on
    // save, tombstoned on delete.
    private static string DesignMetaBlobName(long playerId) => $"caidesign_p{playerId}.json";

    private static string DesignImageBlobName(long playerId) => $"caidesign_p{playerId}.png";

    private async Task<StoredDesign?> LoadDesignAsync(long playerId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var (bucket, key) = BlobRouter.Route(DesignMetaBlobName(playerId));
            var bytes = await storage.GetAsync(bucket, key, timeout.Token);
            if (bytes is null || bytes.Length == 0) return null;
            var design = JsonSerializer.Deserialize<StoredDesign>(
                bytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return design is null || design.Deleted ? null : design;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "[customAvatarItems] unreadable saved design for player {Pid}", playerId);
            return null;
        }
    }

    private Task SaveDesignAsync(long playerId, StoredDesign design) =>
        PutBlobAsync(
            DesignMetaBlobName(playerId),
            JsonSerializer.SerializeToUtf8Bytes(design),
            "application/json",
            "custom avatar design record");

    private object ToWire(CustomAvatarItemEntity item) => new
    {
        Id = item.PublicId,
        CustomAvatarItemId = item.PublicId,
        CreatorId = (int)item.CreatorPlayerId,
        CreatorPlayerId = (int)item.CreatorPlayerId,
        item.Name,
        item.Description,
        item.Price,
        item.ItemType,
        item.BaseAvatarItemId,
        item.Color,
        item.ImageName,
        ImageUrl = string.IsNullOrWhiteSpace(item.ImageName) ? string.Empty : $"https://{domain.Sub("cdn")}/{item.ImageName}",
        item.AssetName,
        item.IsPublic,
        item.IsFeatured,
        item.CheerCount,
        item.PurchaseCount,
        item.CreatedAt,
        item.UpdatedAt,
        Status = item.IsPublic ? 1 : 0,
    };

    private static string Trim(string? value, int max)
    {
        var s = (value ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }

    public sealed class BulkRequest
    {
        public List<Guid>? Ids { get; set; }
        public List<Guid>? ItemIds { get; set; }
    }

    public sealed class DesignRequest
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? Price { get; set; }
        public int? ItemType { get; set; }
        public int? BaseAvatarItemId { get; set; }
        public string? Color { get; set; }
        public string? ImageName { get; set; }
        public string? AssetName { get; set; }
        public bool? IsPublic { get; set; }
    }
}
