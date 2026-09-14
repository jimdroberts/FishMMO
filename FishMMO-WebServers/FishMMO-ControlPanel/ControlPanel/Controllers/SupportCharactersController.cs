using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Character views for support staff and operators, and the staff character lock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The reads are here. Edits to a character's stored state live in
	/// <see cref="AdminCharactersController"/> behind a higher policy, so that widening who can look
	/// does not widen who can change.
	/// </para>
	/// <para>
	/// <b>The lock is the exception, and it is a moderation action, not an edit.</b> It changes
	/// nothing about the character — it keeps the player out of the world while staff work on it —
	/// so it sits where kicking and banning sit: game master and above, with a fresh authenticator
	/// code. Every lock and release is recorded by <see cref="AuditActionFilter"/>.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/support/characters")]
	public sealed class SupportCharactersController : ControllerBase
	{
		private readonly ICharacterService characters;
		private readonly IAccountService accounts;
		private readonly IKickRequestService kickRequests;
		private readonly AuditScope audit;
		private readonly ILogger<SupportCharactersController> log;

		/// <summary>The longest a single lock may run.</summary>
		/// <remarks>
		/// A lock must end, so a forgotten one cannot strand a player. Thirty days matches the longest
		/// temporary ban a game master can place; anything longer is a ban, and a ban says so.
		/// </remarks>
		private const int MaxLockMinutes = 30 * 24 * 60;

		public SupportCharactersController(
			ICharacterService characters,
			IAccountService accounts,
			IKickRequestService kickRequests,
			AuditScope audit,
			ILogger<SupportCharactersController> log)
		{
			this.characters = characters;
			this.accounts = accounts;
			this.kickRequests = kickRequests;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>Searches characters by character-name or account-name prefix.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> Search(
			[FromQuery] string query,
			[FromQuery] string online,
			[FromQuery] bool includeDeleted = true,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 15)
		{
			bool? onlineFilter = online switch
			{
				"online" => true,
				"offline" => false,
				_ => null,
			};

			var result = await characters.SearchAdminAsync(query, includeDeleted, onlineFilter, page, pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return BadRequest(new { error = result.ErrorMessage ?? "That search could not be run." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(Summarise),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>One character, deleted rows included.</summary>
		[HttpGet("{id:long}")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> Get(long id)
		{
			var result = await characters.FetchAdminAsync(id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return NotFound(new { error = "No such character." });
			}
			return Ok(Detail(result.Data));
		}

		/// <summary>
		/// Locks a character out of the world until the lock lapses.
		/// </summary>
		/// <remarks>
		/// Character select refuses a locked character. One already in the world is disconnected: kicks
		/// are account-wide, so the kick request written here disconnects the whole account, and the
		/// lock is what then keeps this character from coming straight back.
		/// </remarks>
		[HttpPost("{id:long}/lock")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.CharacterLock, TargetType = "character", TargetRouteValue = "id")]
		public async Task<IActionResult> Lock(long id, [FromBody] LockRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (request.Minutes is null or < 1 or > MaxLockMinutes)
			{
				audit.Outcome = "Refused: lock length out of range.";
				return BadRequest(new { error = "A lock lasts from one minute to 30 days, and must have an end." });
			}

			var (character, failure) = await ResolveAsync(id);
			if (failure != null)
			{
				return failure;
			}
			if (character.Deleted)
			{
				audit.Outcome = "Refused: deleted.";
				return BadRequest(new { error = "That character is deleted; it cannot enter the world to be kept out of it." });
			}

			DateTime until = DateTime.UtcNow.AddMinutes(request.Minutes.Value);
			var result = await characters.LockAsync(id, until, User.Identity?.Name, request.Reason, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That character could not be locked." });
			}

			/* Kick on the session STATE, not only a live lease: a character between selection and load, or
			 * mid scene handover, has no running lease yet but is about to be in the world. A kick for an
			 * account that has already gone is harmless; a missed one is the lock not taking effect. */
			bool inWorld = character.SessionState != 0;
			bool kicked = false;
			if (inWorld)
			{
				kicked = (await kickRequests.PersistAsync(character.Account, HttpContext.RequestAborted)).IsSuccess;
			}
			audit.Details = new { lockedUntilUtc = until, minutes = request.Minutes, inWorld, kickRequested = kicked };

			log.LogWarning("Character {Id} ('{Name}') locked until {Until:u} by '{Actor}'. Reason: {Reason}",
				id, character.Name, until, User.Identity?.Name, request.Reason);

			return Ok(new
			{
				message = !inWorld
					? "Character locked."
					: kicked
						? "Character locked. It was in the world, so its account has been sent a kick."
						: "Character locked, but the kick request could not be written. Kick the account by hand.",
				lockedUntilUtc = until,
			});
		}

		/// <summary>Releases a character lock before it lapses.</summary>
		[HttpPost("{id:long}/unlock")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.CharacterUnlock, TargetType = "character", TargetRouteValue = "id")]
		public async Task<IActionResult> Unlock(long id, [FromBody] LockRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			var (character, failure) = await ResolveAsync(id);
			if (failure != null)
			{
				return failure;
			}

			var result = await characters.UnlockAsync(id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That lock could not be released." });
			}
			if (!result.Data)
			{
				audit.Outcome = "Refused: not locked.";
				return BadRequest(new { error = "That character is not locked." });
			}

			log.LogWarning("Character {Id} ('{Name}') unlocked by '{Actor}'. Reason: {Reason}",
				id, character.Name, User.Identity?.Name, request.Reason);
			return Ok(new { message = "Lock released. The character can enter the world again." });
		}

		/// <summary>
		/// Loads the character and applies the moderation guards against the account that owns it.
		/// </summary>
		/// <remarks>
		/// The same rule as every account action: not your own, and not a peer's or a superior's.
		/// Read from the owning account's level, fresh, because a character's own access level column
		/// is not what decides who a player is.
		/// </remarks>
		private async Task<(CharacterAdminData Character, IActionResult Failure)> ResolveAsync(long id)
		{
			var result = await characters.FetchAdminAsync(id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = "Refused: no such character.";
				return (null, NotFound(new { error = "No such character." }));
			}
			audit.TargetName = result.Data.Name;

			var owner = await accounts.FetchAdminAsync(result.Data.Account, HttpContext.RequestAborted);
			if (!owner.IsSuccess)
			{
				audit.Outcome = "Refused: the owning account could not be read.";
				return (null, NotFound(new { error = "The account that owns this character could not be read." }));
			}

			IActionResult refusal = ModerationGuards.Check(this, owner.Data.Name, (AccessLevel)owner.Data.AccessLevel);
			if (refusal != null)
			{
				audit.Outcome = "Refused: the owner is at or above the operator's level, or is the operator.";
				return (null, refusal);
			}
			return (result.Data, null);
		}

		/// <summary>A lock or a release.</summary>
		public sealed class LockRequest
		{
			/// <summary>How long the lock lasts, in minutes. Required to lock; ignored on release.</summary>
			public int? Minutes { get; set; }

			/// <summary>Why. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>The list projection: what a table row shows, and nothing more.</summary>
		internal static object Summarise(CharacterAdminData c) => new
		{
			id = c.ID,
			name = c.Name,
			account = c.Account,
			raceId = c.RaceID,
			accessLevel = c.AccessLevel,
			selected = c.Selected,
			deleted = c.Deleted,
			storedName = c.StoredName,
			// "Online" to an operator means held, not flagged: see CharacterEditLock.
			online = !CharacterEditLock.For(c).Editable && !c.Deleted,
			lastSavedUtc = c.LastSaved,
			locked = c.LockedUntil > DateTime.UtcNow,
			lockedUntilUtc = c.LockedUntil,
		};

		/// <summary>The detail projection, carrying the edit lock the editor gates on.</summary>
		internal static object Detail(CharacterAdminData c)
		{
			var lockState = CharacterEditLock.For(c);
			return new
			{
				id = c.ID,
				name = c.Name,
				account = c.Account,
				raceId = c.RaceID,
				accessLevel = c.AccessLevel,
				selected = c.Selected,
				deleted = c.Deleted,
				storedName = c.StoredName,
				timeDeletedUtc = c.TimeDeleted,
				online = lockState.Reason == "lease",
				sessionState = c.SessionState,
				sessionOwnerServerId = c.SessionOwnerServerID,
				sessionLeaseExpiresUtc = c.SessionLeaseExpiresUtc,
				worldServerId = c.WorldServerID,
				sceneName = c.SceneName,
				bindScene = c.BindScene,
				x = c.X,
				y = c.Y,
				z = c.Z,
				version = c.Version,
				createdUtc = c.TimeCreated,
				lastSavedUtc = c.LastSaved,
				staffLock = new
				{
					locked = c.LockedUntil > DateTime.UtcNow,
					lockedUntilUtc = c.LockedUntil,
					lockedAtUtc = c.LockedAt,
					lockedBy = c.LockedBy,
					reason = c.LockReason,
				},
				editLock = new
				{
					editable = lockState.Editable,
					reason = lockState.Reason,
					message = lockState.Message,
					ownerServerId = lockState.OwnerServerId,
					leaseExpiresUtc = lockState.LeaseExpiresUtc,
				},
			};
		}
	}
}
