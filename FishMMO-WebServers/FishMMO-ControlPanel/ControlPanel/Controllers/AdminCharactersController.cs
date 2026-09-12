using System.Security.Claims;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Operator actions on an existing character.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Restore and rename, and nothing that decides whether a character exists. Creation and
	/// deletion are in-game only: deletion spans fourteen sub-entity tables inside one unit of
	/// work in the login server's character-select system, and a second implementation of that
	/// is how a shard loses data.
	/// </para>
	/// <para>
	/// Restore is on the right side of that line because it un-deletes a row that is still
	/// there, and rename changes one field. Neither reaches the teardown.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/admin/characters")]
	public sealed class AdminCharactersController : ControllerBase
	{
		private readonly ICharacterService characters;
		private readonly AuditScope audit;
		private readonly ILogger<AdminCharactersController> log;

		public AdminCharactersController(ICharacterService characters, AuditScope audit, ILogger<AdminCharactersController> log)
		{
			this.characters = characters;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>
		/// The character's name and id for the audit record, read before the action.
		/// </summary>
		/// <remarks>
		/// Before, not after: a rename changes the name, and a record that names the target by
		/// what it became cannot answer "what happened to the character called X".
		/// </remarks>
		private async Task<string> TargetNameAsync(long id)
		{
			var result = await characters.FetchAdminAsync(id, HttpContext.RequestAborted);
			return result.IsSuccess ? result.Data.Name : null;
		}

		/// <summary>
		/// Applies an edit to a character's stored row.
		/// </summary>
		/// <remarks>
		/// Position, scene and the character's own access level. The service re-checks
		/// the session lease inside the write, so a character claimed between the operator's
		/// read and this call is refused rather than written over.
		/// </remarks>
		[HttpPatch("{id:long}")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.CharacterEdit, TargetType = "character", TargetRouteValue = "id")]
		public async Task<IActionResult> Patch(long id, [FromBody] EditRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			audit.TargetName = await TargetNameAsync(id);

			/* An operator must not be able to hand a character an access level the panel itself
			 * refuses to grant an account. AccessLevel is the whole authorization surface of
			 * the game's command system, so it is bounded here as well as in the database. */
			if (request.AccessLevel.HasValue && request.AccessLevel.Value > (byte)FishMMO.Auth.Core.AccessLevel.GameMaster)
			{
				// The filter records the refusal from the status code; this only sharpens what it
				// says, because "bad request" does not tell a reader what was attempted.
				audit.Outcome = "Refused: access level above Game Master.";
				audit.Details = new { attempted = request.AccessLevel };
				return BadRequest(new { error = "A character cannot be given an access level above Game Master from the panel." });
			}

			var edit = new CharacterAdminEdit
			{
				X = request.X,
				Y = request.Y,
				Z = request.Z,
				SceneName = string.IsNullOrWhiteSpace(request.SceneName) ? null : request.SceneName.Trim(),
				BindScene = string.IsNullOrWhiteSpace(request.BindScene) ? null : request.BindScene.Trim(),
				AccessLevel = request.AccessLevel,
			};

			var result = await characters.UpdateAdminAsync(id, edit, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				log.LogWarning("Edit of character {Id} by '{Actor}' failed: [{Code}] {Message}",
					id, User.Identity?.Name, result.ErrorCode, result.ErrorMessage);

				audit.Outcome = result.ErrorMessage;
				audit.Details = new { result.ErrorCode };

				if (string.Equals(result.ErrorCode, FishMMO.Database.DatabaseErrorCodes.StaleState, StringComparison.OrdinalIgnoreCase))
				{
					return Conflict(new { error = result.ErrorMessage });
				}
				return BadRequest(new { error = result.ErrorMessage ?? "That character could not be edited." });
			}

			audit.Details = new { edit.X, edit.Y, edit.Z, edit.SceneName, edit.BindScene, edit.AccessLevel };

			log.LogInformation("Character {Id} edited by '{Actor}'. Reason: {Reason}",
				id, User.Identity?.Name, request.Reason);
			return Ok(new { message = "Character saved." });
		}

		/// <summary>
		/// The character's current edit lock, for polling after a kick.
		/// </summary>
		[HttpGet("{id:long}/edit-lock")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> EditLock(long id)
		{
			var result = await characters.FetchAdminAsync(id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return NotFound(new { error = "No such character." });
			}

			var state = CharacterEditLock.For(result.Data);
			return Ok(new
			{
				editable = state.Editable,
				reason = state.Reason,
				message = state.Message,
				ownerServerId = state.OwnerServerId,
				leaseExpiresUtc = state.LeaseExpiresUtc,
			});
		}

		/// <summary>
		/// Restores a soft-deleted character.
		/// </summary>
		/// <remarks>
		/// Deletion renames the row, appending a marker and a GUID so the unique index releases
		/// the name; restoring puts the original name back. That fails when somebody has taken
		/// the name since, and the operator needs to be told which name rather than shown a
		/// generic error.
		/// </remarks>
		[HttpPost("{id:long}/restore")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.CharacterRestore, TargetType = "character", TargetRouteValue = "id")]
		public async Task<IActionResult> Restore(long id, [FromBody] ReasonRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			audit.TargetName = await TargetNameAsync(id);

			var result = await characters.RestoreAsync(id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				log.LogWarning("Restore of character {Id} by '{Actor}' failed: [{Code}] {Message}",
					id, User.Identity?.Name, result.ErrorCode, result.ErrorMessage);

				audit.Outcome = result.ErrorMessage;
				audit.Details = new { result.ErrorCode };

				if (IsUniqueViolation(result.ErrorCode))
				{
					return Conflict(new { error = "That character's name has been taken since it was deleted." });
				}
				return BadRequest(new { error = result.ErrorMessage ?? "That character could not be restored." });
			}

			log.LogInformation("Character {Id} restored by '{Actor}'. Reason: {Reason}",
				id, User.Identity?.Name, request.Reason);
			return Ok(new { message = "Character restored." });
		}

		/// <summary>Renames a character.</summary>
		[HttpPost("{id:long}/rename")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.CharacterRename, TargetType = "character", TargetRouteValue = "id")]
		public async Task<IActionResult> Rename(long id, [FromBody] RenameRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.NewName))
			{
				return BadRequest(new { error = "A new name is required." });
			}
			if (string.IsNullOrWhiteSpace(request.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			/* A leased character's name would be written straight back. The persistence pass
			 * UPDATEs name from the owning server's in-memory copy, so renaming a character
			 * that is in the world does not race — it reliably loses on the next save. Refuse
			 * it here rather than report a success that evaporates. The client disables the
			 * control for the same reason, but the client is not the guard. */
			var current = await characters.FetchAdminAsync(id, HttpContext.RequestAborted);
			if (!current.IsSuccess)
			{
				return NotFound(new { error = "No such character." });
			}

			/* Recorded under the OLD name. The question asked later is "what happened to the
			 * character called X", and X is what it was called before, not after. */
			string oldName = current.Data.Name;
			audit.TargetName = oldName;

			var state = CharacterEditLock.For(current.Data);
			if (state.Reason == "lease")
			{
				audit.Outcome = "Refused: the character holds a live session lease.";
				audit.Details = new { newName = request.NewName };
				return Conflict(new { error = state.Message });
			}

			var result = await characters.RenameAsync(id, request.NewName.Trim(), HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				log.LogWarning("Rename of character {Id} by '{Actor}' failed: [{Code}] {Message}",
					id, User.Identity?.Name, result.ErrorCode, result.ErrorMessage);

				audit.Outcome = result.ErrorMessage;
				audit.Details = new { newName = request.NewName, result.ErrorCode };

				if (IsUniqueViolation(result.ErrorCode))
				{
					return Conflict(new { error = "That name is already taken." });
				}
				return BadRequest(new { error = result.ErrorMessage ?? "That character could not be renamed." });
			}

			audit.Details = new { from = oldName, to = request.NewName.Trim() };

			log.LogInformation("Character {Id} renamed by '{Actor}'. Reason: {Reason}",
				id, User.Identity?.Name, request.Reason);
			return Ok(new { message = "Character renamed." });
		}

		/// <summary>
		/// A unique-index collision, which both routes have to report specifically because
		/// "that name is taken" is the one failure the operator can act on.
		/// </summary>
		private static bool IsUniqueViolation(string errorCode) =>
			string.Equals(errorCode, FishMMO.Database.DatabaseErrorCodes.UniqueViolation, StringComparison.OrdinalIgnoreCase);

		/// <summary>A request carrying only the audit reason.</summary>
		public sealed class ReasonRequest
		{
			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>An edit request. Every field but the reason is optional.</summary>
		public sealed class EditRequest
		{
			/// <summary>New X.</summary>
			public float? X { get; set; }

			/// <summary>New Y.</summary>
			public float? Y { get; set; }

			/// <summary>New Z.</summary>
			public float? Z { get; set; }

			/// <summary>New current scene.</summary>
			public string SceneName { get; set; }

			/// <summary>New respawn scene.</summary>
			public string BindScene { get; set; }

			/// <summary>New character access level.</summary>
			public byte? AccessLevel { get; set; }

			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>A rename request.</summary>
		public sealed class RenameRequest
		{
			/// <summary>The new character name.</summary>
			public string NewName { get; set; } = "";

			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
