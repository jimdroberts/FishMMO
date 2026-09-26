using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Rolls the servers' bandwidth minutes into hours and enforces the retention of both tables,
	/// on a timer, so the Bandwidth page's 30-day figures and a year of history exist whether or
	/// not anybody has the page open.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why here.</b> The game servers write their own minutes and nothing else; a rollup there
	/// would run once per server process and compete with the tick. Nothing in the repository
	/// prunes on a schedule, and PostgreSQL has no scheduler without an extension, so the
	/// panel — the one long-running host that reads these tables — owns it, as it owns the
	/// maintenance advance (<see cref="MaintenanceAdvanceService"/>, which this is modelled on).
	/// </para>
	/// <para>
	/// <b>Safe to run twice, and safe in two panels at once.</b> Every hour row is recomputed from
	/// its minutes and SET, never added to, so repeating a pass changes nothing. Each pass takes
	/// a transaction-level advisory lock first and the second panel skips; the lock is there to
	/// keep two panels' multi-row upserts from deadlocking each other and their retention
	/// batches from fighting over the same rows, not for correctness of the numbers. See
	/// <see cref="IServerBandwidthReportService"/>.
	/// </para>
	/// <para>
	/// <b>The first pass after this panel starts is a catch-up</b>: it rolls every hour that still
	/// has minutes, covering however long no panel was running. Later passes roll the last
	/// <see cref="ServerBandwidthMath.RollupWindowHours"/> hours, which reaches past the latest a
	/// server's retried minute can arrive. Retention folds each hour into its row before deleting
	/// its minutes, so a pass that deletes can never lose traffic the rollup had not reached.
	/// </para>
	/// <para>
	/// A failed pass is logged and the timer continues, for the reason the maintenance service
	/// gives: a transient database fault must not become a panel outage.
	/// </para>
	/// </remarks>
	public sealed class ServerBandwidthRollupService : BackgroundService
	{
		/// <summary>How often a pass runs.</summary>
		/// <remarks>
		/// The page reads minutes for the last few hours (<see cref="ServerBandwidthMath.StitchHours"/>),
		/// so nothing it shows waits on this; five minutes keeps the hour rows close to current
		/// and makes a pass cheap — one windowed upsert over a few hundred rows per server.
		/// </remarks>
		private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

		/// <summary>
		/// A short wait before the catch-up, so it does not compete with the panel's own startup
		/// reads (the TOTP key, the audit coverage check).
		/// </summary>
		private static readonly TimeSpan FirstPassDelay = TimeSpan.FromSeconds(20);

		/// <summary>
		/// Whole hours of minutes one pass may prune. A day per pass at five-minute passes clears
		/// any backlog — a panel that was down for weeks — within the hour, while keeping each pass
		/// short. Each hour is its own transaction.
		/// </summary>
		public const int MaxMinuteHoursPerPass = 24;

		/// <summary>Batches of expired hour rows one pass may delete.</summary>
		public const int MaxHourBatchesPerPass = 10;

		private readonly IServiceScopeFactory scopeFactory;
		private readonly ILogger<ServerBandwidthRollupService> log;

		/// <summary>Whether this panel has completed its catch-up pass.</summary>
		private bool caughtUp;

		/// <summary>Creates the service.</summary>
		/// <param name="scopeFactory">Factory for the scope each pass resolves its service from.</param>
		/// <param name="log">The log to report to.</param>
		public ServerBandwidthRollupService(IServiceScopeFactory scopeFactory, ILogger<ServerBandwidthRollupService> log)
		{
			this.scopeFactory = scopeFactory;
			this.log = log;
		}

		/// <inheritdoc />
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			try
			{
				await Task.Delay(FirstPassDelay, stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			await RunPassAsync(stoppingToken).ConfigureAwait(false);

			using var timer = new PeriodicTimer(Interval);
			while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false))
			{
				await RunPassAsync(stoppingToken).ConfigureAwait(false);
			}
		}

		/// <summary>One rollup, then retention. Never throws, except on shutdown.</summary>
		private async Task RunPassAsync(CancellationToken stoppingToken)
		{
			try
			{
				// A new scope per pass: the service is scoped, and holding one would pin a context for days.
				using var scope = scopeFactory.CreateScope();
				var bandwidth = scope.ServiceProvider.GetRequiredService<IServerBandwidthReportService>();

				bool catchUp = !caughtUp;
				var rolled = await bandwidth.RollupAsync(catchUp, stoppingToken).ConfigureAwait(false);
				if (!rolled.IsSuccess)
				{
					log.LogWarning("Bandwidth rollup failed: [{Code}] {Message}", rolled.ErrorCode, rolled.ErrorMessage);
					return;
				}
				if (rolled.Data.Skipped)
				{
					// Another panel holds the lock and is doing this pass; its result is the same as ours would be.
					log.LogDebug("Bandwidth rollup skipped: another Control Panel holds the rollup lock.");
					return;
				}
				if (catchUp)
				{
					caughtUp = true;
					log.LogInformation("Bandwidth rollup caught up: {Count} hour row(s) written.", rolled.Data.HoursWritten);
				}

				var pruned = await bandwidth.PruneAsync(MaxMinuteHoursPerPass, MaxHourBatchesPerPass, stoppingToken).ConfigureAwait(false);
				if (!pruned.IsSuccess)
				{
					log.LogWarning("Bandwidth retention failed: [{Code}] {Message}", pruned.ErrorCode, pruned.ErrorMessage);
					return;
				}

				// Only when something went. A line every five minutes saying nothing happened buries the ones that matter.
				var p = pruned.Data;
				if (p.MinuteRowsDeleted > 0 || p.HourRowsDeleted > 0)
				{
					log.LogInformation(
						"Bandwidth retention folded and deleted {Hours} hour(s) of minutes ({Minutes} row(s)) and {HourRows} expired hour row(s){More}.",
						p.MinuteHoursPruned, p.MinuteRowsDeleted, p.HourRowsDeleted,
						p.MoreRemaining ? "; more remain for the next pass" : string.Empty);
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				// Shutting down.
			}
			catch (Exception ex)
			{
				// Never let a pass take the host down with it.
				log.LogError(ex, "Bandwidth rollup threw.");
			}
		}

		/// <summary>Waits for the next tick, treating shutdown as an ordinary end rather than a fault.</summary>
		private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
		{
			try
			{
				return await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}
	}
}
