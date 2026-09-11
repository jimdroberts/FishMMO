using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// The two queues players wait in: outbound email, and the group finder.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The email queue is how account verification reaches a player, and when it stops
	/// nothing says so.</b> A registration writes a row here and answers the browser
	/// successfully; a login server is supposed to claim that row and send it. If none does —
	/// the process is down, SMTP is refusing the credentials, the host cannot reach the relay —
	/// then every new account is created and none of them can ever be verified. No error is
	/// logged, because from the database's point of view nothing failed. The site keeps
	/// accepting sign-ups. Players give up quietly and do not file tickets about mail that never
	/// arrived. This endpoint exists because that outage has no other symptom, and the page it
	/// feeds says so in as many words.
	/// </para>
	/// <para>
	/// <b>The number that detects it is an age, not a depth.</b> A dead queue and a healthy one
	/// on a quiet shard both read as a small count. What separates them is how long the oldest
	/// unclaimed message has been sitting there: a working deployment drains within seconds, so
	/// anything measured in minutes means nothing is claiming. That value leads the response and
	/// leads the page.
	/// </para>
	/// <para>
	/// <b>There is no clear, purge or delete here, and there will not be.</b> A queue that can be
	/// emptied from a browser is exactly how the outage above becomes invisible: the rows that
	/// prove registration is broken are the first thing anybody reaches for when the number looks
	/// alarming, and removing them destroys both the evidence and the mail those accounts are
	/// still owed. The only write is a retry, and a retry only ever puts a message <em>back</em>
	/// into the queue.
	/// </para>
	/// <para>
	/// The group finder half is read-only for a different reason. Its rows are matched by a pump
	/// running on every scene server, inside one transaction; deleting one from here would race
	/// that transaction and could strand a character in a party nothing will ever transfer them
	/// into. The safe paths — leaving the queue, the disconnect path, the stale sweep — already
	/// exist in the game, where the caller can deal with what a removal implies.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/platform/queues")]
	public sealed class PlatformQueuesController : ControllerBase
	{
		/// <summary>
		/// How old the oldest unclaimed message may get before the page calls it a warning.
		/// </summary>
		/// <remarks>
		/// A login server polls the queue on a short interval and SMTP hand-off takes about as
		/// long as a TCP round trip, so a healthy queue is drained in seconds and any age in
		/// minutes already means something is wrong. Five minutes is slack for a restart, not a
		/// service level.
		/// </remarks>
		private const int PendingWarnSeconds = 300;

		/// <summary>
		/// How old the oldest unclaimed message may get before the page calls it an outage.
		/// </summary>
		/// <remarks>
		/// Fifteen minutes cannot be explained by a restart, a slow relay or a retry backoff.
		/// Past this, registration is broken for everybody who signed up since, and the page
		/// stops being informative and starts being an alarm.
		/// </remarks>
		private const int PendingDangerSeconds = 900;

		/// <summary>
		/// How long without a heartbeat before a group finder row is shown as stale.
		/// </summary>
		/// <remarks>
		/// <b>The panel's display threshold, not the matcher's.</b> The real one is configured on
		/// each scene server and passed into the queue service per call, so it is not readable
		/// from here. It is reported to the client rather than applied, so the operator sees the
		/// heartbeat age and judges for themselves — a row shown stale under this threshold is a
		/// row worth looking at, not a row the matcher has definitely excluded.
		/// </remarks>
		private const int PulseStaleAfterSeconds = 60;

		private readonly IQueueBoardService queues;
		private readonly AuditScope audit;
		private readonly ILogger<PlatformQueuesController> log;

		public PlatformQueuesController(
			IQueueBoardService queues,
			AuditScope audit,
			ILogger<PlatformQueuesController> log)
		{
			this.queues = queues;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>One page of the outbound email queue, with the counts and the ages.</summary>
		/// <param name="state">One of <c>pending</c>, <c>claimed</c>, <c>failed</c>, <c>sent</c>. Omit for any.</param>
		/// <param name="search">Substring of the recipient address or account name. Omit for all.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Rows per page. Clamped by the service to 200.</param>
		[HttpGet("email")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> EmailQueue(
			[FromQuery] string state,
			[FromQuery] string search,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 25)
		{
			if (!TryParseState(state, out EmailQueueState? parsed))
			{
				return BadRequest(new { error = "State must be one of pending, claimed, failed or sent." });
			}

			var result = await queues.FetchEmailQueueAsync(parsed, search, page, pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The email queue could not be read." });
			}

			var data = result.Data;
			DateTime now = data.ReadAtUtc;

			return Ok(new
			{
				/* Reported rather than applied, like the server board's staleness. The page shows
				 * the age and tints it; baking "this is an outage" into the response would freeze
				 * a judgement at read time that a deployment with a slow relay may disagree
				 * with. */
				pendingWarnSeconds = PendingWarnSeconds,
				pendingDangerSeconds = PendingDangerSeconds,
				readAtUtc = now,
				counts = new
				{
					pending = data.Counts.Pending,
					claimed = data.Counts.Claimed,
					failed = data.Counts.Failed,
					sent = data.Counts.Sent,
					total = data.Counts.Total,
				},
				/* The headline. Null means nothing is waiting to be claimed, which is what a
				 * working queue looks like — and is why it must be distinguishable from zero
				 * seconds rather than flattened into it. */
				oldestPendingCreatedUtc = data.OldestPendingCreatedAt,
				oldestPendingAgeSeconds = AgeSeconds(data.OldestPendingCreatedAt, now),
				/* The longest any account has been waiting for mail, including messages a login
				 * server claimed and then went silent on. Those are stuck just as hard, and a
				 * pending-only number reads zero while they sit there. */
				oldestUnsentCreatedUtc = data.OldestUnsentCreatedAt,
				oldestUnsentAgeSeconds = AgeSeconds(data.OldestUnsentCreatedAt, now),
				items = data.Items.Select(e => new
				{
					id = e.ID,
					recipientEmail = e.RecipientEmail,
					recipientUsername = e.RecipientUsername,
					subject = e.Subject,
					state = StateName(e.State),
					attempts = e.Attempts,
					claimedBy = e.ClaimedBy,
					claimedAtUtc = e.ClaimedAt,
					queuedUtc = e.CreatedAt,
					sentUtc = e.SentAt,
					lastError = e.LastError,
					ageSeconds = AgeSeconds(e.CreatedAt, now),
					/* Decided here rather than in the browser so the button and the endpoint
					 * cannot disagree about what is retryable. A claimed message with no error is
					 * retryable too: a login server that died after claiming leaves exactly that,
					 * and it is stuck more quietly than a failure because nothing wrote one. */
					canRetry = e.State == EmailQueueState.Claimed || e.State == EmailQueueState.Failed,
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>One page of the group finder queue.</summary>
		/// <param name="status">0 waiting, 1 matched. Omit for any.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Rows per page. Clamped by the service to 200.</param>
		[HttpGet("group-finder")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> GroupFinderQueue(
			[FromQuery] int? status,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 25)
		{
			var result = await queues.FetchGroupFinderQueueAsync(status, page, pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The group finder queue could not be read." });
			}

			var data = result.Data;
			DateTime now = data.ReadAtUtc;

			return Ok(new
			{
				pulseStaleAfterSeconds = PulseStaleAfterSeconds,
				readAtUtc = now,
				counts = new
				{
					waiting = data.Counts.Waiting,
					matched = data.Counts.Matched,
					total = data.Counts.Total,
				},
				items = data.Items.Select(g => new
				{
					id = g.ID,
					characterId = g.CharacterID,
					/* Null when the row outlived its character, which the cascade should make
					 * impossible. Sent as null rather than papered over with the id, so that if
					 * it ever happens somebody sees it instead of reading it as a character
					 * named after a number. */
					characterName = g.CharacterName,
					accountName = g.AccountName,
					worldServerId = g.WorldServerID,
					sceneName = g.SceneName,
					sceneType = g.SceneType,
					sceneTypeName = ((FishMMO.Database.Data.Enums.SceneType)g.SceneType).ToString(),
					difficulty = g.Difficulty,
					status = g.Status,
					statusName = ((FishMMO.Database.Data.Enums.GroupFinderQueueStatus)g.Status).ToString(),
					groupId = g.GroupID,
					partyId = g.PartyID,
					instanceId = g.InstanceID,
					queuedUtc = g.TimeCreated,
					waitSeconds = AgeSeconds(g.TimeCreated, now),
					lastPulseUtc = g.LastPulse,
					pulseAgeSeconds = AgeSeconds(g.LastPulse, now),
					/* A long wait on a stale row is not the matcher failing. It is a scene server
					 * that went away with somebody queued on it, and the two have completely
					 * different fixes — so the page must be able to tell them apart. */
					stale = (now - g.LastPulse).TotalSeconds > PulseStaleAfterSeconds,
					matchedUtc = g.TimeMatched,
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>
		/// Releases the claim on a stuck message so a login server can pick it up again.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This does not send anything.</b> Delivery belongs to the login server; all this
		/// does is make the row eligible to be claimed again, which is why the acknowledgement
		/// says the message was re-queued rather than sent. If nothing is claiming — the case
		/// this page exists to reveal — the retry will appear to do nothing, and the honest thing
		/// is to say so rather than let a green toast imply mail went out.
		/// </para>
		/// <para>
		/// Step-up, not plain Operator, because it touches the account verification path: a
		/// borrowed unlocked session should not be able to re-queue a verification email for an
		/// account somebody is trying to take over. The reason is mandatory for the same reason
		/// it is on every other write in this panel — the audit row is only as useful as what it
		/// says about why.
		/// </para>
		/// </remarks>
		[HttpPost("email/{id:long}/retry")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.EmailRetry, TargetType = "email", TargetRouteValue = "id")]
		public async Task<IActionResult> RetryEmail(long id, [FromBody] ReasonRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			var result = await queues.RetryEmailAsync(id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				if (string.Equals(result.ErrorCode, FishMMO.Database.DatabaseErrorCodes.NotFound, StringComparison.Ordinal))
				{
					return NotFound(new { error = "There is no such message in the email queue." });
				}
				return BadRequest(new { error = result.ErrorMessage ?? "That message could not be re-queued." });
			}

			var outcome = result.Data;

			/* The account is the target worth recording, not the row id. A queue row is deleted
			 * or superseded and its number means nothing a week later; "whose verification mail
			 * did somebody re-queue" is the question the log is actually asked. The recipient
			 * address is deliberately NOT put in Details — the audit log is read by more people
			 * than this page is, and the username identifies the account just as well. */
			audit.TargetName = outcome.RecipientUsername;
			audit.Details = new { retried = outcome.Retried, attempts = outcome.Attempts, claimedBy = outcome.ClaimedBy };

			if (!outcome.Retried)
			{
				audit.Outcome = outcome.Refusal;
				return BadRequest(new { error = outcome.Refusal });
			}

			log.LogWarning("Email queue row {Id} for '{Account}' re-queued by '{Actor}' after {Attempts} attempt(s). Reason: {Reason}",
				id, outcome.RecipientUsername, User.Identity?.Name, outcome.Attempts, request.Reason);

			return Ok(new
			{
				id,
				recipientUsername = outcome.RecipientUsername,
				attempts = outcome.Attempts,
				// What was written, not what was delivered. See the remarks above.
				message = "Claim released — the message is back in the queue. It goes out when a login server next claims it, " +
					"so if nothing is claiming, it will sit there and the pending age will keep climbing.",
			});
		}

		/// <summary>
		/// Parses the state filter, treating absent as "any" and anything unrecognised as an
		/// error.
		/// </summary>
		/// <remarks>
		/// A misspelled state must not quietly widen to every row. An operator who filtered to
		/// "faild" and was shown the whole queue would read the sent messages in it as failures.
		/// <c>Enum.TryParse</c> alone is not enough: it happily accepts any number in range —
		/// and out of it — so the parsed value is checked against the defined members.
		/// </remarks>
		private static bool TryParseState(string value, out EmailQueueState? state)
		{
			state = null;
			if (string.IsNullOrWhiteSpace(value))
			{
				return true;
			}
			if (!Enum.TryParse(value.Trim(), ignoreCase: true, out EmailQueueState parsed) ||
				!Enum.IsDefined(typeof(EmailQueueState), parsed))
			{
				return false;
			}
			state = parsed;
			return true;
		}

		/// <summary>The wire name of a state: lower case, matching what the filter accepts.</summary>
		private static string StateName(EmailQueueState state) => state switch
		{
			EmailQueueState.Pending => "pending",
			EmailQueueState.Claimed => "claimed",
			EmailQueueState.Failed => "failed",
			EmailQueueState.Sent => "sent",
			_ => "unknown",
		};

		/// <summary>
		/// Seconds between a timestamp and the instant of the read, or null when there is no
		/// timestamp.
		/// </summary>
		/// <remarks>
		/// Measured against the database read rather than against the browser's clock, and never
		/// negative: a panel host whose clock runs behind the database's would otherwise turn a
		/// freshly queued message into an age "in the future", which reads as a bug in the page
		/// rather than in the deployment.
		/// </remarks>
		private static double? AgeSeconds(DateTime? timestamp, DateTime now) =>
			timestamp.HasValue ? Math.Round(Math.Max(0, (now - timestamp.Value).TotalSeconds), 1) : null;

		/// <summary>A request carrying only the audit reason.</summary>
		public sealed class ReasonRequest
		{
			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
