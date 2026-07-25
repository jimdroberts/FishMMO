using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;
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
	private readonly IConnectionTokenKeyService? connectionTokenKeyService;

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
	/// <param name="connectionTokenKeyService">Service for registering connection token HMAC keys in the database.</param>
	public LoginServerController(NpgsqlDbContextFactory dbContextFactory, IMemoryCache memoryCache, IConnectionTokenKeyService? connectionTokenKeyService = null)
	{
		this.dbContextFactory = dbContextFactory;
		this.memoryCache = memoryCache;
		this.connectionTokenKeyService = connectionTokenKeyService;
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
		//   payload = [keyId ':'] realIp '|' expiryUnixSeconds
		//   hmac    = HMAC-SHA256(sharedKey, payload)
		//
		// When keyId is configured, it is included in the payload so the receiving
		// game server can select the correct verification key for multi-region
		// deployments.  Legacy deployments without a keyId are still supported.
		var hmacKey = GetConnectionTokenHmacKey();
		if (hmacKey == null)
		{
			await Log.Error("LoginServerController", "ConnectionToken HMAC key not configured.");
			return StatusCode(StatusCodes.Status500InternalServerError, "Server configuration error.");
		}
		if (hmacKey.Length < 32)
		{
			await Log.Error("LoginServerController",
				$"ConnectionToken HMAC key is too short ({hmacKey.Length} bytes). " +
				"Minimum 32 bytes (after Base64 decode) is required for HMAC-SHA256.");
			return StatusCode(StatusCodes.Status500InternalServerError, "Server configuration error.");
		}

		var keyId = GetConnectionTokenKeyId();
		var realIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		long expiryUnix = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds();
		var payloadStr = string.IsNullOrEmpty(keyId)
			? $"{realIp}|{expiryUnix}"
			: $"{keyId}:{realIp}|{expiryUnix}";
		var payload = System.Text.Encoding.UTF8.GetBytes(payloadStr);

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

		// Register the signing key in the database so game servers can discover it
		// for verification. The database is the sole source for keys.
		if (connectionTokenKeyService != null)
		{
			_ = RegisterConnectionTokenKeyAsync(keyId ?? "shared", hmacKey, HttpContext.RequestAborted);
		}

		// Wrap in a "Ports" envelope so Unity's JsonUtility can deserialize
		// the response without manual string rewriting on the client.
		return Ok(new { Ports = loginServerPorts, ConnectionToken = token });
	}

	/// <summary>
	/// Registers the connection token HMAC key in the database so game servers
	/// (Login/World/Scene) can discover and use it for token verification without
	/// environment variable coordination.
	/// This is a best-effort operation — failures are logged but never propagated
	/// to the client. The database is the sole source for this key.
	/// </summary>
	/// <param name="keyId">The logical key identifier (e.g., region code, or "default").</param>
	/// <param name="hmacKey">The raw HMAC-SHA256 key bytes.</param>
	/// <param name="ct">Cancellation token.</param>
	private async Task RegisterConnectionTokenKeyAsync(string keyId, byte[] hmacKey, CancellationToken ct)
	{
		if (connectionTokenKeyService == null) return;

		try
		{
			var result = await connectionTokenKeyService.UpsertAsync(keyId, hmacKey, ct);
			if (result.IsSuccess)
			{
				await Log.Debug("LoginServerController",
					$"Registered connection token key '{keyId}' in database.");
			}
			else
			{
				await Log.Warning("LoginServerController",
					$"Failed to register connection token key '{keyId}' in database: {result.ErrorMessage}");
			}
		}
		catch (OperationCanceledException)
		{
			// Shutdown or request aborted — not actionable.
		}
		catch (Exception ex)
		{
			await Log.Warning("LoginServerController",
				$"Could not register connection token key '{keyId}' in database: {ex.Message}");
		}
	}

	/// <summary>
	/// Resolves the shared HMAC key for connection token signing.
	/// Loaded from the connection_token_keys database table (key_id='shared').
	/// All IpFetchServers share one key. No environment variable fallback.
	/// Returns null if the database is unavailable or the key is not found.
	/// </summary>
	private byte[]? GetConnectionTokenHmacKey()
	{
		if (connectionTokenKeyService == null) return null;
		try
		{
			var result = connectionTokenKeyService.FetchByKeyIdAsync("shared", HttpContext.RequestAborted)
				.GetAwaiter().GetResult();
			if (result.IsSuccess && result.Data.HmacKey is byte[] dbKey && dbKey.Length >= 32)
				return dbKey;
		}
		catch { /* DB unavailable */ }
		return null;
	}

	/// <summary>
	/// Returns null — all IpFetchServers share one key in the database.
	/// No per-region keyId is needed for single shared-key deployments.
	/// </summary>
	private static string? GetConnectionTokenKeyId() => null;
}
