using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

[ApiController]
[Route("[controller]")]
public class LoginServerController : ControllerBase
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly ILogger<LoginServerController> logger;
	private readonly IMemoryCache memoryCache;

	public LoginServerController(NpgsqlDbContextFactory dbContextFactory, ILogger<LoginServerController> logger, IMemoryCache memoryCache)
	{
		this.dbContextFactory = dbContextFactory;
		this.logger = logger;
		this.memoryCache = memoryCache;
	}

	[HttpGet]
	public async Task<IActionResult> GetLoginServers()
	{
		const string cacheKey = "login_servers";

		if (!memoryCache.TryGetValue(cacheKey, out List<LoginServerEntity> loginServers))
		{
			using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return Unauthorized();
			}

			loginServers = await dbContext.LoginServers.ToListAsync();
			var cacheEntryOptions = new MemoryCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(300)
			};

			memoryCache.Set(cacheKey, loginServers, cacheEntryOptions);
			logger.LogInformation("Cache miss. Loaded login servers from DB.");
		}
		else
		{
			logger.LogInformation("Cache hit for login servers.");
		}

		return Ok(loginServers.Select(l => new
		{
			l.Address,
			l.Port
		}));
	}
}