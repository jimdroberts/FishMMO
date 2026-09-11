using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Read-only character views for support staff and operators.
	/// </summary>
	/// <remarks>
	/// Everything here is a read. The writes live in <see cref="AdminCharactersController"/>
	/// behind a higher policy, so that widening who can look does not widen who can change.
	/// </remarks>
	[ApiController]
	[Route("api/support/characters")]
	public sealed class SupportCharactersController : ControllerBase
	{
		private readonly ICharacterService characters;

		public SupportCharactersController(ICharacterService characters)
		{
			this.characters = characters;
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

		/// <summary>The list projection: what a table row shows, and nothing more.</summary>
		internal static object Summarise(CharacterAdminData c) => new
		{
			id = c.ID,
			name = c.Name,
			account = c.Account,
			level = c.Level,
			raceId = c.RaceID,
			accessLevel = c.AccessLevel,
			selected = c.Selected,
			deleted = c.Deleted,
			storedName = c.StoredName,
			// "Online" to an operator means held, not flagged: see CharacterEditLock.
			online = !CharacterEditLock.For(c).Editable && !c.Deleted,
			lastSavedUtc = c.LastSaved,
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
				level = c.Level,
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
