using System.Collections.Generic;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Whether two connection handles name the same connection, for any connection type.
	/// </summary>
	/// <remarks>
	/// The authenticator is generic over its connection type. A class — FishNet's
	/// <c>NetworkConnection</c> in the game — is the same connection only when it is the same
	/// object, so identity is what these checks mean. A value type — the <c>int</c> connection ids of
	/// the unit-test harness — has no identity: <c>ReferenceEquals</c> boxes both arguments and is
	/// ALWAYS false. The two-factor check compared that way refused every code on a value-type
	/// connection as coming from a different connection, which is why no test could ever complete a
	/// two-factor sign-in (issue #267). Reference types keep exactly the identity test they had.
	/// </remarks>
	internal static class ConnectionIdentity
	{
		/// <summary>True when <paramref name="a"/> and <paramref name="b"/> are the same connection.</summary>
		public static bool Same<TConnection>(TConnection a, TConnection b) =>
			typeof(TConnection).IsValueType
				? EqualityComparer<TConnection>.Default.Equals(a, b)
				: ReferenceEquals(a, b);
	}
}
