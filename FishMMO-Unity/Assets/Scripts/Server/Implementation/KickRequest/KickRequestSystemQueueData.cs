using System;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Runtime data container for kick request processing state.
	/// Manages kick request database polling state separately from KickRequestSystem logic.
	/// </summary>
	public class KickRequestSystemQueueData : RuntimeDataContainer, IKickRequestSystemQueueData
	{
		/// <summary>
		/// Indicates whether the system is currently processing kick requests to prevent overlapping fetches.
		/// </summary>
		public bool IsProcessing { get; set; } = false;
		
		/// <summary>
		/// Timestamp of the last successful database fetch for kick requests.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Last processed position (ID) in the kick request table.
		/// </summary>
		public long LastPosition { get; set; }

		/// <summary>
		/// Initializes the kick request queue data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IsProcessing = false;
			LastFetchTime = DateTime.UtcNow;
			LastPosition = 0;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the kick request queue state.
		/// </summary>
		public override void Clear()
		{
			IsProcessing = false;
			LastFetchTime = DateTime.UtcNow;
			LastPosition = 0;
		}

		/// <summary>
		/// Deinitializes the kick request queue data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}