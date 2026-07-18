using FishMMO.Database.Npgsql;
using FishMMO.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Background service that periodically deletes expired connection tokens
	/// from the database. Runs at an interval configured via <c>TokenCleanup:IntervalSeconds</c>
	/// (defaults to 60 seconds). Tokens have a 60-second TTL so this keeps the table
	/// from growing unbounded.
	/// </summary>
	public class TokenCleanupService : BackgroundService
	{
		private readonly NpgsqlDbContextFactory dbFactory;
		private readonly TimeSpan cleanupInterval;

		public TokenCleanupService(NpgsqlDbContextFactory dbFactory, IConfiguration configuration)
		{
			this.dbFactory = dbFactory;

			// Read cleanup interval from settings, defaulting to 60 seconds.
			int intervalSeconds = configuration.GetValue<int?>("TokenCleanup:IntervalSeconds") ?? 60;
			cleanupInterval = TimeSpan.FromSeconds(Math.Max(intervalSeconds, 1));
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(cleanupInterval, stoppingToken);
					using var db = dbFactory.CreateDbContext();
					int deleted = await db.Database.ExecuteSqlRawAsync(
						"DELETE FROM connection_tokens WHERE expires_at < NOW()",
						stoppingToken);
					if (deleted > 0)
					{
						await Log.Debug("TokenCleanup", $"Cleaned up {deleted} expired connection token(s).");
					}
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					await Log.Warning("TokenCleanup", $"Cleanup error: {ex.Message}");
				}
			}
		}
	}
}