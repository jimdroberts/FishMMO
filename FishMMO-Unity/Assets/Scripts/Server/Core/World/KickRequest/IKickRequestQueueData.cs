using System;

namespace FishMMO.Server.Core.World
{
	/// <summary>
	/// Runtime data container for kick request processing state.
	/// Tracks database polling position for kick request processing.
	/// </summary>
	public interface IKickRequestQueueData : IRuntimeDataContainer
	{
		/// <summary>
		/// Timestamp of the last successful database fetch for kick requests.
		/// </summary>
		DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Last processed position (ID) in the kick request table.
		/// </summary>
		long LastPosition { get; set; }
	}
}
