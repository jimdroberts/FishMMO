using FishMMO.Database.Npgsql;
using FishMMO.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Background service that periodically deletes expired connection tokens
	/// from the database. Runs every 60 seconds. Tokens have a 60-second TTL
	/// so this keeps the table from growing unbounded.
	/// </summary>
	public class TokenCleanupService : BackgroundService
	{
		private readonly NpgsqlDbContextFactory _dbFactory;
		private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(60);

		public TokenCleanupService(NpgsqlDbContextFactory dbFactory)
		{
			_dbFactory = dbFactory;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(CleanupInterval, stoppingToken);
					using var db = _dbFactory.CreateDbContext();
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