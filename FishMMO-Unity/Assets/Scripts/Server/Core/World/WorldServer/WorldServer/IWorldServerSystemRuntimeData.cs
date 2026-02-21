namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Runtime data container for world server instance state.
	/// Tracks world server ID and lock status.
	/// </summary>
	public interface IWorldServerSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Database ID for this world server instance.
		/// </summary>
		long ID { get; set; }

		/// <summary>
		/// Indicates whether the world server is locked (not accepting new connections).
		/// </summary>
		bool IsLocked { get; set; }
	}
}