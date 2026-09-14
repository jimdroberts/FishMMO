using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Delivers the outbound SMS queue. The deliberate twin of <see cref="EmailQueueDrainService"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Same shape, same reasons: the queue claim (<c>FOR UPDATE SKIP LOCKED</c>) is the coordination,
	/// a scope is opened per pass, a failed send stops the pass and doubles the delay to a minute,
	/// and the claim identity names the machine. See the email drain for why each of those is so.
	/// Configured by <c>Sms:DrainIntervalSeconds</c> and <c>Sms:MaxPerPass</c>.
	/// </para>
	/// <para>
	/// <b>A delivered verification SMS stamps <c>verification_email_sent_at</c>.</b> What that column
	/// records is "a verification code has reached the player", which staff read when an account cannot
	/// verify; the database has no separate SMS stamp, and an SMS-only account would otherwise always
	/// look as if nothing had arrived. Sign-in does not read it. Notifications stamp nothing, for the
	/// same reason a password reset email stamps nothing.
	/// </para>
	/// <para>Nothing here is audited: a background loop is not an operator.</para>
	/// </remarks>
	public sealed class SmsQueueDrainService : BackgroundService
	{
		private const int DefaultIntervalSeconds = 2;
		private const int DefaultMaxPerPass = 5;
		private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

		private readonly IServiceScopeFactory scopeFactory;
		private readonly ISmsSender sender;
		private readonly ILogger<SmsQueueDrainService> log;
		private readonly TimeSpan interval;
		private readonly int maxPerPass;
		private readonly string claimIdentity;

		public SmsQueueDrainService(
			IServiceScopeFactory scopeFactory,
			ISmsSender sender,
			IConfiguration configuration,
			ILogger<SmsQueueDrainService> log)
		{
			this.scopeFactory = scopeFactory;
			this.sender = sender;
			this.log = log;

			int seconds = configuration.GetValue("Sms:DrainIntervalSeconds", DefaultIntervalSeconds);
			interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300));

			int batch = configuration.GetValue("Sms:MaxPerPass", DefaultMaxPerPass);
			maxPerPass = Math.Clamp(batch, 1, 50);

			string identity = $"{Environment.MachineName}/control-panel";
			claimIdentity = identity.Length <= 100 ? identity : identity.Substring(0, 100);
		}

		/// <inheritdoc />
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (!sender.IsConfigured)
			{
				log.LogInformation(
					"SMS queue drain is idle: {Reason}. Queued messages will wait until a sender is configured and the panel restarts.",
					sender.ConfigurationSummary);
				return;
			}

			log.LogInformation(
				"SMS queue drain started as '{Claim}': {Sender}; up to {Batch} message(s) every {Interval}s.",
				claimIdentity, sender.ConfigurationSummary, maxPerPass, interval.TotalSeconds);

			TimeSpan delay = interval;
			int consecutiveFailedPasses = 0;

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
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
					log.LogError(ex, "SMS queue drain pass threw.");
					failed = true;
				}

				if (!failed)
				{
					if (consecutiveFailedPasses > 0)
					{
						log.LogInformation("SMS queue drain recovered after {Count} failed pass(es).", consecutiveFailedPasses);
					}
					consecutiveFailedPasses = 0;
					delay = interval;
					continue;
				}

				consecutiveFailedPasses++;
				bool reachedCap = delay >= MaxBackoff;
				delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
				if (!reachedCap)
				{
					log.LogWarning("SMS queue drain pass failed ({Count} in a row); retrying in {Delay}s.",
						consecutiveFailedPasses, delay.TotalSeconds);
				}
				else
				{
					log.LogDebug("SMS queue drain pass failed ({Count} in a row).", consecutiveFailedPasses);
				}
			}
		}

		private async Task<bool> RunPassAsync(CancellationToken stoppingToken)
		{
			using var scope = scopeFactory.CreateScope();
			var queue = scope.ServiceProvider.GetRequiredService<ISmsQueueService>();
			var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

			for (int i = 0; i < maxPerPass && !stoppingToken.IsCancellationRequested; i++)
			{
				var claim = await queue.DequeueNextAsync(claimIdentity, stoppingToken).ConfigureAwait(false);
				if (!claim.IsSuccess)
				{
					if (claim.ErrorCode == DatabaseErrorCodes.NotFound)
					{
						return true;
					}
					log.LogWarning("Could not claim a queued SMS: [{Code}] {Message}", claim.ErrorCode, claim.ErrorMessage);
					return false;
				}

				var message = claim.Data;
				bool sent = await sender.SendAsync(message.RecipientPhone, message.Body, stoppingToken).ConfigureAwait(false);
				if (!sent)
				{
					var marked = await queue.MarkFailedAsync(message.ID, "SMS delivery failed; see the panel log.",
						cancellationToken: stoppingToken).ConfigureAwait(false);
					if (!marked.IsSuccess)
					{
						log.LogError("SMS {Id} failed to send AND could not be marked failed: [{Code}] {Message}.",
							message.ID, marked.ErrorCode, marked.ErrorMessage);
					}
					return false;
				}

				var sentMark = await queue.MarkSentAsync(message.ID, stoppingToken).ConfigureAwait(false);
				if (!sentMark.IsSuccess)
				{
					log.LogError("SMS {Id} was DELIVERED but could not be marked sent: [{Code}] {Message}. Do not retry it.",
						message.ID, sentMark.ErrorCode, sentMark.ErrorMessage);
				}

				if (message.Kind == SmsKind.Verification)
				{
					var stamped = await accounts.PersistVerificationEmailSentAsync(message.RecipientUsername, stoppingToken).ConfigureAwait(false);
					if (!stamped.IsSuccess)
					{
						log.LogWarning("Delivered the verification SMS for '{User}' but could not stamp it: [{Code}] {Message}",
							message.RecipientUsername, stamped.ErrorCode, stamped.ErrorMessage);
					}
				}
			}

			return true;
		}
	}
}
