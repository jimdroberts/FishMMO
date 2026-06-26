using System;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Runtime data container for kick request processing state.
	/// Tracks database polling position for kick request processing.
	/// </summary>
	public interface IKickRequestSystemQueueData : IRuntimeDataContainer
	{
		/// <summary>
		/// Indicates whether a kick request fetch is currently in progress.
		/// </summary>
		bool IsProcessing { get; set; }
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