using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Beta code administration: programs, codes, minting and revocation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Administrator only. Minting grants access to the game, so it sits at
	/// <see cref="PanelPolicies.OperatorStepUp"/> alongside revocation, and both are audited. The
	/// minted codes are returned once, in the response, and deliberately NOT written to the audit row:
	/// the audit log is read by more people than this page, and a live code in it is a live code
	/// anyone reading the log can redeem. The row records the program, count, uses and expiry.
	/// </para>
	/// <para>
	/// Nothing deletes a code. Revoking one ends redemption and the access of every account that
	/// redeemed it; the code and its links stay as the record.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/admin/beta")]
	public sealed class AdminBetaController : ControllerBase
	{
		private const int MaxNoteLength = 256;
		private const int MaxMintCount = 500;
		private const int MaxUsesPerCode = 100_000;

		private readonly IBetaCodeService betaCodes;
		private readonly BetaOptions beta;
		private readonly AuditScope audit;
		private readonly ILogger<AdminBetaController> log;

		public AdminBetaController(IBetaCodeService betaCodes, BetaOptions beta, AuditScope audit, ILogger<AdminBetaController> log)
		{
			this.betaCodes = betaCodes;
			this.beta = beta;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>Every program with codes, and how the registration gate is configured.</summary>
		[HttpGet("programs")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Programs()
		{
			var result = await betaCodes.ListProgramsAsync(HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The beta programs could not be read." });
			}

			return Ok(new
			{
				gate = new { enabled = beta.Enabled, programs = beta.Programs },
				programs = result.Data.Select(p => new
				{
					program = p.Program,
					codeCount = p.CodeCount,
					activeCodeCount = p.ActiveCodeCount,
					redemptionCount = p.RedemptionCount,
				}),
			});
		}

		/// <summary>One page of codes, newest first.</summary>
		[HttpGet("codes")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Codes(
			[FromQuery] string program,
			[FromQuery] bool includeRevoked = true,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 50)
		{
			string normalized = string.IsNullOrWhiteSpace(program) ? null : BetaCodeFormat.NormalizeProgram(program);
			if (normalized != null && !BetaCodeFormat.IsValidProgram(normalized))
			{
				return BadRequest(new { error = BetaCodeFormat.InvalidProgramError });
			}

			var result = await betaCodes.SearchAsync(new BetaCodeQuery
			{
				Program = normalized,
				IncludeRevoked = includeRevoked,
				Page = Math.Max(1, page),
				PageSize = pageSize,
			}, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The beta codes could not be read." });
			}

			return Ok(new
			{
				items = result.Data.Items.Select(Project),
				page = result.Data.Page,
				pageSize = result.Data.PageSize,
				totalCount = result.Data.TotalCount,
			});
		}

		/// <summary>Mints a batch of codes. The codes are in this response and nowhere else a person reads.</summary>
		[HttpPost("mint")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.BetaMint, TargetType = "beta-program")]
		public async Task<IActionResult> Mint([FromBody] MintRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			string program = BetaCodeFormat.NormalizeProgram(request.Program);
			if (!BetaCodeFormat.IsValidProgram(program))
			{
				return BadRequest(new { error = BetaCodeFormat.InvalidProgramError });
			}
			if (request.Count < 1 || request.Count > MaxMintCount)
			{
				return BadRequest(new { error = $"Mint between 1 and {MaxMintCount} codes at a time." });
			}
			if (request.MaxUses < 1 || request.MaxUses > MaxUsesPerCode)
			{
				return BadRequest(new { error = $"Each code admits between 1 and {MaxUsesPerCode} accounts." });
			}

			DateTime? expiresUtc = request.ExpiresUtc.HasValue ? AsUtc(request.ExpiresUtc.Value) : null;
			if (expiresUtc.HasValue && expiresUtc.Value <= DateTime.UtcNow)
			{
				return BadRequest(new { error = "The expiry must be in the future." });
			}

			string note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
			if (note != null && note.Length > MaxNoteLength)
			{
				return BadRequest(new { error = $"The note must be {MaxNoteLength} characters or fewer." });
			}

			// What was minted, never the codes themselves.
			audit.TargetID = program;
			audit.TargetName = program;
			audit.Details = new { program, count = request.Count, maxUses = request.MaxUses, expiresUtc };

			var result = await betaCodes.MintAsync(program, request.Count, request.MaxUses, expiresUtc,
				User.Identity?.Name, note, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "Those codes could not be minted." });
			}

			log.LogWarning("'{Actor}' minted {Count} beta code(s) for program '{Program}' ({MaxUses} use(s) each). Reason: {Reason}",
				User.Identity?.Name, result.Data.Count, program, request.MaxUses, request.Reason);

			return Ok(new
			{
				message = $"Minted {result.Data.Count} code(s) for {program}. They are shown once here; copy or download them now.",
				program,
				codes = result.Data.Select(Project),
			});
		}

		/// <summary>Revokes a code, ending its redemption and the access it granted.</summary>
		[HttpPost("{id:long}/revoke")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.BetaRevoke, TargetType = "beta-code", TargetRouteValue = "id")]
		public async Task<IActionResult> Revoke(long id, [FromBody] ReasonRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			var result = await betaCodes.RevokeAsync(id, User.Identity?.Name, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				if (result.ErrorCode == DatabaseErrorCodes.NotFound)
				{
					return NotFound(new { error = "There is no such beta code." });
				}
				return BadRequest(new { error = result.ErrorMessage ?? "That code could not be revoked." });
			}

			log.LogWarning("'{Actor}' revoked beta code {Id}. Reason: {Reason}", User.Identity?.Name, id, request.Reason);
			return Ok(new { message = "Code revoked. It can no longer be redeemed, and every account that redeemed it has lost the access it granted." });
		}

		private static object Project(BetaCodeData c) => new
		{
			id = c.ID,
			code = c.Code,
			program = c.Program,
			maxUses = c.MaxUses,
			useCount = c.UseCount,
			remainingUses = c.RemainingUses,
			expiresUtc = c.ExpiresUtc,
			isExpired = c.IsExpired,
			revokedUtc = c.RevokedUtc,
			revokedBy = c.RevokedBy,
			isRevoked = c.IsRevoked,
			createdBy = c.CreatedBy,
			createdUtc = c.CreatedUtc,
			note = c.Note,
		};

		private static DateTime AsUtc(DateTime value) => value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

		/// <summary>A mint request. The reason lands in the audit log; the codes do not.</summary>
		public sealed class MintRequest
		{
			/// <summary>Program key, such as <c>closed_beta</c>.</summary>
			public string Program { get; set; } = "";

			/// <summary>How many codes.</summary>
			public int Count { get; set; }

			/// <summary>How many accounts each code admits.</summary>
			public int MaxUses { get; set; } = 1;

			/// <summary>When the codes stop being redeemable, or null.</summary>
			public DateTime? ExpiresUtc { get; set; }

			/// <summary>Optional staff note, such as who the batch is for.</summary>
			public string Note { get; set; }

			/// <summary>Why. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>A request carrying only the audit reason.</summary>
		public sealed class ReasonRequest
		{
			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
