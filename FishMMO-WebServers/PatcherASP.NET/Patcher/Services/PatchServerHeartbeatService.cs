using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

public class PatchServerHeartbeatService : BackgroundService
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly ILogger<PatchServerHeartbeatService> logger;
	private readonly IConfiguration config;

	public PatchServerHeartbeatService(NpgsqlDbContextFactory dbContextFactory, ILogger<PatchServerHeartbeatService> logger, IConfiguration config)
	{
		this.dbContextFactory = dbContextFactory;
		this.logger = logger;
		this.config = config;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var ip = await GetExternalIpAsync();
		var port = ushort.TryParse(config["Server:Port"], out var parsedPort) ? parsedPort : (ushort)8000;

		logger.LogInformation("PatchServerHeartbeatService started. IP: {IP}, Port: {Port}", ip, port);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();

				var now = DateTime.UtcNow;

				// Check if the patch server already exists
				var patchServer = await dbContext.PatchServers
					.Where(ps => ps.Address == ip && ps.Port == port)
					.FirstOrDefaultAsync();

				if (patchServer == null)
				{
					// If it doesn't exist, create a new one
					patchServer = new PatchServerEntity
					{
						Address = ip,
						Port = port,
						LastPulse = now
					};
					dbContext.PatchServers.Add(patchServer);
				}
				else
				{
					// If it exists, update the LastPulse value
					patchServer.LastPulse = now;
					dbContext.PatchServers.Update(patchServer);
				}

				// Commit the changes to the database
				await dbContext.SaveChangesAsync();

				logger.LogInformation("Updated patch server pulse at {Time}", now);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to update patch server heartbeat");
			}

			await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
		}

		logger.LogInformation("PatchServerHeartbeatService stopping...");
	}

	private async Task<string> GetExternalIpAsync()
	{
		try
		{
			using var client = new HttpClient();
			var ip = (await client.GetStringAsync("https://checkip.amazonaws.com/")).Trim();
			logger.LogInformation("External IP resolved: {IP}", ip);
			return ip;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to retrieve external IP");
			return "127.0.0.1"; // fallback
		}
	}
}