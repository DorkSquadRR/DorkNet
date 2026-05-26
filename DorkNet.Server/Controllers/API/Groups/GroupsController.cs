using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Groups;

/// <summary>
/// api.rec.net/api/groups/* — clubs / groups. Wire types from
/// <c>RecNet.Group</c> + <c>RecNet.GroupMembership</c>:
///
/// • Group: <c>GroupId, Name, Description, CreatedAt, ImageName,
///   BanStatus, CreatorId, NumMembers, Members</c>.
/// • GroupMembership: <c>GroupId, PlayerId, Permissions</c>
///   (GroupMembershipPermissions flags).
///
/// URL surface (verb + template) verified against
/// <c>Cpp2IL_ISIL/.../RecNet/Groups.txt</c>:
///   GET    api/groups/v1/{id}
///   GET    api/groups/v1/name/{name}
///   GET    api/groups/v1/memberships/{playerId}
///   POST   api/groups/v1            (create — body CreateGroupRequest)
///   POST   api/groups/v1/delete/{id}
/// </summary>
[ApiController]
public class GroupsController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();
    private long? MeOrNull => this.CurrentPlayerId();

    [HttpGet("api/groups/v1/{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var g = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id);
        if (g is null) return NotFound();
        var members = await db.ClubMemberships
            .Where(m => m.ClubId == id)
            .ToListAsync();
        return Ok(ToWire(g, members));
    }

    [HttpGet("api/groups/v1/name/{name}")]
    public async Task<IActionResult> GetByName(string name)
    {
        var g = await db.Clubs.FirstOrDefaultAsync(c => c.Name == name);
        if (g is null) return NotFound();
        var members = await db.ClubMemberships
            .Where(m => m.ClubId == g.Id)
            .ToListAsync();
        return Ok(ToWire(g, members));
    }

    [HttpGet("api/groups/v1/memberships/{playerId:long}")]
    public async Task<IActionResult> Memberships(long playerId)
    {
        var rows = await (from m in db.ClubMemberships
                          join c in db.Clubs on m.ClubId equals c.Id
                          where m.PlayerId == playerId
                          select new { m, c }).ToListAsync();
        return Ok(rows.Select(r => new
        {
            GroupId = r.c.Id,
            PlayerId = (int)r.m.PlayerId,
            Permissions = r.m.Permissions,
            Group = ToWire(r.c, new List<ClubMembershipEntity>()),
        }));
    }

    public sealed class CreateGroupRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
    }

    [HttpPost("api/groups/v1")]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { Result = 1, Error = "name required" });
        var pid = Me;
        var dupe = await db.Clubs.AnyAsync(c => c.Name == req.Name);
        if (dupe) return Conflict(new { Result = 2, Error = "name taken" });

        var club = new ClubEntity
        {
            Name = req.Name.Trim(),
            Description = req.Description ?? string.Empty,
            ImageName = req.ImageName ?? string.Empty,
            CreatorPlayerId = pid,
            BanStatus = 0,
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        // Owner membership uses GroupMembershipPermissions.Owner = 127.
        db.ClubMemberships.Add(new ClubMembershipEntity
        {
            ClubId = club.Id,
            PlayerId = pid,
            Permissions = 127,
        });
        await db.SaveChangesAsync();
        return Ok(new
        {
            Result = 0,
            Group = ToWire(club, new List<ClubMembershipEntity>
            {
                new() { ClubId = club.Id, PlayerId = pid, Permissions = 127 },
            }),
        });
    }

    public sealed class UpdateGroupRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
    }

    /// <summary>POST <c>api/groups/v1/{id}</c> — owner-only update of
    /// description / image (per <c>RecNet.Groups.UpdateGroup</c> body
    /// shape). Mirrors the create response so the watch's
    /// <c>CreateModifyGroupResponse</c> deserialiser parses both.</summary>
    [HttpPost("api/groups/v1/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateGroupRequest req)
    {
        var pid = Me;
        var g = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id);
        if (g is null) return NotFound();
        if (g.CreatorPlayerId != pid) return Forbid();
        if (req.Description is not null) g.Description = req.Description;
        if (req.ImageName is not null) g.ImageName = req.ImageName;
        // Name updates are routed to the dedicated endpoint below
        // (uniqueness check + name index has to round-trip).
        g.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var members = await db.ClubMemberships.Where(m => m.ClubId == id).ToListAsync();
        return Ok(new { Result = 0, Group = ToWire(g, members) });
    }

    public sealed class UpdateGroupNameRequest { public string? Name { get; set; } }

    /// <summary>POST <c>api/groups/v1/name/{id}</c> — owner-only
    /// rename. Unique-name collision returns Result=2 to match the
    /// client's <c>CreateModifyGroupStatus</c> enum.</summary>
    [HttpPost("api/groups/v1/name/{id:long}")]
    [Authorize]
    public async Task<IActionResult> UpdateName(long id, [FromBody] UpdateGroupNameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { Result = 1, Error = "name required" });
        var pid = Me;
        var g = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id);
        if (g is null) return NotFound();
        if (g.CreatorPlayerId != pid) return Forbid();
        var dupe = await db.Clubs.AnyAsync(c => c.Id != id && c.Name == req.Name);
        if (dupe) return Conflict(new { Result = 2, Error = "name taken" });
        g.Name = req.Name.Trim();
        g.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var members = await db.ClubMemberships.Where(m => m.ClubId == id).ToListAsync();
        return Ok(new { Result = 0, Group = ToWire(g, members) });
    }

    [HttpPost("api/groups/v1/delete/{id:long}")]
    [HttpDelete("api/groups/v1/delete/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Delete(long id)
    {
        var pid = Me;
        var g = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id);
        if (g is null) return NotFound();
        if (g.CreatorPlayerId != pid) return Forbid();
        var memberships = await db.ClubMemberships.Where(m => m.ClubId == id).ToListAsync();
        db.ClubMemberships.RemoveRange(memberships);
        db.Clubs.Remove(g);
        await db.SaveChangesAsync();
        return Ok(new { Deleted = true });
    }

    [HttpPost("api/groups/v1/{id:long}/join")]
    [Authorize]
    public async Task<IActionResult> Join(long id)
    {
        var pid = Me;
        var g = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id);
        if (g is null) return NotFound();
        var existing = await db.ClubMemberships
            .FirstOrDefaultAsync(m => m.ClubId == id && m.PlayerId == pid);
        if (existing is not null) return Ok(new { Joined = true });
        db.ClubMemberships.Add(new ClubMembershipEntity
        {
            ClubId = id,
            PlayerId = pid,
            Permissions = 0, // Member
        });
        await db.SaveChangesAsync();
        return Ok(new { Joined = true });
    }

    [HttpPost("api/groups/v1/{id:long}/leave")]
    [Authorize]
    public async Task<IActionResult> Leave(long id)
    {
        var pid = Me;
        var g = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id);
        if (g is null) return NotFound();
        if (g.CreatorPlayerId == pid)
            return BadRequest(new { Error = "owner cannot leave; transfer ownership first" });
        var row = await db.ClubMemberships
            .FirstOrDefaultAsync(m => m.ClubId == id && m.PlayerId == pid);
        if (row is null) return NotFound();
        db.ClubMemberships.Remove(row);
        await db.SaveChangesAsync();
        return Ok(new { Left = true });
    }

    private static object ToWire(ClubEntity g, List<ClubMembershipEntity> members) => new
    {
        GroupId = g.Id,
        g.Name,
        g.Description,
        g.CreatedAt,
        g.ImageName,
        BanStatus = g.BanStatus,
        CreatorId = (int)g.CreatorPlayerId,
        NumMembers = members.Count,
        Members = members.Select(m => new
        {
            GroupId = g.Id,
            PlayerId = (int)m.PlayerId,
            Permissions = m.Permissions,
        }),
    };
}
