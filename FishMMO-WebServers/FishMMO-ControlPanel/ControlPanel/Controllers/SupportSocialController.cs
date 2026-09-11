using FishMMO.ControlPanel.Auth;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Read-only views of guilds and parties for support staff.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Every route here is a GET and there is no write counterpart anywhere in the panel.</b>
	/// Guild and party membership is game state: it is changed by players in the world, held in
	/// the memory of the scene server that owns the characters, and replicated to the database
	/// through the update tables. A row edited here would be overwritten by that server's next
	/// save, or — worse — would not be, leaving the roster on screen and the roster in the game
	/// permanently disagreeing. Support looks; the game decides.
	/// </para>
	/// <para>
	/// <b>Nothing here is audited, deliberately.</b> The audit log records privileged actions,
	/// and reads are not actions: a log in which "looked at a guild" outnumbers every ban and
	/// every rename by a thousand to one is a log nobody can read. Every route that changes
	/// something carries <c>[Audited]</c>; none of these change anything.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/support/social")]
	public sealed class SupportSocialController : ControllerBase
	{
		private readonly ISocialBoardService social;

		public SupportSocialController(ISocialBoardService social)
		{
			this.social = social;
		}

		/// <summary>Searches guilds by name prefix.</summary>
		[HttpGet("guilds")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> SearchGuilds(
			[FromQuery] string? query,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 25)
		{
			var result = await social.SearchGuildsAsync(query ?? string.Empty, page, pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return BadRequest(new { error = result.ErrorMessage ?? "That search could not be run." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(g => new
				{
					id = g.ID,
					name = g.Name,
					memberCount = g.MemberCount,
					rankCount = g.RankCount,
					// Null when the guild has nobody in it, or no ranks to lead.
					leaderName = g.LeaderName,
					leaderCharacterId = g.LeaderCharacterID,
					leaderIsAmbiguous = g.LeaderIsAmbiguous,
					isRecruiting = g.IsRecruiting,
					tags = g.Tags,
					blurb = g.Blurb,
					createdUtc = g.TimeCreated,
					version = g.Version,
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>One guild: the row, its rank ladder, its roster and its pending applications.</summary>
		[HttpGet("guilds/{id:long}")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> GetGuild(long id)
		{
			var result = await social.FetchGuildAsync(id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				/* A guild that does not exist and a guild that could not be read are different
				 * answers: the first is the operator following a stale link, the second is the
				 * database being unreachable, and telling them apart is the difference between
				 * "this guild was disbanded" and "try again". */
				if (result.ErrorCode == DatabaseErrorCodes.NotFound)
				{
					return NotFound(new { error = "No such guild." });
				}
				return BadRequest(new { error = result.ErrorMessage ?? "That guild could not be read." });
			}

			var g = result.Data;
			return Ok(new
			{
				id = g.ID,
				name = g.Name,
				notice = g.Notice,
				messageOfTheDay = g.MessageOfTheDay,
				blurb = g.Blurb,
				tags = g.Tags,
				isRecruiting = g.IsRecruiting,
				createdUtc = g.TimeCreated,
				version = g.Version,
				memberCount = g.Members.Count,
				ranks = g.Ranks.Select(r => new
				{
					id = r.ID,
					order = r.RankOrder,
					name = r.Name,
					/* The raw mask. The flag names live in the game's GuildPermissions enum,
					 * which is a Unity assembly this one cannot reference, so the client decodes
					 * it — a second copy of the enum here would drift from the first in silence. */
					permissions = r.Permissions,
					memberCount = r.MemberCount,
					createdUtc = r.TimeCreated,
				}),
				members = g.Members.Select(m => new
				{
					id = m.ID,
					characterId = m.CharacterID,
					name = m.CharacterName,
					deleted = m.CharacterDeleted,
					level = m.Level,
					online = IsOnline(m.SessionState, m.SessionLeaseExpiresUtc, m.CharacterDeleted),
					rank = m.Rank,
					// Null when the membership points at a rung the ladder no longer defines.
					rankName = m.RankName,
					isLeader = m.IsLeader,
					location = m.Location,
					publicNote = m.PublicNote,
					joinedUtc = m.TimeCreated,
				}),
				applications = g.Applications.Select(a => new
				{
					id = a.ID,
					characterId = a.CharacterID,
					name = a.CharacterName,
					level = a.Level,
					message = a.Message,
					appliedUtc = a.TimeCreated,
				}),
			});
		}

		/// <summary>One guild's activity log, newest first.</summary>
		[HttpGet("guilds/{id:long}/log")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> GetGuildLog(
			long id,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 25)
		{
			var result = await social.FetchGuildLogAsync(id, page, pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return BadRequest(new { error = result.ErrorMessage ?? "That guild log could not be read." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(l => new
				{
					id = l.ID,
					guildId = l.GuildID,
					/* Both the number and the name. The name is the enum's own, read from the
					 * database assembly rather than spelled out again here, so a new event kind
					 * cannot arrive labelled as something it is not — and the number is sent
					 * beside it so an unrecognised kind still shows as itself. */
					eventType = (int)l.EventType,
					eventName = l.EventType.ToString(),
					actorCharacterId = l.ActorCharacterID,
					// Null for the log's "nobody", and for a character that no longer exists.
					actorName = l.ActorName,
					targetCharacterId = l.TargetCharacterID,
					targetName = l.TargetName,
					detail = l.Detail,
					occurredUtc = l.TimeCreated,
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>Live parties, newest first, each with its roster.</summary>
		[HttpGet("parties")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> GetParties(
			[FromQuery] long? worldServerId,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 25)
		{
			var result = await social.FetchPartiesAsync(worldServerId, page, pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return BadRequest(new { error = result.ErrorMessage ?? "Parties could not be read." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(p => new
				{
					id = p.ID,
					worldServerId = p.WorldServerID,
					memberCount = p.MemberCount,
					createdUtc = p.TimeCreated,
					version = p.Version,
					members = p.Members.Select(m => new
					{
						id = m.ID,
						characterId = m.CharacterID,
						name = m.CharacterName,
						deleted = m.CharacterDeleted,
						level = m.Level,
						sceneName = m.SceneName,
						online = IsOnline(m.SessionState, m.SessionLeaseExpiresUtc, m.CharacterDeleted),
						// PartyRank: 0 none, 1 member, 2 leader. The client names them.
						rank = m.Rank,
						healthPct = m.HealthPCT,
						joinedUtc = m.TimeCreated,
					}),
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>
		/// Whether a character is really in the world, decided the way the character editor
		/// decides it.
		/// </summary>
		/// <remarks>
		/// The session flag alone is not presence — a scene server that crashed leaves it set
		/// behind — so the lease has to still be running for it to mean anything. This is
		/// <see cref="Services.CharacterEditLock"/>'s test with the same reasoning; it is spelled
		/// out rather than shared because that type needs a whole <see cref="CharacterAdminData"/>
		/// and a guild roster row is not one.
		/// </remarks>
		private static bool IsOnline(int sessionState, DateTime leaseExpiresUtc, bool deleted)
		{
			return !deleted && sessionState == 1 && leaseExpiresUtc > DateTime.UtcNow;
		}
	}
}
