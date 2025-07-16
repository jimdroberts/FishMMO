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
public class LoginServerController : ControllerBase
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly IMemoryCache memoryCache;

	public LoginServerController(NpgsqlDbContextFactory dbContextFactory, IMemoryCache memoryCache)
	{
		this.dbContextFactory = dbContextFactory;
		this.memoryCache = memoryCache;
	}

	[HttpGet]
	public async Task<IActionResult> GetLoginServers()
	{
		const string cacheKey = "login_servers";

		List<LoginServerEntity> loginServers;

		if (!memoryCache.TryGetValue(cacheKey, out loginServers))
		{
			using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				await Log.Error("LoginServerController", "Failed to create DbContext for LoginServerController.");
				return Unauthorized();
			}

			loginServers = await dbContext.LoginServers.ToListAsync();
			var cacheEntryOptions = new MemoryCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(300)
			};

			memoryCache.Set(cacheKey, loginServers, cacheEntryOptions);
			await Log.Info("LoginServerController", "Cache miss. Loaded login servers from DB.");
		}
		else
		{
			await Log.Info("LoginServerController", "Cache hit for login servers.");
		}

		if (loginServers == null || !loginServers.Any()) // Handle case where dbContext was null or no servers found
		{
			await Log.Error("LoginServerController", "No login servers available.");
			return NotFound("No login servers available.");
		}

		return Ok(loginServers.Select(l => new
		{
			l.Address,
			l.Port
		}));
	}
}