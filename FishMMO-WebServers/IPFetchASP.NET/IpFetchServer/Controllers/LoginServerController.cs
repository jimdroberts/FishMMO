using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FishMMO.Database;
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
	/// <param name="connectionTokenKeyService">Service that reads the shared connection token HMAC key from the database.</param>
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
	/// on success, HTTP 404 if no servers are available, HTTP 503 if the database
	/// could not be read, or HTTP 500 if the signing key is not configured.
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
					/* The factory throws rather than returning null, and so does the query when the
					 * database is down. Both used to escape to the global exception handler as a 500 —
					 * "the server is broken" — for what is a 503, "try again". */
					try
					{
						using NpgsqlDbContext dbContext = dbContextFactory.CreateDbContext();

						// Project to an immutable array before caching.
						loginServerPorts = await dbContext.LoginServers
							.AsNoTracking()
							.Select(l => l.Port)
							.ToArrayAsync(HttpContext.RequestAborted);
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						await Log.Error("LoginServerController", $"Could not read the login server directory: {ex.Message}");
						return StatusCode(StatusCodes.Status503ServiceUnavailable, "Login server directory temporarily unavailable.");
					}

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
		var (hmacKey, keyFailure) = await GetConnectionTokenHmacKeyAsync(HttpContext.RequestAborted);
		if (keyFailure != null || hmacKey == null)
		{
			return keyFailure ?? StatusCode(StatusCodes.Status500InternalServerError, "Server configuration error.");
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

		/* Nothing is written back. Every request used to fire-and-forget an upsert of the key it had
		 * just read from the database — a write per anonymous request, racing the installer: a request
		 * that read the old key before a rotation wrote it back after, silently reverting it, and the
		 * upsert's is_active = true reactivated a key an operator had deactivated. The key is already
		 * in the database, which is where the game servers read it from; the installer is its writer. */

		// Wrap in a "Ports" envelope so Unity's JsonUtility can deserialize
		// the response without manual string rewriting on the client.
		return Ok(new { Ports = loginServerPorts, ConnectionToken = token });
	}

	/// <summary>
	/// Reads the shared HMAC key for connection token signing from the connection_token_keys table
	/// (key_id='shared'). All IpFetchServers share one key. No environment variable fallback.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Awaited, and every failure answered for what it is. This was a synchronous
	/// <c>GetAwaiter().GetResult()</c> on a request thread inside a bare <c>catch</c>, and any failure
	/// at all came back as "HMAC key not configured" and a 500 — so a database blip was logged as a
	/// configuration error, and sent operators looking for a missing key that was there all along.
	/// </para>
	/// <para>
	/// An inactive key is refused. The game servers verify only against active keys, so a token signed
	/// with one would be rejected at the login server with nothing here to say why.
	/// </para>
	/// </remarks>
	/// <returns>The key, or the reply to give instead.</returns>
	private async Task<(byte[]? Key, IActionResult? Failure)> GetConnectionTokenHmacKeyAsync(CancellationToken ct)
	{
		if (connectionTokenKeyService == null)
		{
			await Log.Error("LoginServerController", "ConnectionToken HMAC key cannot be read: no key service is registered.");
			return (null, StatusCode(StatusCodes.Status500InternalServerError, "Server configuration error."));
		}

		var result = await connectionTokenKeyService.FetchByKeyIdAsync("shared", ct);
		if (!result.IsSuccess)
		{
			if (result.ErrorCode == DatabaseErrorCodes.NotFound)
			{
				await Log.Error("LoginServerController",
					"ConnectionToken HMAC key not configured: there is no 'shared' row in connection_token_keys. Run the installer's Configure Server Keys.");
				return (null, StatusCode(StatusCodes.Status500InternalServerError, "Server configuration error."));
			}

			await Log.Error("LoginServerController",
				$"ConnectionToken HMAC key could not be read: [{result.ErrorCode}] {result.ErrorMessage}. This is a database problem, not a missing key.");
			return (null, StatusCode(StatusCodes.Status503ServiceUnavailable, "Login server directory temporarily unavailable."));
		}

		if (!result.Data.IsActive)
		{
			await Log.Error("LoginServerController",
				"ConnectionToken HMAC key 'shared' is deactivated; game servers will not verify tokens signed with it. Refusing to sign.");
			return (null, StatusCode(StatusCodes.Status500InternalServerError, "Server configuration error."));
		}

		if (result.Data.HmacKey == null)
		{
			await Log.Error("LoginServerController", "ConnectionToken HMAC key 'shared' has no key material.");
			return (null, StatusCode(StatusCodes.Status500InternalServerError, "Server configuration error."));
		}

		return (result.Data.HmacKey, null);
	}

	/// <summary>
	/// Returns null — all IpFetchServers share one key in the database.
	/// No per-region keyId is needed for single shared-key deployments.
	/// </summary>
	private static string? GetConnectionTokenKeyId() => null;
}
