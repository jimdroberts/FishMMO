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
		/// <summary>Port number for the server.</summary>
		public ushort Port;
		/// <summary>
		/// Number of characters currently on the server.
		/// Server must enforce CharacterCount >= 0. Negative values indicate
		/// a desync or uninitialized counter.
		/// </summary>
		public int CharacterCount;
		/// <summary>Indicates whether the server is locked (not accepting new connections).</summary>
		public bool Locked;
	}
}
