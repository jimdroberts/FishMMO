using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Advances maintenance windows on a timer, so their record does not depend on somebody
	/// having the page open.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Starting a window is already complete when it returns: each target's own row carries a
	/// lock and an absolute deadline, and the server counts down to it inside its own process.
	/// None of that needs this service, and nothing here is required for a shard to stop on
	/// time. What needs it is everything the panel knows <i>afterwards</i>.
	/// </para>
	/// <para>
	/// Two things go wrong without it. The smaller one is that statuses stay stale: every read
	/// advances the record, so with nobody looking, a window whose servers stopped an hour ago
	/// still reads "Draining" until the next person opens the page. The shard went down on
	/// time and only the bookkeeping is late — but an operator checking whether last night's
	/// maintenance finished is reading exactly that bookkeeping.
	/// </para>
	/// <para>
	/// The larger one is the retry. The plan is committed <i>before</i> any server is touched,
	/// so a panel that dies mid-actuation leaves a visible half-written window rather than a
	/// locked shard with no record — and <see cref="IMaintenanceService.AdvanceAsync"/> is what
	/// finishes those writes. Left to a reader, a target whose deadline was never written sits
	/// unwritten until somebody happens to look, and a server the operator believes is draining
	/// is in fact still open and still taking players.
	/// </para>
	/// <para>
	/// A failed pass is logged and the timer continues. Refusing to serve the panel because one
	/// advance could not reach the database would turn a transient fault into an outage on the
	/// one surface an operator opens during an incident.
	/// </para>
	/// </remarks>
	public sealed class MaintenanceAdvanceService : BackgroundService
	{
		/// <summary>How often a pass runs.</summary>
		/// <remarks>
		/// The work is one indexed read that finds nothing at all outside a maintenance window,
		/// which is almost always. Half a minute keeps the record close enough to current for
		/// somebody watching a drain, without polling for the sake of it.
		/// </remarks>
		private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

		private readonly IServiceScopeFactory scopeFactory;
		private readonly ILogger<MaintenanceAdvanceService> log;

		/// <summary>Creates the service.</summary>
		/// <param name="scopeFactory">Factory for the scope each pass resolves its service from.</param>
		/// <param name="log">The log to report failures to.</param>
		public MaintenanceAdvanceService(IServiceScopeFactory scopeFactory, ILogger<MaintenanceAdvanceService> log)
		{
			this.scopeFactory = scopeFactory;
			this.log = log;
		}

		/// <inheritdoc />
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			using var timer = new PeriodicTimer(Interval);

			while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false))
			{
				try
				{
					// A new scope per pass: IMaintenanceService is scoped, and holding one for
					// the lifetime of the host would pin a database context open for days.
					using var scope = scopeFactory.CreateScope();
					var maintenance = scope.ServiceProvider.GetRequiredService<IMaintenanceService>();

					var result = await maintenance.AdvanceAsync(stoppingToken).ConfigureAwait(false);
					if (!result.IsSuccess)
					{
						log.LogWarning("Maintenance advance failed: [{Code}] {Message}",
							result.ErrorCode, result.ErrorMessage);
						continue;
					}

					// Only when something moved. A line every thirty seconds saying nothing
					// happened would bury the lines that say something did.
					if (result.Data > 0)
					{
						log.LogInformation("Maintenance advance changed {Count} target(s).", result.Data);
					}
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					// Never let a pass take the host down with it.
					log.LogError(ex, "Maintenance advance threw.");
				}
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
