using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using System.Net.Http; // Added for HttpClient
using FishMMO.Logging;

public class PatchServerHeartbeatService : BackgroundService
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly IConfiguration config;
	private readonly IHttpClientFactory httpClientFactory;

	public PatchServerHeartbeatService(NpgsqlDbContextFactory dbContextFactory, IConfiguration config, IHttpClientFactory httpClientFactory)
	{
		this.dbContextFactory = dbContextFactory;
		this.config = config;
		this.httpClientFactory = httpClientFactory;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var ip = await GetExternalIpAsync();
		var port = ushort.TryParse(config["WebServer:HttpPort"], out var parsedPort) ? parsedPort : (ushort)8090;

		var intervalSeconds = config.GetValue<int>("HeartbeatService:IntervalSeconds", 60);
		var heartbeatInterval = TimeSpan.FromSeconds(intervalSeconds);

		await Log.Info("PatchServerHeartbeatService", $"PatchServerHeartbeatService started. IP: {ip}, Port: {port}");

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();

				var now = DateTime.UtcNow;

				var patchServer = await dbContext.PatchServers
					.Where(ps => ps.Address == ip && ps.Port == port)
					.FirstOrDefaultAsync();

				if (patchServer == null)
				{
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
					patchServer.LastPulse = now;
					dbContext.PatchServers.Update(patchServer);
				}

				await dbContext.SaveChangesAsync();

				await Log.Info("PatchServerHeartbeatService", $"Updated patch server pulse at {now}");
			}
			catch (Exception ex)
			{
				await Log.Error("PatchServerHeartbeatService", "Failed to update patch server heartbeat", ex);
			}

			await Task.Delay(heartbeatInterval, stoppingToken);
		}

		await Log.Info("PatchServerHeartbeatService", "PatchServerHeartbeatService stopping...");
	}

	private async Task<string> GetExternalIpAsync()
	{
		try
		{
			using var client = httpClientFactory.CreateClient(); // Use IHttpClientFactory
			var ip = (await client.GetStringAsync("https://checkip.amazonaws.com/")).Trim();
			await Log.Info("PatchServerHeartbeatService", $"External IP resolved: {ip}");
			return ip;
		}
		catch (Exception ex)
		{
			await Log.Error("PatchServerHeartbeatService", "Failed to retrieve external IP", ex);
			return "127.0.0.1"; // fallback
		}
	}
}