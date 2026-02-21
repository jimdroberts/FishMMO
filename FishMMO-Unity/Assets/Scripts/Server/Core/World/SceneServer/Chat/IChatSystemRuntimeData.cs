using System;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for chat message synchronization state.
	/// Tracks database polling position for chat message pump.
	/// </summary>
	public interface IChatSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Timestamp of the last successful database fetch for chat messages.
		/// </summary>
		DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Position (ID) of the last fetched chat message in the database.
		/// </summary>
		long LastFetchPosition { get; set; }

		/// <summary>
		/// Atomic in-flight flag for the periodic message pump.
		/// 0 = idle, 1 = running.
		/// </summary>
		int MessagePumpInFlight { get; set; }
	}
}