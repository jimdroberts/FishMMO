using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Shared;

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
		/// Reusable scratch list for character broadcast iteration, avoiding per-message allocation.
		/// Only used from the main thread.
		/// </summary>
		List<IPlayerCharacter> CharacterBroadcastBuffer { get; }

		/// <summary>
		/// Reusable scratch list for connection broadcast iteration, avoiding per-message allocation.
		/// Only used from the main thread.
		/// </summary>
		List<NetworkConnection> ConnectionBroadcastBuffer { get; }

		/// <summary>
		/// Atomic in-flight flag for the periodic message pump.
		/// 0 = idle, 1 = running.
		/// </summary>
		bool TryBeginMessagePump();
		void EndMessagePump();
	}
}