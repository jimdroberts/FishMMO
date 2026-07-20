using System;
using System.Collections.Generic;
using System.Net;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Utility for resolving the configured API host(s) into an ordered list of
	/// candidate base URLs. <c>APIHost</c> may be a single URL or a comma-separated
	/// list; this resolver normalizes the values (trims, ensures a trailing slash)
	/// and randomizes the order so callers can try each in sequence with failover.
	///
	/// All returned candidates are validated:
	///   * Must parse as an absolute URI.
	///   * Must use the <c>https</c> scheme in release builds. The <c>http</c>
	///     scheme is accepted only in editor / development builds and only when
	///     the host is a loopback address — anything else enables trivial MITM
	///     against the login-server discovery and patcher endpoints.
	/// Entries that fail validation are dropped (with a warning) instead of
	/// being silently retried.
	/// </summary>
	internal static class ApiHostResolver
	{
		private const string logChannel = "ApiHostResolver";

		/// <summary>
		/// Maximum length of any single candidate URL accepted by the resolver.
		/// A generous cap; legitimate hosts are tens of characters.
		/// </summary>
		private const int maxCandidateLength = 512;

		/// <summary>
		/// Sanitizes an APIHost candidate for safe inclusion in log lines. Strips
		/// CR/LF (defeats CWE-117 log injection from a misconfigured / hostile
		/// override) and truncates excessively long values. Always returns a
		/// non-null string so call sites can format it without null checks.
		/// </summary>
		public static string SanitizeForLog(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			string s = value;
			// Strip newline / carriage-return so a hostile override can't forge log lines.
			if (s.IndexOf('\n') >= 0) s = s.Replace("\n", "\\n");
			if (s.IndexOf('\r') >= 0) s = s.Replace("\r", "\\r");
			const int LogTruncationLength = 256;
			if (s.Length > LogTruncationLength)
			{
				s = s.Substring(0, LogTruncationLength) + "…";
			}
			return s;
		}

		/// <summary>
		/// Resolves the active API host string.
		///
		/// In editor / development builds, a user override stored in
		/// <see cref="Configuration.GlobalSettings"/> takes precedence so that
		/// developers can point the client at a staging endpoint. In release
		/// builds the user override is intentionally ignored — accepting a
		/// player-supplied API host enables trivial MITM and credential
		/// harvesting because the host returned by <c>/loginserver</c> is
		/// trusted implicitly afterwards.
		/// </summary>
		public static string GetRawApiHost()
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (Configuration.GlobalSettings != null &&
				Configuration.GlobalSettings.TryGetString("APIHost", out string configured) &&
				!string.IsNullOrWhiteSpace(configured))
			{
				return configured;
			}
#endif
			return Constants.Configuration.APIHost;
		}

		/// <summary>
		/// Returns the candidate base URLs in randomized order. Each entry is
		/// trimmed and guaranteed to end with '/'. Duplicates and empty entries
		/// are removed.
		/// </summary>
		/// <param name="rawApiHost">Raw APIHost string (single URL or comma-separated).</param>
		public static List<string> GetCandidates(string rawApiHost)
		{
			List<string> result = new List<string>();
			if (string.IsNullOrWhiteSpace(rawApiHost))
			{
				return result;
			}

			string[] parts = rawApiHost.Split(',');
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < parts.Length; i++)
			{
				string trimmed = parts[i].Trim();
				if (trimmed.Length == 0)
				{
					continue;
				}
				if (trimmed.Length > maxCandidateLength)
				{
					_ = Log.Warning(logChannel, $"Rejected APIHost candidate (length>{maxCandidateLength}): {SanitizeForLog(trimmed)}");
					continue;
				}
				if (!trimmed.EndsWith("/", StringComparison.Ordinal))
				{
					trimmed += "/";
				}
				if (!IsValidCandidate(trimmed))
				{
					// Reason already logged by IsValidCandidate.
					continue;
				}
				if (seen.Add(trimmed))
				{
					result.Add(trimmed);
				}
			}

			// Fisher-Yates shuffle so failover order varies across runs without favouring the first listed host.
			for (int i = result.Count - 1; i > 0; i--)
			{
				int j = UnityEngine.Random.Range(0, i + 1);
				if (j != i)
				{
					(result[i], result[j]) = (result[j], result[i]);
				}
			}

			return result;
		}

		/// <summary>
		/// Convenience: <see cref="GetCandidates(string)"/> using the resolved raw
		/// host from <see cref="GetRawApiHost"/>.
		/// </summary>
		public static List<string> GetCandidates() => GetCandidates(GetRawApiHost());

		/// <summary>
		/// Validates a single candidate base URL. Returns <c>true</c> if the URL
		/// is acceptable, <c>false</c> if it should be dropped. Logs a warning
		/// with the reason on rejection.
		/// </summary>
		private static bool IsValidCandidate(string candidate)
		{
			if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
			{
				_ = Log.Warning(logChannel, $"Rejected APIHost candidate (not an absolute URI): {SanitizeForLog(candidate)}");
				return false;
			}

			// Reject anything that isn't HTTP(S). file://, javascript:, etc. are
			// never meaningful for the loginserver/patcher endpoints.
			bool isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
			bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
			if (!isHttp && !isHttps)
			{
				_ = Log.Warning(logChannel, $"Rejected APIHost candidate (scheme must be https): {SanitizeForLog(candidate)}");
				return false;
			}

			// Require HTTPS for release builds. The plaintext http scheme is only
			// allowed in editor/development builds and only when pointing at a
			// loopback address — any other plaintext target enables MITM of the
			// login-server discovery handshake.
			if (isHttp)
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (!IsLoopback(uri))
				{
					_ = Log.Warning(logChannel, $"Rejected APIHost candidate (http only allowed for loopback in DEV builds): {SanitizeForLog(candidate)}");
					return false;
				}
#else
				_ = Log.Warning(logChannel, $"Rejected APIHost candidate (http scheme not allowed in release builds): {SanitizeForLog(candidate)}");
				return false;
#endif
			}

			// userinfo (user:pass@host) is never legitimate here and can be used
			// to confuse log readers ("https://api.fishmmo.com@evil.example/").
			if (!string.IsNullOrEmpty(uri.UserInfo))
			{
				_ = Log.Warning(logChannel, $"Rejected APIHost candidate (userinfo not permitted): {SanitizeForLog(candidate)}");
				return false;
			}

			// In release builds, reject hosts that resolve as IP literals into
			// private/loopback/link-local space. Cert pinning blocks the obvious
			// MITM cases, but a pinned cert plus a hostile DHCP-supplied DNS server
			// can still try to rebind a "legit-looking" host to 127.0.0.1 or
			// 192.168.x.y to pivot from the player's LAN. We don't try to resolve
			// DNS names here (cost + privacy); we only catch the easy case where
			// the configured host is itself a private/loopback IP literal.
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
			if (IsPrivateOrReservedIpLiteral(uri))
			{
				_ = Log.Warning(logChannel, $"Rejected APIHost candidate (private/loopback IP literal not permitted in release builds): {SanitizeForLog(candidate)}");
				return false;
			}
