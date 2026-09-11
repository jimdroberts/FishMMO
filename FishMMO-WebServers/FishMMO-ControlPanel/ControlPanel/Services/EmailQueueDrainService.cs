using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Delivers the outbound email queue. This is the process that turns a queued row into a
	/// message in somebody's inbox.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This used to live in the LoginServer</b>, driven from a Unity frame callback: a timer
	/// accumulated <c>deltaTime</c>, one message went out every ten seconds, the send was
	/// fire-and-forget with a flag to stop two overlapping, and the blocking network call to the
	/// relay happened on a game server's tick. It worked, and none of it belonged there —
	/// sending mail was paced by frame time, and a relay that took its time held up a loop whose
	/// job is players. This is an ASP.NET Core host with a thread pool, real asynchrony and an
	/// existing <see cref="BackgroundService"/> pattern, so the work moves here and the game
	/// server goes back to only enqueueing.
	/// </para>
	/// <para>
	/// <b>The queue is the coordination, not this class.</b> <c>DequeueNextAsync</c> claims a
	/// row with <c>FOR UPDATE SKIP LOCKED</c>, so a panel and a login server — or two panels —
	/// racing for the same row is already handled: each gets a different row or nothing. Nothing
	/// here assumes it is the only drain, which is what makes the migration safe to do one
	/// process at a time.
	/// </para>
	/// <para>
	/// <b>The verification stamp is conditional, and that condition is the reason
	/// <see cref="EmailKind"/> exists.</b> After a successful send the old drain always called
	/// <c>PersistVerificationEmailSentAsync</c>, which stamps <c>verification_email_sent_at</c>
	/// and thereby ENDS the grace period an unverified account logs in under — both
	/// <c>ServerAuthenticator</c> and <see cref="SrpLoginService"/> allow an unverified account
	/// in only while that stamp is null. That was correct when verification mail was the only
	/// mail in the table. It no longer is: the panel queues password resets here too, and
	/// stamping after one of those would lock an unverified player out of the game and the panel
	/// at the exact moment they finished recovering their password. So the stamp follows the
	/// row's kind, and the kind is a column rather than a guess at the subject line.
	/// </para>
	/// <para>
	/// <b>Nothing here is audited.</b> The audit log records what an operator did; a background
	/// loop delivering mail is not an operator action, and writing one audit row per verification
	/// email would bury the rows that record a person doing something.
	/// </para>
	/// </remarks>
	public sealed class EmailQueueDrainService : BackgroundService
	{
		/// <summary>How long between passes when everything is healthy.</summary>
		/// <remarks>
		/// <para>
		/// Two seconds, against the LoginServer's ten. The old interval was not chosen for the
		/// relay's sake — it was the pace at which it seemed acceptable to do blocking I/O on a
		/// game loop — and it meant a player who had just typed their email sat waiting on a
		/// timer for no reason. Two seconds keeps a quiet queue's cost at one indexed
		/// <c>SKIP LOCKED</c> claim that finds nothing, which is what it will find almost always.
		/// </para>
		/// <para>
		/// Configurable via <c>Email:DrainIntervalSeconds</c> for a deployment whose relay is
		/// stricter than the defaults assume.
		/// </para>
		/// </remarks>
		private const int DefaultIntervalSeconds = 2;

		/// <summary>How many messages one pass may deliver.</summary>
		/// <remarks>
		/// <para>
		/// Five. Exactly one per pass — which is what the old drain did — is fine on a quiet
		/// shard and badly wrong after any interruption: a relay outage, a panel restart or a
		/// burst of registrations leaves a backlog that then drains at one message per interval,
		/// so fifty queued accounts wait nearly two minutes for the last of them even though the
		/// relay is healthy again.
		/// </para>
		/// <para>
		/// Five rather than fifty because a free relay tier is the constraint that matters, and
		/// those are rated per second as well as per month. The batch is sent sequentially, one
		/// connection at a time, each waiting for the relay to accept the last — so five per two
		/// seconds is a ceiling of 2.5 messages a second and, in practice, closer to one, since
		/// a round trip to a relay is not free. That fits comfortably inside the per-second
		/// limits published by the relays this deployment is likely to use, while clearing a
		/// hundred-message backlog in well under a minute.
		/// </para>
		/// <para>
		/// Configurable via <c>Email:MaxPerPass</c>. Sequential is not configurable, deliberately:
		/// parallel sends would multiply the per-second rate by the degree of parallelism, which
		/// is the number a rate limiter is watching.
		/// </para>
		/// </remarks>
		private const int DefaultMaxPerPass = 5;

		/// <summary>The longest a pass will wait after repeated failures.</summary>
		private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

		private readonly IServiceScopeFactory scopeFactory;
		private readonly ISmtpSender sender;
		private readonly ILogger<EmailQueueDrainService> log;

		private readonly TimeSpan interval;
		private readonly int maxPerPass;

		/// <summary>
		/// Who this process says it is when it claims a row.
		/// </summary>
		/// <remarks>
		/// The Queues page shows <c>claimed_by</c> verbatim, and an operator looking at a stuck
		/// message needs to know which process to go and look at. Naming the machine as well as
		/// the role distinguishes two panels behind one load balancer, and the suffix
		/// distinguishes all of them from a login server's bare <c>ServerName</c>. Bounded to the
		/// column's 100 characters.
		/// </remarks>
		private readonly string claimIdentity;

		/// <summary>Creates the service.</summary>
		public EmailQueueDrainService(
			IServiceScopeFactory scopeFactory,
			ISmtpSender sender,
			IConfiguration configuration,
			ILogger<EmailQueueDrainService> log)
		{
			this.scopeFactory = scopeFactory;
			this.sender = sender;
			this.log = log;

			int seconds = configuration.GetValue("Email:DrainIntervalSeconds", DefaultIntervalSeconds);
			interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300));

			int batch = configuration.GetValue("Email:MaxPerPass", DefaultMaxPerPass);
			maxPerPass = Math.Clamp(batch, 1, 50);

			string identity = $"{Environment.MachineName}/control-panel";
			claimIdentity = identity.Length <= 100 ? identity : identity.Substring(0, 100);
		}

		/// <inheritdoc />
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			/* A panel with no relay is a supported deployment — a development host, or a shard
			 * whose mail goes out from somewhere else — so this is one informational line and
			 * then silence. Polling a queue it could not deliver from would cost a database round
			 * trip every couple of seconds forever and fill the log with the same complaint. The
			 * settings are read once at startup, so there is nothing to re-check: configuring a
			 * relay means restarting the panel. */
			if (!sender.IsConfigured)
			{
				log.LogInformation(
					"Email queue drain is idle: {Reason}. Queued mail will wait until a relay is configured and the panel restarts.",
					sender.ConfigurationSummary);
				return;
			}

			log.LogInformation(
				"Email queue drain started as '{Claim}': {Relay}; up to {Batch} message(s) every {Interval}s.",
				claimIdentity, sender.ConfigurationSummary, maxPerPass, interval.TotalSeconds);

			TimeSpan delay = interval;
			int consecutiveFailedPasses = 0;

			while (!stoppingToken.IsCancellationRequested)
			{
				if (!await SafeDelayAsync(delay, stoppingToken).ConfigureAwait(false))
				{
					return;
				}

				bool failed;
				try
				{
					failed = !await RunPassAsync(stoppingToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					/* Never let a pass take the host down with it. The panel is the surface an
					 * operator opens during an incident, and refusing to serve it because mail
					 * could not be sent would turn a relay problem into an outage. */
					log.LogError(ex, "Email queue drain pass threw.");
					failed = true;
				}

				if (!failed)
				{
					if (consecutiveFailedPasses > 0)
					{
						log.LogInformation("Email queue drain recovered after {Count} failed pass(es).",
							consecutiveFailedPasses);
					}
					consecutiveFailedPasses = 0;
					delay = interval;
					continue;
				}

				/* Back off on repeated failure, doubling to a minute. Two things make this worth
				 * having rather than hammering at the healthy interval: a relay that is refusing
				 * everything would otherwise burn a message's five attempts in ten seconds and
				 * bury it as permanently failed over what may be a brief outage, and a database
				 * that is down would otherwise be asked every two seconds forever. */
				consecutiveFailedPasses++;
				TimeSpan next = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
				bool reachedCap = delay >= MaxBackoff;
				delay = next;

				if (!reachedCap)
				{
					log.LogWarning("Email queue drain pass failed ({Count} in a row); retrying in {Delay}s.",
						consecutiveFailedPasses, delay.TotalSeconds);
					if (delay >= MaxBackoff)
					{
						log.LogWarning(
							"Email queue drain is now at its {Delay}s backoff; further failures are logged at Debug until it recovers. The queue page shows each message's last error.",
							MaxBackoff.TotalSeconds);
					}
				}
				else
				{
					// Already said it, loudly, once. Repeating it every minute would only bury
					// whatever eventually explains it.
					log.LogDebug("Email queue drain pass failed ({Count} in a row).", consecutiveFailedPasses);
				}
			}
		}

		/// <summary>
		/// Claims and delivers up to <see cref="maxPerPass"/> messages.
		/// </summary>
		/// <returns>
		/// False when something went wrong that should slow the next pass down: a send that
		/// failed, or a queue that could not be read. True for a clean pass, including the
		/// overwhelmingly common one that found nothing to do.
		/// </returns>
		private async Task<bool> RunPassAsync(CancellationToken stoppingToken)
		{
			/* A scope per pass, not per host lifetime: the database services are scoped, and
			 * holding one open for days would pin a context and its connection for the sake of a
			 * loop that is idle almost all the time. */
			using var scope = scopeFactory.CreateScope();
			var queue = scope.ServiceProvider.GetRequiredService<IEmailQueueService>();
			var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

			for (int i = 0; i < maxPerPass && !stoppingToken.IsCancellationRequested; i++)
			{
				var claim = await queue.DequeueNextAsync(claimIdentity, stoppingToken).ConfigureAwait(false);
				if (!claim.IsSuccess)
				{
					/* An empty queue reports itself as NOT_FOUND, and that is the normal case:
					 * it is neither logged nor treated as a failure, or a healthy quiet shard
					 * would back off to a minute and stay there. Anything else is the database
					 * saying something is wrong, and is worth both the line and the backoff. */
					if (claim.ErrorCode == DatabaseErrorCodes.NotFound)
					{
						return true;
					}

					log.LogWarning("Could not claim a queued email: [{Code}] {Message}",
						claim.ErrorCode, claim.ErrorMessage);
					return false;
				}

				var email = claim.Data;

				bool sent = await sender.SendAsync(
					email.RecipientEmail, email.Subject, email.Body, stoppingToken).ConfigureAwait(false);

				if (!sent)
				{
					/* Records the attempt and — while attempts remain — releases the claim, which
					 * is what puts the message back in line rather than leaving it held by a
					 * process that could not deliver it. One message that cannot be sent
					 * therefore cannot block the queue: it comes back, fails its way through its
					 * attempts, and is then left with its error for an operator to retry by hand
					 * from the Queues page. */
					var marked = await queue.MarkFailedAsync(
						email.ID, "SMTP delivery failed; see the panel log for the relay's response.",
						cancellationToken: stoppingToken).ConfigureAwait(false);
					if (!marked.IsSuccess)
					{
						log.LogError("Email {Id} failed to send AND could not be marked failed: [{Code}] {Message}. It stays claimed until its claim is released by hand.",
							email.ID, marked.ErrorCode, marked.ErrorMessage);
					}

					// Stop the pass. The relay just refused; the next four messages would almost
					// certainly be refused too, and each refusal costs one of their attempts.
					return false;
				}

				var sentMark = await queue.MarkSentAsync(email.ID, stoppingToken).ConfigureAwait(false);
				if (!sentMark.IsSuccess)
				{
					/* The mail is gone and the row does not know it. It is still claimed, so
					 * nothing will pick it up on its own — but an operator pressing Retry on it
					 * would send a second copy, so this is an error rather than a warning: the
					 * row's state is now a lie and somebody has to know it. */
					log.LogError("Email {Id} was DELIVERED but could not be marked sent: [{Code}] {Message}. Do not retry it.",
						email.ID, sentMark.ErrorCode, sentMark.ErrorMessage);
				}

				await StampVerificationIfNeededAsync(accounts, email, stoppingToken).ConfigureAwait(false);
			}

			return true;
		}

		/// <summary>
		/// Ends the account's unverified grace period — but only for a verification email.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Stamping <c>verification_email_sent_at</c> is what turns "unverified but allowed in"
		/// into "unverified and blocked until you type the code", and it is correct precisely
		/// once: when the code has actually reached the mailbox. That is why it happens here,
		/// after the relay accepted the message, and not when the row was enqueued — the
		/// LoginServer learned that the hard way, where stamping on enqueue with an unreachable
		/// relay locked accounts out permanently over mail that never went anywhere.
		/// </para>
		/// <para>
		/// <b>A password reset must not be stamped.</b> Its recipient may well be an unverified
		/// account — somebody who registered, never typed the code, and has now forgotten their
		/// password — and stamping would lock them out of the game and the panel at the moment
		/// they recovered access. Nothing about delivering a reset says anything about whether a
		/// verification code reached them.
		/// </para>
		/// <para>
		/// A failure here is logged and not retried: the mail went out, and the queue has no way
		/// to express "delivered, bookkeeping pending". The cost of missing it is that an
		/// unverified account keeps its grace period a while longer, which is the safe direction
		/// — the opposite mistake locks a player out.
		/// </para>
		/// </remarks>
		private async Task StampVerificationIfNeededAsync(
			IAccountService accounts,
			EmailQueueData email,
			CancellationToken stoppingToken)
		{
			if (email.Kind != EmailKind.Verification)
			{
				log.LogDebug("Delivered a {Kind} email for '{User}'; verification state untouched.",
					email.Kind, email.RecipientUsername);
				return;
			}

			var stamped = await accounts.PersistVerificationEmailSentAsync(
				email.RecipientUsername, stoppingToken).ConfigureAwait(false);
			if (!stamped.IsSuccess)
			{
				log.LogWarning("Delivered the verification email for '{User}' but could not mark it sent: [{Code}] {Message}",
					email.RecipientUsername, stamped.ErrorCode, stamped.ErrorMessage);
			}
		}

		/// <summary>Waits, treating shutdown as an ordinary end rather than a fault.</summary>
		private static async Task<bool> SafeDelayAsync(TimeSpan delay, CancellationToken stoppingToken)
		{
			try
			{
				await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
				return true;
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}
	}
}
