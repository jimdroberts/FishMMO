using System.Collections.Concurrent;
using System.Linq;
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
		// Tightened from 300s to 30s to shrink the replay window.
		// The per-process nonce cache is sized for this window; bumping it back up
		// requires proportionally increasing NonceCacheCapacity to maintain the
		// same eviction headroom.
		private const int MaxSkewSeconds = 30;
		private const int NonceCacheCapacity = 20_000;
		// Minimum HMAC secret length in bytes. Anything shorter
		// is rejected at startup regardless of environment — a short shared secret
		// defeats the entire gate by being brute-forceable offline.
		private const int MinSecretBytes = 32;
		private const string SecretEnvVar = "FISHMMO_CLIENT_GATE_SECRET";
		private const string LogChannel = "ClientGate";

		// Nonce -> expiry unix seconds. ConcurrentDictionary is the simplest
		// thread-safe structure available; we periodically prune expired keys
		// and hard-cap the size so memory cannot grow unboundedly under attack.
		private static readonly ConcurrentDictionary<string, long> seenNonces = new();
		private static long lastPruneTicks;
		// Serialises the prune+evict snapshot so the LRU
		// OrderBy snapshot cannot race with concurrent prunes producing a stale
		// victim list that evicts still-valid nonces and leaves the cache over cap.
		private static readonly object pruneLock = new object();

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

			// Accept a comma-separated keyset so operators can
			// rotate the shared secret by deploying with both old+new keys, waiting
			// for clients to upgrade, then deploying again with only the new key.
			string[] secretParts = secretText.Split(',', StringSplitOptions.RemoveEmptyEntries);
			byte[][] secrets = new byte[secretParts.Length][];
			for (int i = 0; i < secretParts.Length; i++)
			{
				byte[] s = Encoding.UTF8.GetBytes(secretParts[i].Trim());
				// Enforce the minimum secret length up-front.
				if (s.Length < MinSecretBytes)
				{
					throw new InvalidOperationException(
						$"{SecretEnvVar} entry #{i + 1} is only {s.Length} bytes; minimum is {MinSecretBytes}. " +
						"A short shared secret defeats the gate.");
				}
				secrets[i] = s;
			}
			if (secrets.Length == 0)
			{
				throw new InvalidOperationException($"{SecretEnvVar} contained no usable entries after parsing.");
			}
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
				// Collapse repeated slashes and reject traversal segments so the signed canonical string
				// cannot be desynchronised from the routing target via path-equivalence tricks.
				string rawCanonicalPath = (ctx.Request.PathBase.HasValue ? ctx.Request.PathBase.Value : string.Empty)
					+ (ctx.Request.Path.HasValue ? ctx.Request.Path.Value : string.Empty);
				string? normalizedPath = CanonicalizePath(rawCanonicalPath);
				if (normalizedPath == null)
				{
					await Reject(ctx, "invalid path");
					return;
				}
				string canonicalPath = normalizedPath
					+ (ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : string.Empty);
				string canonical = Version + "\n" + ctx.Request.Method.ToUpperInvariant() + "\n" + canonicalPath + "\n" + ts.ToString() + "\n" + nonce;

				// Try every active key. Always evaluate all HMACs
				// before deciding so we do not leak which slot matched via timing.
				byte[] canonicalBytes = Encoding.UTF8.GetBytes(canonical);
				bool anyMatch = false;
				for (int i = 0; i < secrets.Length; i++)
				{
					byte[] expectedMac;
					using (HMACSHA256 hmac = new HMACSHA256(secrets[i]))
					{
						expectedMac = hmac.ComputeHash(canonicalBytes);
					}
					string expected = ToBase64Url(expectedMac);
					if (FixedTimeEquals(sig, expected))
					{
						anyMatch = true;
					}
				}

				if (!anyMatch)
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

			// Serialises the prune+evict so the LRU snapshot
			// cannot race with a concurrent prune. TryEnter (non-blocking) means a
			// second caller arriving while a prune is in flight simply skips —
			// the in-flight prune already covers the work.
			if (!System.Threading.Monitor.TryEnter(pruneLock))
				return;
			try
			{
				Interlocked.Exchange(ref lastPruneTicks, ticks);

				// Snapshot first so we walk a stable view rather than the live dictionary.
				var snapshot = seenNonces.ToArray();
				for (int i = 0; i < snapshot.Length; i++)
				{
					if (snapshot[i].Value <= nowSec)
					{
						seenNonces.TryRemove(snapshot[i].Key, out _);
					}
				}

				// Hard cap: if we're still over capacity after the time-based sweep
				// (e.g., a burst of fresh nonces), evict the oldest-expiring entries via LRU
				// rather than clearing the entire cache. Clearing would allow a flood of fresh
				// nonces to evict legitimate still-valid nonces and enable replay of those
				// previously-seen nonces until their original expiry. LRU bounds memory while
				// preserving replay-protection for the most recent legitimate traffic.
				int over = seenNonces.Count - NonceCacheCapacity;
				if (over > 0)
				{
					int target = Math.Max(over, NonceCacheCapacity / 4);
					Log.Warning(LogChannel, $"Nonce cache exceeded {NonceCacheCapacity}; evicting {target} oldest entries.");
					// Re-snapshot post-expiry. Use Array.Sort so we operate on a fully
					// detached copy instead of an OrderBy enumerator over the live dict.
					var liveSnapshot = seenNonces.ToArray();
					Array.Sort(liveSnapshot, (a, b) => a.Value.CompareTo(b.Value));
					int evictCount = Math.Min(target, liveSnapshot.Length);
					for (int i = 0; i < evictCount; i++)
					{
						seenNonces.TryRemove(liveSnapshot[i].Key, out _);
					}
				}
			}
			finally
			{
				System.Threading.Monitor.Exit(pruneLock);
			}
		}

		/// <summary>
		/// Normalises% the request path so client and server
		/// produce the same canonical string. Collapses repeated slashes and
		/// rejects any path-traversal segment (raw or percent-encoded).
		/// Returns null if the path is unsafe.
		/// </summary>
		private static string? CanonicalizePath(string path)
		{
			if (string.IsNullOrEmpty(path)) return "/";
			for (int i = 0; i < path.Length; i++)
			{
				char c = path[i];
				if (c == '\0' || c == '\\') return null;
			}
			// Reject any traversal-style segment (literal or percent-encoded).
			string[] segments = path.Split('/');
			for (int i = 0; i < segments.Length; i++)
			{
				string seg = segments[i];
				if (seg == ".." || seg == ".") return null;
				if (seg.Equals("%2e", StringComparison.OrdinalIgnoreCase)) return null;
				if (seg.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase)) return null;
				if (seg.Equals("%2e.", StringComparison.OrdinalIgnoreCase)) return null;
				if (seg.Equals(".%2e", StringComparison.OrdinalIgnoreCase)) return null;
			}
			while (path.IndexOf("//", StringComparison.Ordinal) >= 0)
			{
				path = path.Replace("//", "/");
			}
			return path;
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
