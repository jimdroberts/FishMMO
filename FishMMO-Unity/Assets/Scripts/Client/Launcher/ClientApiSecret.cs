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
		/// The shared secret. Long, opaque, and high-entropy.
		///
		/// <para>
		/// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
		/// WARNING: THIS IS A PLACEHOLDER. IT MUST BE REPLACED BEFORE SHIPPING.
		/// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
		/// </para>
		///
		/// <para>
		/// The value committed here is a well-known default so the project
		/// compiles and can be tested in the editor / dev builds.  If you ship
		/// a public binary with this default, every user will have the same
		/// secret — recovery from the binary is trivial — and the gate provides
		/// zero protection.  Rotate the secret by changing this literal AND
		/// setting the matching server-side value via the
		/// <c>FISHMMO_CLIENT_GATE_SECRET</c> environment variable.
		/// </para>
		/// </summary>
		private const string secretLiteral =
			"FishMMO-default-client-gate-secret-replace-before-release-d8b1c4a6e7f23519";

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
		/// <summary>
		/// True when the default placeholder API secret is still active in a release build.
		/// Other security-sensitive code (e.g. ClientSecurityBuildValidator) should check this flag.
		/// </summary>
		public static bool IsPlaceholderSecret { get; private set; }

		static ClientApiSecret()
		{
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
			// Alert other security code (e.g. ClientSecurityBuildValidator) that the
			// default placeholder API secret is still active in a release build.
			IsPlaceholderSecret = true;
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
