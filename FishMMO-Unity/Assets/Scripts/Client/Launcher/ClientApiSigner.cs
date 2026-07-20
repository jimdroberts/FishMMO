using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FishMMO.Client
{
	/// <summary>
	/// Produces the <c>X-FishMMO-Client</c> request signature that gates the
	/// public web endpoints (IpFetch, Patcher, WebGL). The gate is a defence
	/// against generic crawlers / opportunistic scanners and ties an API call
	/// to a fresh timestamp + nonce so that captured headers cannot be
	/// trivially replayed.
	///
	/// Header format:
	///     X-FishMMO-Client: v1.&lt;ts&gt;.&lt;nonce&gt;.&lt;sig&gt;
	///
	///   ts    — UNIX seconds (UTC). Server rejects timestamps outside ±300s.
	///   nonce — 16 random bytes, base64url, no padding. Server LRU-tracks
	///           recent nonces to reject replays inside the skew window.
	///   sig   — base64url(HMAC-SHA256(secret, "v1\n{METHOD}\n{PATH}\n{ts}\n{nonce}")).
	///
	/// The shared secret is compiled into the client (see
	/// <see cref="ClientApiSecret"/>). It is NOT a credential and confers no
	/// authority on its own — it is a low-friction filter that anyone with
	/// the binary can extract, but that all opportunistic non-FishMMO traffic
	/// will lack. Treat it as approximately equivalent to a User-Agent check
	/// hardened against trivial spoofing and replay.
	/// </summary>
	internal static class ClientApiSigner
	{
		private const string headerName = "X-FishMMO-Client";
		private const string version = "v1";

		/// <summary>
		/// Computes the gate header for the given HTTP method + absolute URL
		/// and inserts it into <paramref name="headers"/>. Existing entries
		/// for the header name are overwritten. Returns the header value for
		/// callers that need to apply it to a raw <c>UnityWebRequest</c>.
		/// </summary>
		/// <param name="headers">Destination header dictionary; may be null.</param>
		/// <param name="method">HTTP verb (GET, POST, …). Case-insensitive.</param>
		/// <param name="url">The absolute request URL. The path component is signed.</param>
		public static string SignAndAdd(Dictionary<string, string> headers, string method, string url)
		{
			string value = BuildHeaderValue(method, url);
			if (headers != null)
			{
				headers[headerName] = value;
			}
			return value;
		}

		/// <summary>The header name to set on the outgoing request.</summary>
		public static string HeaderKey => headerName;

		/// <summary>
		/// Builds the signed header value without mutating any caller state.
		/// </summary>
		public static string BuildHeaderValue(string method, string url)
		{
			if (string.IsNullOrEmpty(method)) throw new ArgumentException("HTTP method must not be null or empty.", nameof(method));
			if (string.IsNullOrEmpty(url)) throw new ArgumentException("URL must not be null or empty.", nameof(url));

			string path = ExtractPath(url);
			// Client must apply the same path normalization the
			// server gate uses (CanonicalizePath in ClientGate.cs) or the HMAC
			// will not validate. Throwing here is preferable to silently signing
			// a path the server will reject after the round trip.
			string canonicalPath = CanonicalizePath(path)
				?? throw new ArgumentException("URL path failed canonicalization", nameof(url));
			long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
			string nonce = GenerateNonce();
			string methodUpper = method.ToUpperInvariant();

			string canonical = version + "\n" + methodUpper + "\n" + canonicalPath + "\n" + ts.ToString() + "\n" + nonce;
			byte[] secret = ClientApiSecret.GetBytes();
			byte[] mac;
			using (HMACSHA256 hmac = new HMACSHA256(secret))
			{
				mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
			}
			// Cryptographically zero the secret buffer copy to remove it from
			// process memory promptly (resistant to JIT eliding a plain Array.Clear).
			// The underlying constant still lives in the loaded assembly image regardless.
			CryptographicOperations.ZeroMemory(secret);

			return version + "." + ts.ToString() + "." + nonce + "." + ToBase64Url(mac);
		}

		/// <summary>
		/// Extracts the path component (with query) used in the canonical string.
		/// Falls back to "/" if the URL cannot be parsed; the server will then
		/// reject the request with a 401 — which is preferable to silently
		/// signing the wrong canonical input.
		/// </summary>
		private static string ExtractPath(string url)
		{
			if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
			{
				// PathAndQuery includes the leading slash and any query string.
				return uri.PathAndQuery;
			}
			// Best-effort fallback: strip the scheme+authority manually.
			int schemeIdx = url.IndexOf("://", StringComparison.Ordinal);
			if (schemeIdx >= 0)
			{
				int pathIdx = url.IndexOf('/', schemeIdx + 3);
				if (pathIdx >= 0)
				{
					return url.Substring(pathIdx);
				}
			}
			return "/";
		}

		/// <summary>
		/// Mirror of <c>ClientGate.CanonicalizePath</c>.
		/// Both sides MUST apply the same normalization or the HMAC over the
		/// canonical string will mismatch. Returns null for unsafe paths.
		/// </summary>
		private static string CanonicalizePath(string path)
		{
			if (string.IsNullOrEmpty(path)) return "/";
			for (int i = 0; i < path.Length; i++)
			{
				char c = path[i];
				if (c == '\0' || c == '\\') return null;
			}
			// Query string is preserved as-is at the end; only the path portion
			// is segment-walked for traversal checks.
			int q = path.IndexOf('?');
			string pathOnly = q >= 0 ? path.Substring(0, q) : path;
			string tail = q >= 0 ? path.Substring(q) : string.Empty;
			string[] segments = pathOnly.Split('/');
			for (int i = 0; i < segments.Length; i++)
			{
				string seg = segments[i];
				if (seg == ".." || seg == ".") return null;
				if (seg.Equals("%2e", StringComparison.OrdinalIgnoreCase)) return null;
				if (seg.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase)) return null;
				if (seg.Equals("%2e.", StringComparison.OrdinalIgnoreCase)) return null;
				if (seg.Equals(".%2e", StringComparison.OrdinalIgnoreCase)) return null;
			}
			// Additional check: URL-decode the path and re-check for traversal
			// patterns.  This catches attempts to smuggle ".." or "."
			// segments via URL-encoded path separators (e.g. %2F..%2F..)
			// that the raw-path split above would not see because %2F is not
			// a '/' character in the raw string.
			// This check is performed IN ADDITION to the raw-path check, not
			// instead of it, so that both attack vectors are covered.
			string decodedPathOnly = Uri.UnescapeDataString(pathOnly);
			if (!string.Equals(pathOnly, decodedPathOnly, StringComparison.Ordinal))
			{
				string[] decodedSegments = decodedPathOnly.Split('/');
				for (int i = 0; i < decodedSegments.Length; i++)
				{
					if (decodedSegments[i] == ".." || decodedSegments[i] == ".")
					{
						return null;
					}
				}
			}
			while (pathOnly.IndexOf("//", StringComparison.Ordinal) >= 0)
			{
				pathOnly = pathOnly.Replace("//", "/");
			}
			return pathOnly + tail;
		}

		private static string GenerateNonce()
		{
			byte[] buf = new byte[16];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(buf);
			}
			return ToBase64Url(buf);
		}

		private static string ToBase64Url(byte[] bytes)
		{
			string s = Convert.ToBase64String(bytes);
			// RFC 4648 §5 url-safe alphabet, padding stripped.
			s = s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
			return s;
		}
	}
}
