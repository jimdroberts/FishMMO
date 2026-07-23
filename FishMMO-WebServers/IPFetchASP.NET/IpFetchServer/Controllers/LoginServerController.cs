using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Logging;


/// <summary>
/// Controller that exposes endpoints for retrieving available login servers.
/// Uses an <see cref="NpgsqlDbContextFactory"/> to access the database and an
/// <see cref="IMemoryCache"/> to cache results for improved performance.
/// Returns only ports; the client always connects via GameHost.
/// </summary>
[ApiController]
[Route("[controller]")]
public class LoginServerController : ControllerBase
{
	private readonly NpgsqlDbContextFactory dbContextFactory;
	private readonly IMemoryCache memoryCache;
	private readonly IConfiguration configuration;

	/// <summary>
	/// Per-process gate that serialises the cache-miss DB load. Without it, a burst of
	/// concurrent requests on a cold cache would each spawn a DbContext + query (cache
	/// stampede), which is exactly the failure mode the cache exists to prevent.
	/// </summary>
	private static readonly SemaphoreSlim loginServersLoadGate = new SemaphoreSlim(1, 1);

	/// <summary>
	/// Initializes a new instance of the <see cref="LoginServerController"/> class.
	/// </summary>
	/// <param name="dbContextFactory">Factory used to create instances of <see cref="NpgsqlDbContext"/>.</param>
	/// <param name="memoryCache">In-memory cache used to store and retrieve cached login server lists.</param>
	public LoginServerController(NpgsqlDbContextFactory dbContextFactory, IMemoryCache memoryCache, IConfiguration configuration)
	{
		this.dbContextFactory = dbContextFactory;
		this.memoryCache = memoryCache;
		this.configuration = configuration;
	}

	/// <summary>
	/// Retrieves a list of available login server ports.
	/// Results are cached in memory for a short duration to reduce database load.
	/// </summary>
	/// <returns>
	/// An <see cref="IActionResult"/> containing HTTP 200 with the list of ports
	/// on success, HTTP 404 if no servers are available, or
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

		if (!memoryCache.TryGetValue(cacheKey, out int[] loginServerPorts))
		{
			// Single-flight DB load to avoid cache stampede. The first thread that
			// enters the gate populates the cache; later threads find the cached value on
			// re-check and skip the DB hit entirely.
			await loginServersLoadGate.WaitAsync(HttpContext.RequestAborted);
			try
			{
				if (!memoryCache.TryGetValue(cacheKey, out loginServerPorts))
				{
					using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();
					if (dbContext == null)
					{
						await Log.Error("LoginServerController", "Failed to create DbContext for LoginServerController.");
						return StatusCode(StatusCodes.Status503ServiceUnavailable, "Login server directory temporarily unavailable.");
					}

					// Project to an immutable array before caching.
					loginServerPorts = await dbContext.LoginServers
						.AsNoTracking()
						.Select(l => l.Port)
						.ToArrayAsync(HttpContext.RequestAborted);

					// Never cache an empty list — after a LoginServer restart it takes
					// 60-90s for the server to re-register. A cached empty result would
					// make clients receive 404 for the full cache TTL window.
					if (loginServerPorts.Length == 0)
					{
						await Log.Warning("LoginServerController", "DB returned zero login servers — serving live, not cached.");
					}
					else
					{
						var cacheEntryOptions = new MemoryCacheEntryOptions
						{
							AbsoluteExpirationRelativeToNow = CacheTtl(),
						};
						memoryCache.Set(cacheKey, loginServerPorts, cacheEntryOptions);
					}
					await Log.Debug("LoginServerController", $"Cache miss. Loaded {loginServerPorts.Length} login server(s) from DB.");
				}
			}
			finally
			{
				loginServersLoadGate.Release();
			}
		}
		else
		{
			// Cache hit is a routine condition; downgrade from Info to Debug to
			// keep request-rate logs from drowning operationally important events.
			await Log.Debug("LoginServerController", "Cache hit for login servers.");
		}

		if (loginServerPorts == null || loginServerPorts.Length == 0)
		{
			await Log.Error("LoginServerController", "No login servers available.");
			return NotFound("No login servers available.");
		}

		// Generate a stateless HMAC-signed connection token for real-IP recovery.
		// The real client IP is visible here (via X-Forwarded-For from NGINX)
		// but lost at the game server (L4 UDP proxy). The token bridges this gap:
		// the client echoes it in the first ClientHandshake, and the Login Server
		// verifies the HMAC to recover the real IP — no database round-trip needed.
		//
		// Token format: base64url(payload).base64url(hmac)
		//   payload = realIp + '|' + expiryUnixSeconds
		//   hmac    = HMAC-SHA256(sharedKey, payload)
		var hmacKey = GetConnectionTokenHmacKey();
		if (hmacKey == null)
		{
			await Log.Error("LoginServerController", "ConnectionToken HMAC key not configured.");
			return StatusCode(StatusCodes.Status500InternalServerError, "Server configuration error.");
		}

		var realIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		long expiryUnix = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds();
		var payload = System.Text.Encoding.UTF8.GetBytes($"{realIp}|{expiryUnix}");

		byte[] signature;
		using (var hmac = new System.Security.Cryptography.HMACSHA256(hmacKey))
		{
			signature = hmac.ComputeHash(payload);
		}
		var payloadB64 = Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var sigB64 = Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var token = $"{payloadB64}.{sigB64}";

		await Log.Debug("LoginServerController",
			$"Issued stateless connection token for IP {realIp} (expires in 60s)");

		// Wrap in a "Ports" envelope so Unity's JsonUtility can deserialize
		// the response without manual string rewriting on the client.
		return Ok(new { Ports = loginServerPorts, ConnectionToken = token });
	}

	/// <summary>
	/// Resolves the shared HMAC key for connection token signing.
	/// Order: ConnectionToken:HmacKey in appsettings.json, then
	/// FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64 environment variable.
	/// Returns null if neither is configured.
	/// </summary>
	private byte[]? GetConnectionTokenHmacKey()
	{
		var b64 = configuration["ConnectionToken:HmacKey"];
		if (string.IsNullOrWhiteSpace(b64))
			b64 = System.Environment.GetEnvironmentVariable("FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64");
		if (string.IsNullOrWhiteSpace(b64))
			return null;
		try
		{
			return Convert.FromBase64String(b64.Trim());
		}
		catch (FormatException)
		{
			return null;
		}
	}
}