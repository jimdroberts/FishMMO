using System.Diagnostics.CodeAnalysis;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// What SRP hashes as an account's identity, and how a sign-in identifier is written on the wire.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One fixed identity for every account.</b> SRP derives the verifier from
	/// <c>x = H(salt, H(identity ":" password))</c>, and every proof hashes the identity again, so a
	/// proof verifies only if the identity is byte-for-byte what it was when the password was set.
	/// The typed name used to be that identity, and nothing agreed on its spelling: registration and
	/// sign-in hashed what the player typed, the Control Panel's password reset and password change
	/// hashed the stored lowercase name, and an email sign-in hashed the email — which never matched
	/// a verifier made from the username, so email sign-in could not succeed (issue #267).
	/// </para>
	/// <para>
	/// A constant has the same security here as a name: every account has its own random salt, which
	/// is what stops one password guess being tested against many verifiers, and the row a verifier
	/// is stored in already says whose it is. It is the identity the Control Panel's
	/// <c>wwwroot/js/srp.js</c> uses too, as <c>SRP_IDENTITY</c>; the two must never differ.
	/// </para>
	/// <para>
	/// <b>Changing <see cref="Value"/> invalidates every stored password.</b>
	/// </para>
	/// </remarks>
	public static class SrpIdentity
	{
		/// <summary>The identity every SRP derivation and proof uses. See the remarks before changing it.</summary>
		public const string Value = "fishmmo";

		/// <summary>
		/// A username or email address as it is sent and compared: trimmed and lowercased under the
		/// invariant culture.
		/// </summary>
		/// <remarks>
		/// Invariant, never the current culture: under a Turkish culture <c>"I".ToLower()</c> is a
		/// dotless ı. Usernames are ASCII letters, digits and underscores, so for them this changes
		/// only A–Z. Account lookups are case-insensitive on the server regardless; this makes the
		/// wire form one spelling, so "Jim", "JIM" and "jim" are the same sign-in.
		/// </remarks>
		/// <param name="identifier">A username or email address.</param>
		/// <returns>The normalised identifier, or the input when it is null or empty.</returns>
		[return: NotNullIfNotNull("identifier")]
		public static string? NormalizeIdentifier(string? identifier) =>
			string.IsNullOrEmpty(identifier) ? identifier : identifier.Trim().ToLowerInvariant();
	}
}