#endif

			return true;
		}

		/// <summary>True when <paramref name="uri"/> targets a loopback host.</summary>
		private static bool IsLoopback(Uri uri)
		{
			try { return uri.IsLoopback; }
			catch { return false; }
		}

		/// <summary>
		/// True when the URI's host is an IP literal that falls in loopback,
		/// link-local, or RFC1918/ULA private space. Hostnames (i.e. names that
		/// require DNS resolution) return <c>false</c> — we deliberately don't
		/// do DNS lookups here to avoid leaking the configured host before the
		/// player has consented to a connection.
		/// </summary>
		private static bool IsPrivateOrReservedIpLiteral(Uri uri)
		{
			if (uri == null) return false;
			string host = uri.DnsSafeHost;
			if (string.IsNullOrEmpty(host)) return false;
			// IPAddress.TryParse rejects hostnames; only IP literals succeed.
			if (!IPAddress.TryParse(host, out IPAddress ip)) return false;

			if (IPAddress.IsLoopback(ip)) return true;

			if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
			{
				byte[] b = ip.GetAddressBytes();
				// 10.0.0.0/8
				if (b[0] == 10) return true;
				// 172.16.0.0/12
				if (b[0] == 172 && (b[1] & 0xF0) == 16) return true;
				// 192.168.0.0/16
				if (b[0] == 192 && b[1] == 168) return true;
				// 169.254.0.0/16 link-local
				if (b[0] == 169 && b[1] == 254) return true;
				// 0.0.0.0/8 "this network" — unrouteable; treat as reserved.
				if (b[0] == 0) return true;
			}
			else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
			{
				// fc00::/7 unique local
				if (ip.IsIPv6SiteLocal) return true;
				byte[] b = ip.GetAddressBytes();
				if ((b[0] & 0xFE) == 0xFC) return true; // fc00::/7
				// fe80::/10 link-local
				if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return true;
			}

			return false;
		}
	}
}
