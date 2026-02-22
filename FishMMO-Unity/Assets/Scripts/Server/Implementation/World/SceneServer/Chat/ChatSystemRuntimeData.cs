using System;
using System.Threading;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for chat message synchronization state.
	/// Manages chat database polling state separately from ChatSystem logic.
	/// </summary>
	public class ChatSystemRuntimeData : RuntimeDataContainer, IChatSystemRuntimeData
	{
		/// <summary>
		/// Timestamp of the last successful database fetch for chat messages.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Position (ID) of the last fetched chat message in the database.
		/// </summary>
		public long LastFetchPosition { get; set; }

		private int messagePumpInFlight;

		/// <inheritdoc/>
		public bool TryBeginMessagePump()
		{
			return Interlocked.CompareExchange(ref messagePumpInFlight, 1, 0) == 0;
		}

		/// <inheritdoc/>
		public void EndMessagePump()
		{
			Interlocked.Exchange(ref messagePumpInFlight, 0);
		}

		/// <summary>
		/// Initializes the chat message queue data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			LastFetchTime = DateTime.UtcNow;
			LastFetchPosition = 0;
			Interlocked.Exchange(ref messagePumpInFlight, 0);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the chat message queue state.
		/// </summary>
		public override void Clear()
		{
			LastFetchTime = DateTime.UtcNow;
			LastFetchPosition = 0;
			Interlocked.Exchange(ref messagePumpInFlight, 0);
		}

		/// <summary>
		/// Deinitializes the chat message queue data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}