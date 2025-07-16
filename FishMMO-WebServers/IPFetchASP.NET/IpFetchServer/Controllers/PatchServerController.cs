using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishMMO.Logging;

[ApiController]
[Route("[controller]")]
public class PatchServerController : ControllerBase
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly IMemoryCache memoryCache;

	public PatchServerController(NpgsqlDbContextFactory dbContextFactory, IMemoryCache memoryCache)
	{
		this.dbContextFactory = dbContextFactory;
		this.memoryCache = memoryCache;
	}

	[HttpGet]
	public async Task<IActionResult> GetPatchServers()
	{
		const string cacheKey = "patch_servers";

		List<PatchServerEntity> patchServers;

		if (!memoryCache.TryGetValue(cacheKey, out patchServers))
		{
			using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				await Log.Error("PatchServerController", "Failed to create DbContext for PatchServerController.");
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
			await Log.Info("PatchServerController", "Cache miss for patch servers. Pulled from DB.");
		}
		else
		{
			await Log.Info("PatchServerController", "Cache hit for patch servers.");
		}

		if (patchServers == null || !patchServers.Any()) // Handle case where dbContext was null or no servers found
		{
			await Log.Warning("PatchServerController", "No patch servers available.");
			return NotFound("No patch servers available.");
		}

		return Ok(patchServers.Select(p => new
		{
			p.Address,
			p.Port,
			LastPulse = p.LastPulse.ToString("yyyy-MM-dd HH:mm:ss")
		}));
	}
}