using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

[ApiController]
[Route("[controller]")]
public class LoginServerController : ControllerBase
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly ILogger<LoginServerController> logger;
	private static List<LoginServerEntity> loginCache;
	private static DateTime cacheTimestamp;
	private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(300);

	public LoginServerController(NpgsqlDbContextFactory dbContextFactory, ILogger<LoginServerController> logger)
	{
		this.dbContextFactory = dbContextFactory;
		this.logger = logger;
	}

	[HttpGet]
	public async Task<IActionResult> GetLoginServers()
	{
		if (loginCache != null && DateTime.UtcNow - cacheTimestamp < CacheDuration)
		{
			logger.LogInformation("Cache hit for login servers.");
			return Ok(loginCache.Select(l => new
			{
				l.Address,
				l.Port
			}));
		}

		using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();
		if (dbContext == null)
		{
			return Unauthorized();
		}

		var loginServers = await dbContext.LoginServers.ToListAsync();
		loginCache = loginServers;
		cacheTimestamp = DateTime.UtcNow;

		logger.LogInformation("Cache miss for login servers. Pulled from DB.");
		return Ok(loginCache.Select(l => new
		{
			l.Address,
			l.Port
		}));
	}
}