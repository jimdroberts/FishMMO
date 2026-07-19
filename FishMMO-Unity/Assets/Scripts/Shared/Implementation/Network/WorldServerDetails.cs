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
		/// Timestamp of the last server heartbeat or status update,
		/// stored as a <see cref="DateTimeOffset"/> to carry UTC offset
		/// information and prevent silent timezone miscomparisons.
		///
		/// <para>
		/// Callers should supply values produced by
		/// <see cref="DateTimeOffset.UtcNow"/> (or convert from database
		/// timestamps using an explicit UTC offset).  Comparisons and
		/// arithmetic are safe regardless of the local time zone of
		/// either the producer or consumer.
		/// </para>
		/// </summary>
		public DateTimeOffset LastPulse;
		/// <summary>Port number for the server.</summary>
		public ushort Port;
		/// <summary>Number of characters currently on the server.</summary>
		public int CharacterCount;
		/// <summary>Indicates whether the server is locked (not accepting new connections).</summary>
		public bool Locked;
	}
}
