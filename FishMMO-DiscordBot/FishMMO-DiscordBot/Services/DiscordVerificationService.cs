using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Delivers the Discord account-verification DM: the one message, ever, that carries an account's
	/// Discord verification code.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Who does what.</b> The game and the Control Panel issue the code; the player types it back into
	/// the game or the panel. This service only carries it. Nothing is ever typed into Discord.
	/// </para>
	/// <para>
	/// <b>No polling.</b> Issuing a code raises a PostgreSQL notification on
	/// <see cref="DiscordVerification.NotifyChannel"/>, which a dedicated connection here LISTENs for. A slow
	/// sweep catches anything raised while the bot or its listener was down, the gateway's Ready event
	/// catches up after a reconnect, and a player who was not in the Discord server yet is served from the
	/// member-joined event with a targeted read of just their username.
	/// </para>
	/// <para>
	/// <b>One DM, ever.</b> A delivery is claimed before it is sent and marked delivered after. A send that
	/// Discord definitely refused (a 4xx, such as closed DMs) is released and counts as an attempt. A send
	/// whose outcome is unknown (a timeout, a 5xx, a dropped connection) may have reached the player, so it
	/// is deliberately left claimed for staff to check: one DM ever beats a guaranteed delivery.
	/// </para>
	/// <para>
	/// <b>Single flight.</b> Every wake source only signals a one-slot channel, and one loop reads it, so
	/// passes never overlap and any number of wakes during a pass collapse into one more pass.
	/// </para>
	/// </remarks>
	public sealed class DiscordVerificationService : BackgroundService
	{
		private readonly DiscordSocketClient discordClient;
		private readonly IDiscordAccountService accountService;
		private readonly NpgsqlDbContextFactory dbContextFactory;
		private readonly ILogger<DiscordVerificationService> logger;
		private readonly ulong guildId;
		private readonly TimeSpan sweepInterval;
		private readonly int batchSize;
		private readonly TimeSpan minDmInterval;

		/// <summary>One slot: a wake while a pass is queued or running is folded into that one.</summary>
		private readonly Channel<bool> wakeSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
		{
			FullMode = BoundedChannelFullMode.DropWrite,
			SingleReader = true,
		});

		/// <summary>Username keys of members who joined since the last pass.</summary>
		private readonly ConcurrentQueue<SocketGuildUser> joinedMembers = new ConcurrentQueue<SocketGuildUser>();

		/// <summary>1 when a full pass has been asked for and not yet started.</summary>
		private int fullPassRequested;

		/// <summary>When the last DM was attempted, as a <see cref="Stopwatch"/> timestamp; 0 before the first.</summary>
		private long lastDmTimestamp;

		/// <summary>Whether the "bot is not in the configured guild" warning has been logged for this outage.</summary>
		private bool guildMissingLogged;

		/// <summary>Whether the "member list still downloading" note has been logged for this outage.</summary>
		private bool membersPendingLogged;

		/// <summary>
		/// Initializes a new instance of the <see cref="DiscordVerificationService"/> class.
		/// </summary>
		/// <param name="discordClient">The Discord socket client.</param>
		/// <param name="accountService">The database's Discord delivery and link service.</param>
		/// <param name="dbContextFactory">Factory whose connection string the LISTEN connection reuses.</param>
		/// <param name="configuration">Application configuration.</param>
		/// <param name="logger">Logger instance.</param>
		public DiscordVerificationService(
			DiscordSocketClient discordClient,
			IDiscordAccountService accountService,
			NpgsqlDbContextFactory dbContextFactory,
			IConfiguration configuration,
			ILogger<DiscordVerificationService> logger)
		{
			this.discordClient = discordClient;
			this.accountService = accountService;
			this.dbContextFactory = dbContextFactory;
			this.logger = logger;

			ulong.TryParse(configuration.GetSection("Discord")["DefaultGuildId"], out guildId);

			var section = configuration.GetSection("DiscordVerification");
			sweepInterval = TimeSpan.FromMinutes(DiscordVerificationRules.ReadClamped(
				section["SweepMinutes"],
				DiscordVerificationRules.DefaultSweepMinutes,
				DiscordVerificationRules.MinSweepMinutes,
				DiscordVerificationRules.MaxSweepMinutes));
			batchSize = DiscordVerificationRules.ReadClamped(
				section["BatchSize"],
				DiscordVerificationRules.DefaultBatchSize,
				DiscordVerificationRules.MinBatchSize,
				DiscordVerificationRules.MaxBatchSize);
			minDmInterval = TimeSpan.FromMilliseconds(DiscordVerificationRules.ReadClamped(
				section["MinDmIntervalMs"],
				DiscordVerificationRules.DefaultMinDmIntervalMs,
				DiscordVerificationRules.MinDmIntervalFloorMs,
				DiscordVerificationRules.MaxDmIntervalMs));
		}

		/// <inheritdoc />
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (guildId == 0)
			{
				logger.LogWarning(
					"Discord verification delivery is disabled: Discord:DefaultGuildId is not set. " +
					"Players who chose Discord verification will not be sent their code.");
				return;
			}

			logger.LogInformation(
				"DiscordVerificationService starting for guild {GuildId}: sweep every {SweepMinutes} min, batch {BatchSize}, at least {MinDmIntervalMs} ms between DMs.",
				guildId, sweepInterval.TotalMinutes, batchSize, minDmInterval.TotalMilliseconds);

			discordClient.Ready += OnReady;
			discordClient.GuildMembersDownloaded += OnGuildMembersDownloaded;
			discordClient.UserJoined += OnUserJoined;

			try
			{
				Task listener = ListenLoopAsync(stoppingToken);
				Task sweeper = SweepLoopAsync(stoppingToken);

				// Catch up on anything owed from before this start.
				RequestFullPass();

				await ProcessLoopAsync(stoppingToken);
				await Task.WhenAll(listener, sweeper);
			}
			finally
			{
				discordClient.Ready -= OnReady;
				discordClient.GuildMembersDownloaded -= OnGuildMembersDownloaded;
				discordClient.UserJoined -= OnUserJoined;
			}
		}

		#region Wake sources

		/// <summary>Asks for a full pass. Coalesces with any pass already queued.</summary>
		private void RequestFullPass()
		{
			Interlocked.Exchange(ref fullPassRequested, 1);
			wakeSignal.Writer.TryWrite(true);
		}

		/// <summary>After every new gateway session: notifications may have been raised while disconnected.</summary>
		private Task OnReady()
		{
			RequestFullPass();
			return Task.CompletedTask;
		}

		/// <summary>The member cache a pass matches against is complete; anything waiting on it can go.</summary>
		private Task OnGuildMembersDownloaded(SocketGuild guild)
		{
			if (guild.Id == guildId)
			{
				RequestFullPass();
			}
			return Task.CompletedTask;
		}

		/// <summary>
		/// A member joined: queue a targeted pass for their username. Never does database or Discord work on
		/// the gateway's thread.
		/// </summary>
		private Task OnUserJoined(SocketGuildUser user)
		{
			if (user.Guild.Id == guildId && !user.IsBot)
			{
				joinedMembers.Enqueue(user);
				wakeSignal.Writer.TryWrite(true);
			}
			return Task.CompletedTask;
		}

		/// <summary>
		/// Holds a dedicated connection on <c>LISTEN</c>, reconnecting with capped exponential backoff. Logs
		/// once per outage, and wakes a pass after every (re)connect because a notification may have been
		/// missed while it was down.
		/// </summary>
		private async Task ListenLoopAsync(CancellationToken stoppingToken)
		{
			int consecutiveFailures = 0;
			bool outageLogged = false;

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await using var connection = new NpgsqlConnection(BuildListenConnectionString());
					connection.Notification += (_, args) =>
					{
						if (args.Channel == DiscordVerification.NotifyChannel)
						{
							RequestFullPass();
						}
					};

					await connection.OpenAsync(stoppingToken);
					await using (var listen = new NpgsqlCommand($"LISTEN {DiscordVerification.NotifyChannel}", connection))
					{
						await listen.ExecuteNonQueryAsync(stoppingToken);
					}

					if (outageLogged)
					{
						logger.LogInformation("Discord verification listener reconnected to the database.");
					}
					else
					{
						logger.LogInformation("Discord verification listener is listening on {Channel}.", DiscordVerification.NotifyChannel);
					}
					outageLogged = false;
					consecutiveFailures = 0;

					RequestFullPass();

					while (!stoppingToken.IsCancellationRequested)
					{
						await connection.WaitAsync(stoppingToken);
					}
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					consecutiveFailures++;
					if (!outageLogged)
					{
						outageLogged = true;
						logger.LogWarning(ex,
							"Discord verification listener lost its database connection. Retrying with backoff; the {SweepMinutes}-minute sweep still runs meanwhile.",
							sweepInterval.TotalMinutes);
					}

					try
					{
						await Task.Delay(DiscordVerificationRules.ReconnectDelay(consecutiveFailures), stoppingToken);
					}
					catch (OperationCanceledException)
					{
						break;
					}
				}
			}
		}

		/// <summary>
		/// The bot's own connection string with pooling off: a LISTEN is session state, and the shared pool
		/// resets nothing on return (<c>NoResetOnClose</c>), so this connection must never be handed back to
		/// it. Keepalive makes a dead socket surface as an exception from <c>WaitAsync</c> so the loop reconnects.
		/// </summary>
		private string BuildListenConnectionString()
		{
			string? connectionString;
			using (var dbContext = dbContextFactory.CreateDbContext())
			{
				connectionString = dbContext.Database.GetConnectionString();
			}

			return new NpgsqlConnectionStringBuilder(connectionString ?? string.Empty)
			{
				Pooling = false,
				KeepAlive = 30,
			}.ConnectionString;
		}

		/// <summary>The safety sweep.</summary>
		private async Task SweepLoopAsync(CancellationToken stoppingToken)
		{
			using var timer = new PeriodicTimer(sweepInterval);
			try
			{
				while (await timer.WaitForNextTickAsync(stoppingToken))
				{
					RequestFullPass();
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		#endregion

		#region Passes

		/// <summary>The single consumer of <see cref="wakeSignal"/>: runs passes one at a time.</summary>
		private async Task ProcessLoopAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await wakeSignal.Reader.ReadAsync(stoppingToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				try
				{
					await RunPassAsync(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Discord verification pass failed.");
				}
			}
		}

		/// <summary>Runs the targeted join passes that are queued, then a full pass if one was asked for.</summary>
		private async Task RunPassAsync(CancellationToken stoppingToken)
		{
			if (discordClient.ConnectionState != ConnectionState.Connected)
			{
				// Ready wakes a pass when the gateway is back. Joins seen before the drop keep their queue slot.
				return;
			}

			SocketGuild? guild = discordClient.GetGuild(guildId);
			if (guild == null)
			{
				if (!guildMissingLogged)
				{
					guildMissingLogged = true;
					logger.LogWarning(
						"Discord verification cannot deliver: the bot is not in guild {GuildId} (Discord:DefaultGuildId), or it is unavailable.",
						guildId);
				}
				return;
			}
			guildMissingLogged = false;

			if (!guild.HasAllMembers)
			{
				/* Matching a username against a half-downloaded member list would record "waiting to join"
				 * for players who are in fact members, and spend each one's single REST search. The
				 * member-download event wakes a pass when the list is complete. */
				if (!membersPendingLogged)
				{
					membersPendingLogged = true;
					logger.LogDebug("Discord verification is waiting for guild {GuildId}'s member list to finish downloading.", guildId);
				}
				return;
			}
			membersPendingLogged = false;

			await RunJoinPassesAsync(guild, stoppingToken);

			if (Interlocked.Exchange(ref fullPassRequested, 0) == 1)
			{
				await RunFullPassAsync(guild, stoppingToken);
			}
		}

		/// <summary>For each member who joined, reads only the deliveries owed to their username keys.</summary>
		private async Task RunJoinPassesAsync(SocketGuild guild, CancellationToken stoppingToken)
		{
			var seenKeys = new HashSet<string>(StringComparer.Ordinal);
			while (joinedMembers.TryDequeue(out SocketGuildUser? member))
			{
				foreach (string key in DiscordVerificationRules.UsernameKeysForMember(member.Username, member.Discriminator))
				{
					if (!seenKeys.Add(key))
					{
						continue;
					}

					stoppingToken.ThrowIfCancellationRequested();
					var pending = await accountService.FetchPendingDeliveriesForUsernameAsync(key, stoppingToken);
					if (!pending.IsSuccess)
					{
						logger.LogWarning(
							"Could not read Discord deliveries for a joining member ({ErrorCode}: {ErrorMessage}). The sweep will retry.",
							pending.ErrorCode, pending.ErrorMessage);
						continue;
					}

					foreach (var delivery in pending.Data)
					{
						stoppingToken.ThrowIfCancellationRequested();
						await ProcessDeliveryAsync(guild, delivery, member, stoppingToken);
					}
				}
			}
		}

		/// <summary>Reads a batch of owed deliveries and tries each one.</summary>
		private async Task RunFullPassAsync(SocketGuild guild, CancellationToken stoppingToken)
		{
			var pending = await accountService.FetchPendingDeliveriesAsync(batchSize, stoppingToken);
			if (!pending.IsSuccess)
			{
				logger.LogWarning(
					"Could not read pending Discord deliveries ({ErrorCode}: {ErrorMessage}). The next wake or sweep will retry.",
					pending.ErrorCode, pending.ErrorMessage);
				return;
			}

			foreach (var delivery in pending.Data)
			{
				stoppingToken.ThrowIfCancellationRequested();
				await ProcessDeliveryAsync(guild, delivery, null, stoppingToken);
			}
		}

		#endregion

		#region One delivery

		/// <summary>
		/// Resolves one delivery's member, claims it, checks the link, and sends the DM at most once.
		/// </summary>
		/// <param name="guild">The configured guild.</param>
		/// <param name="delivery">The delivery.</param>
		/// <param name="joinedMember">The member whose join caused this pass, which may not be in the cache yet.</param>
		/// <param name="stoppingToken">Host shutdown. Never passed to a write that follows a claim.</param>
		private async Task ProcessDeliveryAsync(
			SocketGuild guild,
			DiscordVerificationDelivery delivery,
			SocketGuildUser? joinedMember,
			CancellationToken stoppingToken)
		{
			string username = delivery.DiscordUsername;

			IEnumerable<IUser> cached = guild.Users;
			if (joinedMember != null)
			{
				cached = cached.Append(joinedMember);
			}

			var match = DiscordVerificationRules.FindMember(cached, username, u => u.Id, u => u.Username, u => u.Discriminator, u => u.IsBot);

			if (match.Status == MemberMatchStatus.NotFound && DiscordVerificationRules.IsFirstTry(delivery))
			{
				// The one REST lookup a delivery ever gets; later tries rely on the cache and the join event.
				match = await SearchMemberAsync(guild, username, stoppingToken);
			}

			if (match.Status == MemberMatchStatus.Ambiguous)
			{
				logger.LogWarning(
					"Discord verification for account '{AccountName}' matched more than one member named {DiscordUsername}; not sending.",
					delivery.AccountName, username);
				await RecordReasonAsync(delivery, DiscordVerificationRules.AmbiguousMemberReason(username));
				return;
			}

			if (match.Status == MemberMatchStatus.NotFound || match.Member == null)
			{
				logger.LogDebug(
					"Discord verification for account '{AccountName}' is waiting for {DiscordUsername} to join the server.",
					delivery.AccountName, username);
				await RecordReasonAsync(delivery, DiscordVerificationRules.WaitingToJoinReason(username));
				return;
			}

			IUser member = match.Member;

			// Writes from here on ignore shutdown: abandoning one half-way would strand the claim.
			var claim = await accountService.ClaimDeliveryAsync(delivery.AccountName, CancellationToken.None);
			if (!claim.IsSuccess)
			{
				logger.LogWarning(
					"Could not claim the Discord delivery for account '{AccountName}' ({ErrorCode}: {ErrorMessage}).",
					delivery.AccountName, claim.ErrorCode, claim.ErrorMessage);
				return;
			}
			if (!claim.Data)
			{
				logger.LogDebug("Discord delivery for account '{AccountName}' is no longer owed or was taken; skipping.", delivery.AccountName);
				return;
			}

			long signedUserId = unchecked((long)member.Id);
			var link = await accountService.FetchLinkByDiscordUserAsync(signedUserId, CancellationToken.None);
			if (!link.IsSuccess)
			{
				logger.LogWarning(
					"Could not check Discord user {DiscordUserId}'s account link for account '{AccountName}' ({ErrorCode}: {ErrorMessage}); releasing unsent.",
					member.Id, delivery.AccountName, link.ErrorCode, link.ErrorMessage);
				await ReleaseClaimAsync(delivery, DiscordVerificationRules.LinkCheckFailedReason, countAttempt: false);
				return;
			}

			if (link.Data.HasValue &&
				!string.Equals(link.Data.Value.AccountName, delivery.AccountName, StringComparison.OrdinalIgnoreCase))
			{
				logger.LogWarning(
					"Discord user {DiscordUserId} is already linked to another game account; not sending account '{AccountName}' its code.",
					member.Id, delivery.AccountName);
				await ReleaseClaimAsync(delivery, DiscordVerificationRules.LinkedElsewhereReason, countAttempt: true);
				return;
			}

			try
			{
				await WaitForDmSlotAsync(stoppingToken);
			}
			catch (OperationCanceledException)
			{
				await ReleaseClaimAsync(delivery, DiscordVerificationRules.StoppedBeforeSendReason, countAttempt: false);
				throw;
			}

			await SendAsync(delivery, member);
		}

		/// <summary>
		/// Opens the DM channel and sends the code, then records the outcome.
		/// </summary>
		private async Task SendAsync(DiscordVerificationDelivery delivery, IUser member)
		{
			/* Discord.Net retries timeouts and 502s by default. A timed-out message may already have been
			 * delivered, so retrying it is exactly the second DM this service exists to prevent. Only rate
			 * limits are retried: a rate-limited request was never processed. */
			var options = new RequestOptions { RetryMode = RetryMode.RetryRatelimit };

			IDMChannel channel;
			try
			{
				channel = await member.CreateDMChannelAsync(options);
			}
			catch (Exception ex)
			{
				MarkDmAttempted();
				// No message has been attempted yet, so nothing can have reached the player.
				var failure = DiscordVerificationRules.ClassifyChannelOpenFailure(ex);
				logger.LogWarning(ex,
					"Could not open a DM channel to Discord user {DiscordUserId} for account '{AccountName}' ({Failure}).",
					member.Id, delivery.AccountName, failure);
				await ReleaseClaimAsync(
					delivery,
					failure == SendFailure.Refused
						? DiscordVerificationRules.RefusalReason(ex)
						: DiscordVerificationRules.NotSentReason,
					countAttempt: failure == SendFailure.Refused);
				return;
			}

			try
			{
				await channel.SendMessageAsync(
					DiscordVerificationRules.BuildDmText(delivery.Code),
					allowedMentions: AllowedMentions.None,
					options: options);
			}
			catch (Exception ex)
			{
				MarkDmAttempted();
				var failure = DiscordVerificationRules.ClassifySendFailure(ex);
				switch (failure)
				{
					case SendFailure.Refused:
						logger.LogWarning(ex,
							"Discord refused the verification DM to user {DiscordUserId} for account '{AccountName}'.",
							member.Id, delivery.AccountName);
						await ReleaseClaimAsync(delivery, DiscordVerificationRules.RefusalReason(ex), countAttempt: true);
						break;

					case SendFailure.NotSent:
						logger.LogWarning(ex,
							"Discord did not accept the verification DM to user {DiscordUserId} for account '{AccountName}'; releasing it to retry.",
							member.Id, delivery.AccountName);
						await ReleaseClaimAsync(delivery, DiscordVerificationRules.NotSentReason, countAttempt: false);
						break;

					default:
						logger.LogError(ex,
							"The verification DM to Discord user {DiscordUserId} for account '{AccountName}' may or may not have been delivered. " +
							"It is left CLAIMED and will not be retried automatically: staff must check with the player and resolve the delivery by hand.",
							member.Id, delivery.AccountName);
						break;
				}
				return;
			}

			MarkDmAttempted();

			var delivered = await accountService.MarkDeliveredAsync(delivery.AccountName, unchecked((long)member.Id), CancellationToken.None);
			if (!delivered.IsSuccess)
			{
				logger.LogError(
					"The verification DM was delivered to Discord user {DiscordUserId} for account '{AccountName}', but recording it failed ({ErrorCode}: {ErrorMessage}). " +
					"It stays claimed and will not be sent again; staff should mark it delivered.",
					member.Id, delivery.AccountName, delivered.ErrorCode, delivered.ErrorMessage);
				return;
			}

			logger.LogInformation(
				"Discord verification code delivered for account '{AccountName}' to Discord user {DiscordUserId}.",
				delivery.AccountName, member.Id);
		}

		/// <summary>The one REST member search a delivery may use, on its first try.</summary>
		private async Task<MemberMatch<IUser>> SearchMemberAsync(SocketGuild guild, string username, CancellationToken stoppingToken)
		{
			if (!DiscordVerificationRules.TryParseStoredUsername(username, out string name, out _))
			{
				return MemberMatch<IUser>.NotFound;
			}

			try
			{
				var found = await guild.SearchUsersAsync(name, DiscordVerificationRules.SearchLimit, new RequestOptions { CancelToken = stoppingToken });
				return DiscordVerificationRules.FindMember<IUser>(found, username, u => u.Id, u => u.Username, u => u.Discriminator, u => u.IsBot);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Discord member search for a verification delivery failed; treating the member as not yet joined.");
				return MemberMatch<IUser>.NotFound;
			}
		}

		/// <summary>Waits until the global minimum gap since the last DM attempt has passed.</summary>
		private async Task WaitForDmSlotAsync(CancellationToken stoppingToken)
		{
			long last = Interlocked.Read(ref lastDmTimestamp);
			if (last == 0)
			{
				return;
			}

			TimeSpan remaining = minDmInterval - Stopwatch.GetElapsedTime(last);
			if (remaining > TimeSpan.Zero)
			{
				await Task.Delay(remaining, stoppingToken);
			}
		}

		/// <summary>Starts the pacing interval. Called for every attempt, successful or not.</summary>
		private void MarkDmAttempted()
		{
			Interlocked.Exchange(ref lastDmTimestamp, Stopwatch.GetTimestamp());
		}

		/// <summary>
		/// Records why an unclaimed delivery is waiting, without counting an attempt, and only when the reason
		/// changed — a sweep must not rewrite the same row every few minutes.
		/// </summary>
		private async Task RecordReasonAsync(DiscordVerificationDelivery delivery, string reason)
		{
			if (!DiscordVerificationRules.ShouldRecordReason(delivery.LastError, reason))
			{
				return;
			}

			var result = await accountService.ReleaseDeliveryAsync(delivery.AccountName, reason, countAttempt: false, CancellationToken.None);
			if (!result.IsSuccess)
			{
				logger.LogWarning(
					"Could not record the Discord delivery status for account '{AccountName}' ({ErrorCode}: {ErrorMessage}).",
					delivery.AccountName, result.ErrorCode, result.ErrorMessage);
			}
		}

		/// <summary>Gives a claimed delivery back unsent. A failure here leaves it claimed, which is logged for staff.</summary>
		private async Task ReleaseClaimAsync(DiscordVerificationDelivery delivery, string reason, bool countAttempt)
		{
			var result = await accountService.ReleaseDeliveryAsync(delivery.AccountName, reason, countAttempt, CancellationToken.None);
			if (!result.IsSuccess)
			{
				logger.LogError(
					"Could not release the unsent Discord delivery for account '{AccountName}' ({ErrorCode}: {ErrorMessage}). " +
					"No DM was sent, but it stays claimed until staff release it.",
					delivery.AccountName, result.ErrorCode, result.ErrorMessage);
			}
		}

		#endregion
	}

	/// <summary>How a username resolved against a set of guild members.</summary>
	internal enum MemberMatchStatus
	{
		/// <summary>No member has the username.</summary>
		NotFound,
		/// <summary>Exactly one member has it.</summary>
		Found,
		/// <summary>More than one member has it; sending to either could hand the code to a stranger.</summary>
		Ambiguous,
	}

	/// <summary>The result of resolving a username against guild members.</summary>
	internal readonly struct MemberMatch<T> where T : class
	{
		/// <summary>No match.</summary>
		public static MemberMatch<T> NotFound => new MemberMatch<T>(MemberMatchStatus.NotFound, null);

		/// <summary>How it resolved.</summary>
		public MemberMatchStatus Status { get; }

		/// <summary>The member when <see cref="Status"/> is <see cref="MemberMatchStatus.Found"/>.</summary>
		public T? Member { get; }

		/// <summary>Creates a result.</summary>
		public MemberMatch(MemberMatchStatus status, T? member)
		{
			Status = status;
			Member = member;
		}
	}

	/// <summary>What a failed Discord call means for a delivery.</summary>
	internal enum SendFailure
	{
		/// <summary>Discord definitely refused it (4xx). Release, and count an attempt.</summary>
		Refused,
		/// <summary>Discord definitely did not deliver it, through no fault of the member (a rate limit). Release, uncounted.</summary>
		NotSent,
		/// <summary>It may have been delivered (timeout, 5xx, network). Leave it claimed.</summary>
		Ambiguous,
	}

	/// <summary>
	/// The pure rules behind <see cref="DiscordVerificationService"/>: username keys, member matching, failure
	/// classification and the message text. Free of Discord connections and the database so they can be tested.
	/// </summary>
	internal static class DiscordVerificationRules
	{
		/// <summary>Default minutes between safety sweeps.</summary>
		public const int DefaultSweepMinutes = 5;
		/// <summary>Shortest sweep interval allowed.</summary>
		public const int MinSweepMinutes = 1;
		/// <summary>Longest sweep interval allowed.</summary>
		public const int MaxSweepMinutes = 60;
		/// <summary>Default deliveries read per full pass.</summary>
		public const int DefaultBatchSize = 25;
		/// <summary>Smallest batch allowed.</summary>
		public const int MinBatchSize = 1;
		/// <summary>Largest batch allowed; the database caps a read at 100 regardless.</summary>
		public const int MaxBatchSize = 100;
		/// <summary>Default minimum milliseconds between any two DMs.</summary>
		public const int DefaultMinDmIntervalMs = 1500;
		/// <summary>Lowest pacing allowed: zero would switch the spam guard off.</summary>
		public const int MinDmIntervalFloorMs = 250;
		/// <summary>Highest pacing allowed.</summary>
		public const int MaxDmIntervalMs = 60000;
		/// <summary>Most members one REST search returns.</summary>
		public const int SearchLimit = 25;
		/// <summary>Longest wait between listener reconnects.</summary>
		public static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(60);

		/// <summary>Recorded when the Discord user is linked to a different account.</summary>
		public const string LinkedElsewhereReason = "That Discord account is already linked to another game account.";
		/// <summary>Recorded when Discord refused the DM because the member does not accept DMs from the bot.</summary>
		public const string DmsClosedReason = "Discord refused the message: the member's DMs are closed.";
		/// <summary>Recorded when Discord definitely did not take the message, such as a rate limit.</summary>
		public const string NotSentReason = "Discord did not accept the message; it will be retried.";
		/// <summary>Recorded when the link check could not be read.</summary>
		public const string LinkCheckFailedReason = "Could not check the Discord account's link; it will be retried.";
		/// <summary>Recorded when the bot shut down between claiming and sending.</summary>
		public const string StoppedBeforeSendReason = "The bot stopped before sending; it will be retried.";

		/// <summary>Recorded while the player has not joined the Discord server.</summary>
		public static string WaitingToJoinReason(string discordUsername) =>
			$"Waiting for {discordUsername} to join the Discord server.";

		/// <summary>Recorded when a username matches more than one member.</summary>
		public static string AmbiguousMemberReason(string discordUsername) =>
			$"More than one member of the Discord server matches {discordUsername}.";

		/// <summary>Reads an integer setting, falling back to a default and clamping to a range.</summary>
		public static int ReadClamped(string? raw, int defaultValue, int min, int max)
		{
			int value = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : defaultValue;
			return Math.Clamp(value, min, max);
		}

		/// <summary>
		/// A discriminator in its four-digit form, or null when the user has none. Discord.Net reports
		/// <c>"0000"</c> for a migrated username and the API reports <c>"0"</c>; neither is a discriminator.
		/// </summary>
		public static string? NormalizeDiscriminator(string? discriminator)
		{
			if (string.IsNullOrWhiteSpace(discriminator))
			{
				return null;
			}

			string trimmed = discriminator.Trim();
			if (trimmed.Length > 4)
			{
				return null;
			}

			bool anyNonZero = false;
			foreach (char c in trimmed)
			{
				if (c < '0' || c > '9')
				{
					return null;
				}
				anyNonZero |= c != '0';
			}

			return anyNonZero ? trimmed.PadLeft(4, '0') : null;
		}

		/// <summary>
		/// The stored-username keys a joining member could have been registered under: the bare username, and
		/// <c>username#1234</c> when the member still has a real discriminator. Lowercase.
		/// </summary>
		public static IReadOnlyList<string> UsernameKeysForMember(string? username, string? discriminator)
		{
			var keys = new List<string>(2);
			string bare = (username ?? string.Empty).Trim().ToLowerInvariant();
			if (bare.Length == 0)
			{
				return keys;
			}

			keys.Add(bare);
			string? tag = NormalizeDiscriminator(discriminator);
			if (tag != null)
			{
				keys.Add($"{bare}#{tag}");
			}
			return keys;
		}

		/// <summary>
		/// The username to record for a Discord user: <c>name#1234</c> for a real discriminator, otherwise the
		/// bare name. The database normalises and lowercases it.
		/// </summary>
		public static string StoredUsernameFor(string username, string? discriminator)
		{
			string? tag = NormalizeDiscriminator(discriminator);
			return tag != null ? $"{username}#{tag}" : username;
		}

		/// <summary>Splits a stored username into its name and its discriminator, if it has one.</summary>
		public static bool TryParseStoredUsername(string? stored, out string name, out string? discriminator)
		{
			name = string.Empty;
			discriminator = null;
			if (string.IsNullOrWhiteSpace(stored))
			{
				return false;
			}

			int hash = stored.LastIndexOf('#');
			if (hash < 0)
			{
				name = stored.Trim();
				return name.Length > 0;
			}

			name = stored.Substring(0, hash).Trim();
			string tag = stored.Substring(hash + 1);
			if (name.Length == 0 || tag.Length != 4 || !tag.All(c => c >= '0' && c <= '9'))
			{
				name = string.Empty;
				return false;
			}

			discriminator = tag;
			return true;
		}

		/// <summary>
		/// Whether a member is the one a stored username names.
		/// </summary>
		/// <remarks>
		/// A bare name matches a member's username, ignoring case, only when that member has no real
		/// discriminator: a bare name is unique only in Discord's new username system, and a legacy
		/// <c>FishFan#1234</c> is not the person who registered <c>fishfan</c>. A tagged name matches the
		/// username ignoring case AND the exact discriminator.
		/// </remarks>
		public static bool MatchesMember(string storedUsername, string? memberUsername, string? memberDiscriminator)
		{
			if (memberUsername == null || !TryParseStoredUsername(storedUsername, out string name, out string? tag))
			{
				return false;
			}

			if (!string.Equals(name, memberUsername.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			return string.Equals(tag, NormalizeDiscriminator(memberDiscriminator), StringComparison.Ordinal);
		}

		/// <summary>
		/// Resolves a stored username against members, ignoring bots and duplicate entries of one user.
		/// </summary>
		public static MemberMatch<T> FindMember<T>(
			IEnumerable<T> members,
			string storedUsername,
			Func<T, ulong> id,
			Func<T, string?> username,
			Func<T, string?> discriminator,
			Func<T, bool> isBot) where T : class
		{
			T? found = null;
			ulong foundId = 0;
			foreach (T member in members)
			{
				if (member == null || isBot(member) || !MatchesMember(storedUsername, username(member), discriminator(member)))
				{
					continue;
				}

				ulong memberId = id(member);
				if (found == null)
				{
					found = member;
					foundId = memberId;
				}
				else if (memberId != foundId)
				{
					return new MemberMatch<T>(MemberMatchStatus.Ambiguous, null);
				}
			}

			return found == null
				? MemberMatch<T>.NotFound
				: new MemberMatch<T>(MemberMatchStatus.Found, found);
		}

		/// <summary>Whether this is a delivery's first try: the only one that may search Discord's REST API.</summary>
		public static bool IsFirstTry(DiscordVerificationDelivery delivery) =>
			delivery.Attempts == 0 && delivery.LastError == null;

		/// <summary>Whether a reason differs from the one already recorded.</summary>
		public static bool ShouldRecordReason(string? lastError, string reason) =>
			!string.Equals(lastError, reason, StringComparison.Ordinal);

		/// <summary>What an HTTP status from Discord means for a message that was sent.</summary>
		public static SendFailure ClassifyHttpStatus(int httpStatus)
		{
			if (httpStatus == 429)
			{
				return SendFailure.NotSent;
			}
			if (httpStatus == 408)
			{
				return SendFailure.Ambiguous;
			}
			if (httpStatus >= 400 && httpStatus < 500)
			{
				return SendFailure.Refused;
			}
			return SendFailure.Ambiguous;
		}

		/// <summary>
		/// What a failure to send the message means. Only a Discord HTTP answer is definitive; every other
		/// failure (timeouts, including Discord.Net's rate-limit timeout, cancellation, network errors) may
		/// have happened after the message went out.
		/// </summary>
		public static SendFailure ClassifySendFailure(Exception exception) =>
			exception is HttpException http
				? ClassifyHttpStatus((int)http.HttpCode)
				: SendFailure.Ambiguous;

		/// <summary>
		/// What a failure to open the DM channel means. No message has been attempted, so nothing is ambiguous:
		/// a 4xx is a refusal, and anything else simply did not send.
		/// </summary>
		public static SendFailure ClassifyChannelOpenFailure(Exception exception) =>
			exception is HttpException http && ClassifyHttpStatus((int)http.HttpCode) == SendFailure.Refused
				? SendFailure.Refused
				: SendFailure.NotSent;

		/// <summary>The reason recorded for a refusal, naming the cause where Discord gave one.</summary>
		public static string RefusalReason(Exception exception)
		{
			if (exception is not HttpException http)
			{
				return "Discord refused the message.";
			}

			if (http.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
			{
				return DmsClosedReason;
			}

			return http.DiscordCode.HasValue
				? $"Discord refused the message (HTTP {(int)http.HttpCode}, Discord error {(int)http.DiscordCode.Value})."
				: $"Discord refused the message (HTTP {(int)http.HttpCode}).";
		}

		/// <summary>Capped exponential backoff for the listener: 1 s, 2 s, 4 s … up to a minute.</summary>
		public static TimeSpan ReconnectDelay(int consecutiveFailures)
		{
			int exponent = Math.Clamp(consecutiveFailures - 1, 0, 6);
			TimeSpan delay = TimeSpan.FromSeconds(1 << exponent);
			return delay > MaxReconnectDelay ? MaxReconnectDelay : delay;
		}

		/// <summary>
		/// The verification DM. Names no account, because the message is the code's only carrier and a
		/// forwarded screenshot should not also say whose account it opens.
		/// </summary>
		public static string BuildDmText(int code) =>
			"**FishMMO account verification**\n" +
			$"Your verification code is **{code.ToString("D6", CultureInfo.InvariantCulture)}**.\n" +
			"Enter it when the FishMMO game or Control Panel asks for your verification code. Any one of the codes we sent you verifies your account.\n" +
			"This message is sent only once — keep it until your account is verified.\n" +
			"If you did not create a FishMMO account, you can ignore this message.";
	}
}
