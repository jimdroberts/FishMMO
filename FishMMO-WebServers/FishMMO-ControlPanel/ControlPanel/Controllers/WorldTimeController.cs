using System.Globalization;
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
	/// The world clock: read it, set it, shift it, change its pace, hold and resume it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Every control here writes ONE database row</b> (<c>world_clock_control</c>), and every
	/// scene server adopts it on its next pulse, a couple of seconds later, and jumps its players'
	/// clocks to it. Nothing here reaches into a process — so every acknowledgement says what was
	/// <em>written</em>, never that the world has changed. If no scene server is running, the row is
	/// read by nobody.
	/// </para>
	/// <para>
	/// <b>All clock arithmetic happens in the database.</b> The world time a write replaces, the
	/// anchor it writes and the instant it stamps come from one reading of the database clock inside
	/// the statement (see <c>WorldClockControlService</c>), so this host's clock skew cannot move the
	/// world. This controller only parses and formats: a world timestamp is the row's epoch plus world
	/// milliseconds, written <see cref="WorldClockText.Format"/>; a change is <c>+1h30m</c>,
	/// <c>-90s</c>, <c>+2d3h4m5.25s</c>. Both forms are parsed by <see cref="WorldClockText"/>, the
	/// same code available to the in-game <c>/admin time</c> commands, so the two surfaces agree
	/// about what a typed value means.
	/// </para>
	/// <para>
	/// <b>The row is created by the first scene server to start</b>, which knows the calendar's epoch.
	/// Until then there is nothing to write and the page says so; every write answers 404 with the
	/// service's sentence.
	/// </para>
	/// <para>
	/// Writes sit at Operator plus step-up, as server control does: the in-game commands are Admin,
	/// and two faces of one capability that disagree about who may use it make the weaker one the
	/// way around the stronger.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/world-time")]
	public sealed class WorldTimeController : ControllerBase
	{
		/// <summary>What every successful write's acknowledgement ends with.</summary>
		private const string AdoptedSentence = "Every scene server adopts it within a few seconds.";

		/// <summary>Longest value accepted from the set field, before parsing.</summary>
		private const int MaxValueLength = 64;

		private readonly IWorldClockControlService clock;
		private readonly AuditScope audit;
		private readonly ILogger<WorldTimeController> log;

		public WorldTimeController(IWorldClockControlService clock, AuditScope audit, ILogger<WorldTimeController> log)
		{
			this.clock = clock;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>The clock as the database reads it now.</summary>
		/// <remarks>
		/// Carries world-now as the database computed it plus the pace, so the browser can tick the
		/// display forward from the moment the reply arrived without consulting its own wall clock.
		/// </remarks>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Read()
		{
			var result = await clock.ReadAsync(HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return DatabaseReplies.Failure(this, result, log, "The world clock could not be read.");
			}
			return Ok(Project(result.Data));
		}

		/// <summary>
		/// Sets world time to a world timestamp, keeping the pace. A value starting with + or - is a
		/// change instead, and is recorded as a shift.
		/// </summary>
		[HttpPost("set")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.WorldTimeSet, TargetType = "world-clock")]
		public async Task<IActionResult> Set([FromBody] SetRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			audit.TargetName = "world clock";
			audit.TargetID = "1";
			string value = (request.Value ?? "").Trim();
			audit.Details = new { requested = Truncate(value) };

			if (value.Length == 0)
			{
				audit.Outcome = "Refused: no value.";
				return BadRequest(new { error = $"Give a world timestamp ({WorldClockText.Format}) or a change such as +1h30m." });
			}
			if (value.Length > MaxValueLength)
			{
				audit.Outcome = "Refused: value too long.";
				return BadRequest(new { error = "That value is too long to be a world timestamp or a change." });
			}

			/* A sign makes it a change: the same field takes both, as the lead asked, and the record
			 * names what actually happened. */
			if (value[0] == '+' || value[0] == '-')
			{
				audit.Action = AuditActions.WorldTimeShift;
				return await ShiftCoreAsync(value);
			}

			/* A timestamp is relative to the row's epoch, which only the row knows. The epoch never
			 * changes once seeded, so reading it first cannot race the write below. */
			var read = await clock.ReadAsync(HttpContext.RequestAborted);
			if (!read.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(read);
				return DatabaseReplies.Failure(this, read, log, "The world clock could not be read.");
			}
			if (!read.Data.Exists)
			{
				audit.Outcome = "Refused: no world clock row yet.";
				return NotFound(new { error = NoRowSentence });
			}
			if (!WorldClockText.TryParseTimestamp(value, read.Data.EpochUnixSeconds, out long worldMs, out string? parseError))
			{
				audit.Outcome = "Refused: " + parseError;
				return BadRequest(new { error = parseError });
			}

			var result = await clock.SetAsync(worldMs, Actor, HttpContext.RequestAborted);
			return Written(result, value, "That world time could not be written.",
				after => $"World time written: {Timestamp(after)}, {Pace(after.Rate)}. {AdoptedSentence}");
		}

		/// <summary>Moves world time by a signed change, keeping the pace.</summary>
		[HttpPost("shift")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.WorldTimeShift, TargetType = "world-clock")]
		public async Task<IActionResult> Shift([FromBody] ShiftRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			audit.TargetName = "world clock";
			audit.TargetID = "1";
			string change = (request.Change ?? "").Trim();
			audit.Details = new { requested = Truncate(change) };
			if (change.Length > MaxValueLength)
			{
				audit.Outcome = "Refused: value too long.";
				return BadRequest(new { error = "That change is too long." });
			}
			return await ShiftCoreAsync(change);
		}

		private async Task<IActionResult> ShiftCoreAsync(string change)
		{
			if (!WorldClockText.TryParseChange(change, out long deltaMs, out string? parseError))
			{
				audit.Outcome = "Refused: " + parseError;
				return BadRequest(new { error = parseError });
			}

			var result = await clock.ShiftAsync(deltaMs, Actor, HttpContext.RequestAborted);
			return Written(result, change, "That change could not be written.",
				after => $"World time moved {WorldClockText.FormatChange(deltaMs)} and written: {Timestamp(after)}, {Pace(after.Rate)}. {AdoptedSentence}");
		}

		/// <summary>Changes the pace from this instant, without moving world time.</summary>
		[HttpPost("pace")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.WorldTimePace, TargetType = "world-clock")]
		public async Task<IActionResult> SetPace([FromBody] PaceRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			audit.TargetName = "world clock";
			audit.TargetID = "1";
			audit.Details = new { requestedRate = request.Rate };
			if (request.Rate == null || double.IsNaN(request.Rate.Value) || double.IsInfinity(request.Rate.Value))
			{
				audit.Outcome = "Refused: no pace.";
				return BadRequest(new { error = "Give the pace as world seconds per real second: 0 holds, 1 is real time." });
			}

			// The service refuses a negative or excessive pace with a sentence; it is passed through.
			var result = await clock.SetRateAsync(request.Rate.Value, Actor, HttpContext.RequestAborted);
			return Written(result, null, "That pace could not be written.",
				after => after.Rate <= 0
					? $"Pace written: held at {Timestamp(after)}. {AdoptedSentence}"
					: $"Pace written: {Pace(after.Rate)} from {Timestamp(after)}. {AdoptedSentence}");
		}

		/// <summary>Holds world time where it stands.</summary>
		[HttpPost("hold")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.WorldTimeHold, TargetType = "world-clock")]
		public async Task<IActionResult> Hold([FromBody] ReasonRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			audit.TargetName = "world clock";
			audit.TargetID = "1";

			var result = await clock.HoldAsync(Actor, HttpContext.RequestAborted);
			return Written(result, null, "The hold could not be written.",
				after => $"Hold written at {Timestamp(after)}; resuming returns to {Pace(after.ResumeRate)}. {AdoptedSentence}");
		}

		/// <summary>Resumes at the resume pace from where world time stands.</summary>
		[HttpPost("resume")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.WorldTimeResume, TargetType = "world-clock")]
		public async Task<IActionResult> Resume([FromBody] ReasonRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			audit.TargetName = "world clock";
			audit.TargetID = "1";

			var result = await clock.ResumeAsync(Actor, HttpContext.RequestAborted);
			return Written(result, null, "The resume could not be written.",
				after => $"Resume written: {Pace(after.Rate)} from {Timestamp(after)}. {AdoptedSentence}");
		}

		/* ── Shared ────────────────────────────────────────────────── */

		/// <summary>
		/// The reply for a write, and its audit details: what was asked for, and the world time and
		/// pace before and after, both read by the statement that wrote them.
		/// </summary>
		private IActionResult Written(DatabaseResult<WorldClockReading> result, string? requested, string fault, Func<WorldClockReading, string> message)
		{
			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, fault);
			}

			WorldClockReading after = result.Data;
			string? beforeTimestamp = after.ReplacedWorldMs.HasValue
				? Timestamp(after.EpochUnixSeconds, after.ReplacedWorldMs.Value)
				: null;

			audit.TargetID = "1";
			audit.Details = new
			{
				requested = requested == null ? null : Truncate(requested),
				/* Null only for a replay: the first attempt committed and lost its reply, and the
				 * retry found its own key on the row. The write happened once. */
				before = after.ReplacedWorldMs.HasValue
					? new { worldTimestamp = beforeTimestamp, worldMs = after.ReplacedWorldMs, rate = after.ReplacedRate, pace = Pace(after.ReplacedRate ?? 0) }
					: null,
				after = new { worldTimestamp = Timestamp(after), worldMs = after.WorldMsNow, rate = after.Rate, pace = Pace(after.Rate), resumeRate = after.ResumeRate },
				revision = after.Revision,
			};

			log.LogWarning("World clock written by '{Actor}' ({Action}): {Before} at {BeforePace} -> {After} at {AfterPace}, revision {Revision}. Reason: {Reason}",
				Actor, audit.Action, beforeTimestamp ?? "(replay)", after.ReplacedRate.HasValue ? Pace(after.ReplacedRate.Value) : "?",
				Timestamp(after), Pace(after.Rate), after.Revision, audit.Reason);

			return Ok(new
			{
				clock = Project(after),
				replacedWorldTimestamp = beforeTimestamp,
				replacedPace = after.ReplacedRate.HasValue ? Pace(after.ReplacedRate.Value) : null,
				message = message(after),
			});
		}

		private const string NoRowSentence =
			"The world clock has no row yet: no scene server has started against this database. " +
			"The first scene server to start creates it from its calendar; there is nothing to write until then.";

		/// <summary>The account acting. The actor is the account, never a character.</summary>
		private string Actor => User.Identity?.Name ?? "";

		private static string Truncate(string value) => value.Length > MaxValueLength ? value.Substring(0, MaxValueLength) : value;

		private static string Timestamp(WorldClockReading r) => Timestamp(r.EpochUnixSeconds, r.WorldMsNow);

		private static string Timestamp(long epochUnixSeconds, long worldMs) =>
			WorldClockText.FormatTimestamp(epochUnixSeconds, worldMs)
			?? $"world ms {worldMs.ToString(CultureInfo.InvariantCulture)}";

		/// <summary>"held", "real time", or "60×".</summary>
		private static string Pace(double rate)
		{
			if (rate <= 0) return "held";
			if (rate == 1) return "real time";
			return rate.ToString("0.####", CultureInfo.InvariantCulture) + "×";
		}

		/// <summary>The clock as the page's JSON carries it.</summary>
		private static object Project(WorldClockReading r) => new
		{
			exists = r.Exists,
			dbNowMs = r.DbNowMs,
			worldMsNow = r.WorldMsNow,
			worldTimestamp = r.Exists ? Timestamp(r) : null,
			baseWorldMs = r.BaseWorldMs,
			baseReferenceMs = r.BaseReferenceMs,
			rate = r.Rate,
			resumeRate = r.ResumeRate,
			held = r.IsHeld,
			pace = r.Exists ? Pace(r.Rate) : null,
			resumePace = r.Exists ? Pace(r.ResumeRate) : null,
			revision = r.Revision,
			updatedBy = r.UpdatedBy,
			updatedAtUtc = r.UpdatedAt,
			epochUnixSeconds = r.EpochUnixSeconds,
			epochTimestamp = r.Exists ? WorldClockText.FormatTimestamp(r.EpochUnixSeconds, 0) : null,
			maxRate = WorldClockLimits.MaxRate,
			format = WorldClockText.Format,
		};

		/// <summary>A world timestamp, or a signed change.</summary>
		public sealed class SetRequest
		{
			/// <summary>A world timestamp (<c>yyyy-MM-dd HH:mm:ss.fff</c>) or a change (<c>+1h30m</c>).</summary>
			public string Value { get; set; } = "";

			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>A signed change.</summary>
		public sealed class ShiftRequest
		{
			/// <summary>The change, such as <c>+1h</c> or <c>-90s</c>.</summary>
			public string Change { get; set; } = "";

			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>A new pace.</summary>
		public sealed class PaceRequest
		{
			/// <summary>World seconds per real second. 0 holds; 1 is real time.</summary>
			public double? Rate { get; set; }

			/// <summary>Why the action was taken. Recorded.</summary>
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
