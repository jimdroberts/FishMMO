using System;
using System.Text;

namespace FishMMO.Client
{
	/// <summary>
	/// Holds the compiled-in shared secret used by <see cref="ClientApiSigner"/>
	/// to sign requests against the FishMMO public web endpoints.
	///
	/// <para>
	/// IMPORTANT: This secret is NOT a credential. It is embedded in the
	/// shipped client binary and can be recovered by anyone with the build,
	/// so it should never be relied on for authentication or authorization.
	/// Its job is to filter generic crawlers / port-scanners / opportunistic
	/// abuse traffic and to detect blatant header forgery. Real authority
	/// always comes from the SRP-derived session token issued by the auth
	/// server, NOT from this header.
	/// </para>
	/// <para>
	/// To rotate the secret: change <see cref="secretLiteral"/> and rebuild
	/// the client at the same time as the matching server-side configuration
	/// (env var <c>FISHMMO_CLIENT_GATE_SECRET</c>). There is no rolling
	/// upgrade window — by design, mismatched clients get a hard 401.
	/// </para>
	/// </summary>
	internal static class ClientApiSecret
	{
		/// <summary>
		/// Well-known committed default used only for local/dev detection.
		/// Do NOT put your production secret here — if <see cref="secretLiteral"/>
		/// still equals this string, release builds log the security hold.
		/// </summary>
		private const string PlaceholderSecret =
			"FishMMO-default-client-gate-secret-replace-before-release-d8b1c4a6e7f23519";

		/// <summary>
		/// Compiled-in shared secret. Must match the IPFetch/Patcher process env
		/// <c>FISHMMO_CLIENT_GATE_SECRET</c> exactly (UTF-8 string, same value).
		/// Eqbrowser production value — keep in sync with server secrets.env.
		/// </summary>
		private const string secretLiteral =
			"rWbj3bwCYra4kyEeciqGfgQfFknYkVGFyzOc1zkUk";

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
		/// <summary>
		/// True when the default placeholder API secret is still active in a release build.
		/// Other security-sensitive code (e.g. ClientSecurityBuildValidator) should check this flag.
		/// </summary>
		public static bool IsPlaceholderSecret { get; private set; }

		static ClientApiSecret()
		{
			// Hold only when secretLiteral was never rotated away from the known default.
			IsPlaceholderSecret = string.Equals(secretLiteral, PlaceholderSecret, StringComparison.Ordinal);
			if (!IsPlaceholderSecret)
			{
				return;
			}

			UnityEngine.Debug.LogError(
				"[ClientApiSecret] *******************************************************\n" +
				"[ClientApiSecret] *  SECURITY HOLD: The placeholder default secret is    *\n" +
				"[ClientApiSecret] *  still in use in a release build.                    *\n" +
				"[ClientApiSecret] *                                                      *\n" +
				"[ClientApiSecret] *  Set FISHMMO_CLIENT_GATE_SECRET to a unique value    *\n" +
				"[ClientApiSecret] *  AND update secretLiteral before shipping.           *\n" +
				"[ClientApiSecret] *                                                      *\n" +
				"[ClientApiSecret] *  This binary's gate secret is PUBLIC and provides    *\n" +
				"[ClientApiSecret] *  NO protection against general crawler traffic.      *\n" +
				"[ClientApiSecret] *******************************************************");
		}
#endif

		/// <summary>
		/// Returns the secret as a fresh UTF-8 byte array. Callers SHOULD zero
		/// the buffer after use; this method exists so the secret string is
		/// not pinned in interned-string form longer than necessary.
		/// </summary>
		public static byte[] GetBytes()
		{
			return Encoding.UTF8.GetBytes(secretLiteral);
		}
	}
}
