using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

[ApiController]
[Route("[controller]")]
public class PatchServerController : ControllerBase
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly ILogger<PatchServerController> logger;
	private readonly IMemoryCache memoryCache;

	public PatchServerController(NpgsqlDbContextFactory dbContextFactory, ILogger<PatchServerController> logger, IMemoryCache memoryCache)
	{
		this.dbContextFactory = dbContextFactory;
		this.logger = logger;
		this.memoryCache = memoryCache;
	}

	[HttpGet]
	public async Task<IActionResult> GetPatchServers()
	{
		const string cacheKey = "patch_servers";

		if (!memoryCache.TryGetValue(cacheKey, out List<PatchServerEntity> patchServers))
		{
			using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return Unauthorized();
			}

			var cutoff = DateTime.UtcNow.AddMinutes(-5);
			patchServers = await dbContext.PatchServers
				.Where(p => p.LastPulse >= cutoff)
				.ToListAsync();

			var cacheOptions = new MemoryCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(300)
			};

			memoryCache.Set(cacheKey, patchServers, cacheOptions);
			logger.LogInformation("Cache miss for patch servers. Pulled from DB.");
		}
		else
		{
			logger.LogInformation("Cache hit for patch servers.");
		}

		return Ok(patchServers.Select(p => new
		{
			p.Address,
			p.Port,
			LastPulse = p.LastPulse.ToString("yyyy-MM-dd HH:mm:ss")
		}));
	}
}