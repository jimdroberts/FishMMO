using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

[ApiController]
[Route("[controller]")]
public class PatchServerController : ControllerBase
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly ILogger<PatchServerController> logger;
	private static List<PatchServerEntity> patchCache;
	private static DateTime cacheTimestamp;
	private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(300);

	public PatchServerController(NpgsqlDbContextFactory dbContextFactory, ILogger<PatchServerController> logger)
	{
		this.dbContextFactory = dbContextFactory;
		this.logger = logger;
	}

	[HttpGet]
	public async Task<IActionResult> GetPatchServers()
	{
		if (patchCache != null && DateTime.UtcNow - cacheTimestamp < CacheDuration)
		{
			logger.LogInformation("Cache hit for patch servers.");
			return Ok(patchCache.Select(p => new
			{
				p.Address,
				p.Port,
				LastPulse = p.LastPulse.ToString("yyyy-MM-dd HH:mm:ss")
			}));
		}

		using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();
		if (dbContext == null)
		{
			return Unauthorized();
		}

		var cutoff = DateTime.UtcNow.AddMinutes(-5);
		var patchServers = await dbContext.PatchServers
			.Where(p => p.LastPulse >= cutoff)
			.ToListAsync();

		patchCache = patchServers;
		cacheTimestamp = DateTime.UtcNow;

		logger.LogInformation("Cache miss for patch servers. Pulled from DB.");
		return Ok(patchCache.Select(p => new
		{
			p.Address,
			p.Port,
			LastPulse = p.LastPulse.ToString("yyyy-MM-dd HH:mm:ss")
		}));
	}
}