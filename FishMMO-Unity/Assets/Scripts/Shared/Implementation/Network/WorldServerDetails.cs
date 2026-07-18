using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing details about a world server, including name, port, status, and player count.
	/// </summary>
	[Serializable]
	public class WorldServerDetails
	{
		/// <summary>Name of the world server.</summary>
		public string Name;
		/// <summary>
		/// Timestamp of the last server heartbeat or status update.
		///
		/// <para>
		/// [Obsolete] NOTE: This field stores a <see cref="DateTime"/> without
		/// enforcing <see cref="DateTimeKind.Utc"/>.  Callers MUST supply values
		/// produced by <see cref="DateTime.UtcNow"/> (not local/unspecified time)
		/// or comparisons across servers in different time zones will silently
		/// produce incorrect results.  The serialized form does not preserve
		/// the <c>Kind</c> metadata.
		/// </para>
		/// </summary>
		public DateTime LastPulse;
		/// <summary>Port number for the server.</summary>
		public ushort Port;
		/// <summary>Number of characters currently on the server.</summary>
		public int CharacterCount;
		/// <summary>Indicates whether the server is locked (not accepting new connections).</summary>
		public bool Locked;
	}
}