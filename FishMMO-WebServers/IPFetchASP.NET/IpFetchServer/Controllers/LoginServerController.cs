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
	/// HTTP 503 if the database context could not be created.
	/// </returns>
	[HttpGet]
	public async Task<IActionResult> GetLoginServers()
	{
		const string cacheKey = "login_servers";
		// Short TTL with jitter so a server taken out of rotation falls out of the
		// cache quickly; the previous 300 s window left clients connecting to
		// drained hosts for several minutes after a deploy.
		static TimeSpan CacheTtl()
		{
			int jitterMs = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10_000);
			return TimeSpan.FromSeconds(60) + TimeSpan.FromMilliseconds(jitterMs);
		}

		if (!memoryCache.TryGetValue(cacheKey, out LoginServerAddressDto[] loginServers))
		{
			using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				await Log.Error("LoginServerController", "Failed to create DbContext for LoginServerController.");
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Login server directory temporarily unavailable.");
			}

			// Project to an immutable DTO array before caching. Caching the raw EF
			// entities by reference would let any future mutation (or change-tracker
			// reuse) leak into every subsequent cache hit.
			loginServers = await dbContext.LoginServers
				.AsNoTracking()
				.Select(l => new LoginServerAddressDto(l.Address, l.Port))
				.ToArrayAsync();

			var cacheEntryOptions = new MemoryCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = CacheTtl(),
			};

			memoryCache.Set(cacheKey, loginServers, cacheEntryOptions);
			await Log.Info("LoginServerController", $"Cache miss. Loaded {loginServers.Length} login server(s) from DB.");
		}
		else
		{
			await Log.Info("LoginServerController", "Cache hit for login servers.");
		}

		if (loginServers == null || loginServers.Length == 0)
		{
			await Log.Error("LoginServerController", "No login servers available.");
			return NotFound("No login servers available.");
		}

		// Wrap in an "Addresses" envelope so Unity's JsonUtility can deserialize
		// the response without manual string rewriting on the client.
		return Ok(new { Addresses = loginServers });
	}

	/// <summary>
	/// Immutable projection of <see cref="LoginServerEntity"/> used both as the
	/// cached representation and as the wire format returned to clients. Field
	/// names are PascalCase to match the Unity <c>ServerAddress</c> JsonUtility
	/// type exactly.
	/// </summary>
	private sealed record LoginServerAddressDto(string Address, ushort Port);
}