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
		/// Timestamp of the last server heartbeat or status update (UTC).
		///
		/// <para>
		/// <b>IMPORTANT:</b> Callers MUST supply values produced by
		/// <see cref="DateTime.UtcNow"/>.  The serialized form does not
		/// preserve <see cref="DateTimeKind"/> metadata — comparisons
		/// across different time zones silently produce incorrect results
		/// if a non-UTC value is stored here.  <c>DateTimeOffset</c>
		/// was considered but FishNet does not support serializing it.
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
