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
	/// To rotate the secret: change <see cref="SecretLiteral"/> and rebuild
	/// the client at the same time as the matching server-side configuration
	/// (env var <c>FISHMMO_CLIENT_GATE_SECRET</c>). There is no rolling
	/// upgrade window — by design, mismatched clients get a hard 401.
	/// </para>
	/// </summary>
	internal static class ClientApiSecret
	{
		/// <summary>
		/// The shared secret. Long, opaque, and high-entropy. Replace before
		/// public release; the value committed here is a placeholder so the
		/// project compiles. The matching server-side value MUST be set via
		/// the <c>FISHMMO_CLIENT_GATE_SECRET</c> environment variable.
		/// </summary>
		private const string SecretLiteral =
			"FishMMO-default-client-gate-secret-replace-before-release-d8b1c4a6e7f23519";

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
		static ClientApiSecret()
		{
			UnityEngine.Debug.LogError(
				"[ClientApiSecret] The placeholder default secret is still in use in a release build. " +
				"Set FISHMMO_CLIENT_GATE_SECRET to a unique value and update SecretLiteral before shipping.");
		}
#endif

		/// <summary>
		/// Returns the secret as a fresh UTF-8 byte array. Callers SHOULD zero
		/// the buffer after use; this method exists so the secret string is
		/// not pinned in interned-string form longer than necessary.
		/// </summary>
		public static byte[] GetBytes()
		{
			return Encoding.UTF8.GetBytes(SecretLiteral);
		}
	}
}
