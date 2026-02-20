using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Logging;


/// <summary>
/// Controller that exposes endpoints for retrieving available login servers.
/// Uses an <see cref="NpgsqlDbContextFactory"/> to access the database and an
/// <see cref="IMemoryCache"/> to cache results for improved performance.
/// </summary>
[ApiController]
[Route("[controller]")]
public class LoginServerController : ControllerBase
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly IMemoryCache memoryCache;

	/// <summary>
	/// Initializes a new instance of the <see cref="LoginServerController"/> class.
	/// </summary>
	/// <param name="dbContextFactory">Factory used to create instances of <see cref="NpgsqlDbContext"/>.</param>
	/// <param name="memoryCache">In-memory cache used to store and retrieve cached login server lists.</param>
	public LoginServerController(NpgsqlDbContextFactory dbContextFactory, IMemoryCache memoryCache)
	{
		this.dbContextFactory = dbContextFactory;
		this.memoryCache = memoryCache;
	}
	
	/// <summary>
	/// Retrieves a list of available login servers (address and port).
	/// Results are cached in memory for a short duration to reduce database load.
	/// </summary>
	/// <returns>
	/// An <see cref="IActionResult"/> containing HTTP 200 with the list of servers
	/// (address and port) on success, HTTP 404 if no servers are available, or
	/// HTTP 401 if the database context could not be created.
	/// </returns>
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