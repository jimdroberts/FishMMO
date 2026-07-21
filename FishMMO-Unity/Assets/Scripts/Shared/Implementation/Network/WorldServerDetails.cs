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
		/// Timestamp of the last server heartbeat in UTC ticks.
		/// <para>
		/// Stored as <see cref="long"/> (ticks) rather than <see cref="DateTime"/>
		/// because FishNet does not preserve <see cref="DateTimeKind"/> metadata
		/// during serialization.  Ticks are always UTC — convert with
		/// <c>new DateTime(ticks, DateTimeKind.Utc)</c>.
		/// </para>
		/// </summary>
		public long LastPulseUtcTicks;
		/// <summary>Port number for the server.</summary>
		public ushort Port;
		/// <summary>Number of characters currently on the server.</summary>
		public int CharacterCount;
		/// <summary>Indicates whether the server is locked (not accepting new connections).</summary>
		public bool Locked;
	}
}
