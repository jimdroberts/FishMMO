using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FishMMO.Logging;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Verifies the <c>X-FishMMO-Client</c> signature on incoming requests.
	/// See <c>FishMMO-Unity/Assets/Scripts/Client/Launcher/ClientApiSigner.cs</c>
	/// for the matching client-side producer and the canonical-string format.
	///
	/// <para>
	/// The gate is intentionally lightweight: it filters out generic crawlers
	/// and casual scanners that don't ship the signed header, and it adds an
	/// anti-replay window so a captured header is useful only for ~5 minutes.
	/// It is NOT an authentication mechanism — the shared secret is compiled
	/// into the public client and any motivated attacker can extract it.
	/// All real authority comes from the SRP/token flow inside the application.
	/// </para>
	/// </summary>
	internal static class ClientGate
	{
		private const string HeaderName = "X-FishMMO-Client";
		private const string Version = "v1";
		private const int MaxSkewSeconds = 300;
		private const int NonceCacheCapacity = 20_000;
		private const string SecretEnvVar = "FISHMMO_CLIENT_GATE_SECRET";
		private const string LogChannel = "ClientGate";

		// Nonce -> expiry unix seconds. ConcurrentDictionary is the simplest
		// thread-safe structure available; we periodically prune expired keys
		// and hard-cap the size so memory cannot grow unboundedly under attack.
		private static readonly ConcurrentDictionary<string, long> seenNonces = new();
		private static long lastPruneTicks;

		/// <summary>
		/// Adds the gate middleware. Reads the shared secret from the
		/// <c>FISHMMO_CLIENT_GATE_SECRET</c> environment variable. If the
		/// secret is missing in Production the host refuses to start; in
		/// other environments it logs loudly and lets requests through so
		/// local dev isn't blocked by an unconfigured laptop.
		///
		/// Paths in <paramref name="bypassPaths"/> (case-insensitive, prefix
		/// match) skip the gate. Use this for liveness probes that must work
		/// without the shared secret (e.g., /healthz on loopback).
		/// </summary>
		public static IApplicationBuilder UseClientGate(this IApplicationBuilder app, IHostEnvironment environment, params string[] bypassPaths)
		{
			string? secretText = Environment.GetEnvironmentVariable(SecretEnvVar);
			if (string.IsNullOrEmpty(secretText))
			{
				if (environment.IsProduction())
				{
					throw new InvalidOperationException(
						$"{SecretEnvVar} must be set in Production. The client gate cannot " +
						"verify request signatures without a shared secret.");
				}
				Log.Warning(LogChannel, $"{SecretEnvVar} is unset in {environment.EnvironmentName}; client gate is permissive (all requests pass).");
				return app;
			}

			byte[] secret = Encoding.UTF8.GetBytes(secretText);
			string[] bypass = bypassPaths ?? Array.Empty<string>();

			return app.Use(async (ctx, next) =>
			{
				string path = ctx.Request.Path.HasValue ? ctx.Request.Path.Value! : "/";

				// Bypass: liveness/operational endpoints we don't want to bind to the gate.
				foreach (string prefix in bypass)
				{
					if (!string.IsNullOrEmpty(prefix) &&
						path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					{
						await next();
						return;
					}
				}

				if (!ctx.Request.Headers.TryGetValue(HeaderName, out var headerValues) ||
					headerValues.Count == 0)
				{
					await Reject(ctx, "missing X-FishMMO-Client");
					return;
				}

				string headerValue = headerValues[0] ?? string.Empty;
				if (!TryParseHeader(headerValue, out string tsRaw, out string nonce, out string sig))
				{
					await Reject(ctx, "malformed X-FishMMO-Client");
					return;
				}

				if (!long.TryParse(tsRaw, out long ts))
				{
					await Reject(ctx, "invalid timestamp");
					return;
				}

				long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				long skew = Math.Abs(now - ts);
				if (skew > MaxSkewSeconds)
				{
					await Reject(ctx, $"timestamp skew {skew}s exceeds {MaxSkewSeconds}s");
					return;
				}

				// Nonce sanity: limit length to keep the cache key bounded.
				if (nonce.Length == 0 || nonce.Length > 64)
				{
					await Reject(ctx, "invalid nonce");
					return;
				}

				// Compute expected signature.
				// PathBase + Path + QueryString reproduces what the client signed.
				string canonicalPath = (ctx.Request.PathBase.HasValue ? ctx.Request.PathBase.Value : string.Empty)
					+ (ctx.Request.Path.HasValue ? ctx.Request.Path.Value : string.Empty)
					+ (ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : string.Empty);
				string canonical = Version + "\n" + ctx.Request.Method.ToUpperInvariant() + "\n" + canonicalPath + "\n" + ts.ToString() + "\n" + nonce;

				byte[] expectedMac;
				using (HMACSHA256 hmac = new HMACSHA256(secret))
				{
					expectedMac = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
				}
				string expected = ToBase64Url(expectedMac);

				if (!FixedTimeEquals(sig, expected))
				{
					await Reject(ctx, "signature mismatch");
					return;
				}

				// Anti-replay: insert the nonce into the cache with an expiry past the
				// skew window. TryAdd returns false if it's already present.
				long expiry = now + MaxSkewSeconds + 5;
				if (!seenNonces.TryAdd(nonce, expiry))
				{
					await Reject(ctx, "nonce replay");
					return;
				}

				MaybePruneNonceCache(now);
				await next();
			});
		}

		private static bool TryParseHeader(string value, out string ts, out string nonce, out string sig)
		{
			ts = nonce = sig = string.Empty;
			if (string.IsNullOrEmpty(value)) return false;
			// Expected: v1.<ts>.<nonce>.<sig>
			string[] parts = value.Split('.');
			if (parts.Length != 4) return false;
			if (!string.Equals(parts[0], Version, StringComparison.Ordinal)) return false;
			ts = parts[1];
			nonce = parts[2];
			sig = parts[3];
			return ts.Length > 0 && nonce.Length > 0 && sig.Length > 0;
		}

		private static string ToBase64Url(byte[] bytes)
		{
			string s = Convert.ToBase64String(bytes);
			return s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
		}

		private static bool FixedTimeEquals(string a, string b)
		{
			// Constant-time compare on the ASCII bytes; length leak is acceptable
			// because both sides are fixed-length base64url of a 32-byte HMAC.
			byte[] ba = Encoding.ASCII.GetBytes(a);
			byte[] bb = Encoding.ASCII.GetBytes(b);
			return CryptographicOperations.FixedTimeEquals(ba, bb);
		}

		private static void MaybePruneNonceCache(long nowSec)
		{
			// Cheap throttle: only attempt a sweep every ~5 seconds, AND only when
			// the cache is getting large. The pruning itself walks the dictionary
			// once which is O(n) but n is capped by MaxSkewSeconds * RPS so it's
			// trivial in practice.
			long ticks = System.Diagnostics.Stopwatch.GetTimestamp();
			long elapsedSinceLast = ticks - Interlocked.Read(ref lastPruneTicks);
			long elapsedSeconds = elapsedSinceLast / System.Diagnostics.Stopwatch.Frequency;
			if (elapsedSeconds < 5 && seenNonces.Count < NonceCacheCapacity) return;
			Interlocked.Exchange(ref lastPruneTicks, ticks);

			foreach (var kv in seenNonces)
			{
				if (kv.Value <= nowSec)
				{
					seenNonces.TryRemove(kv.Key, out _);
				}
			}

			// Hard cap: if we're still over capacity after the time-based sweep
			// (e.g., a burst of fresh nonces), drop the lot. The next legitimate
			// request will re-populate. This keeps memory bounded under a flood.
			if (seenNonces.Count >= NonceCacheCapacity)
			{
				Log.Warning(LogChannel, $"Nonce cache exceeded {NonceCacheCapacity}; clearing.");
				seenNonces.Clear();
			}
		}

		private static async Task Reject(HttpContext ctx, string reason)
		{
			// Single uniform response so we don't help an attacker probe which check failed.
			// Reason is logged server-side at Debug level only.
			_ = Log.Debug(LogChannel, $"Reject {ctx.Connection.RemoteIpAddress} {ctx.Request.Method} {ctx.Request.Path}: {reason}");
			ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
			await ctx.Response.WriteAsync("Unauthorized.");
		}
	}
}
