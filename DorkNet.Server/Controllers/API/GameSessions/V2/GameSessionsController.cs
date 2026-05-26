using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Models.GameSessions;
using DorkNet.Server.Auth;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.GameSessions.V2;

[ApiController]
[Route("api/[controller]/v2")]
[Authorize]
public class GameSessionsController(GameSessionService sessionService) : ControllerBase
{
    private long CurrentPlayerId => this.RequireCurrentPlayerId();

    [HttpPost("joinRandom")]
    public async Task<ActionResult<JoinGameSessionResponse>> JoinRandom([FromBody] JoinRandomGameSessionRequest req)
    {
        var session = await sessionService.JoinOrCreateAsync(req.RoomId, req.ActivityLevelId, req.RegionId);
        return Ok(new JoinGameSessionResponse
        {
            Result = JoinGameErrorCode.Success,
            GameSession = session,
        });
    }

    [HttpPost("joinById")]
    public async Task<ActionResult<JoinGameSessionResponse>> JoinById([FromBody] JoinByIdRequest req)
    {
        var session = await sessionService.GetByIdAsync(req.GameSessionId);
        if (session is null)
        {
            return Ok(new JoinGameSessionResponse { Result = JoinGameErrorCode.NotFound });
        }

        if (session.IsFull)
        {
            return Ok(new JoinGameSessionResponse { Result = JoinGameErrorCode.Full });
        }

        return Ok(new JoinGameSessionResponse
        {
            Result = JoinGameErrorCode.Success,
            GameSession = session,
        });
    }

    [HttpPost("leave")]
    public async Task<ActionResult> Leave([FromBody] long sessionId)
    {
        await sessionService.PlayerLeftAsync(sessionId);
        return Ok();
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<GameSession>> GetSession(long id)
    {
        var session = await sessionService.GetByIdAsync(id);
        if (session is null) return NotFound();
        return Ok(session);
    }
}

public class JoinByIdRequest
{
    public long GameSessionId { get; set; }
}
