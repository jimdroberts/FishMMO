using System.Threading;
using System.Threading.Channels;
using FishNet.Connection;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Runtime queue data container for async account creation processing.
	/// Holds Channel and CancellationTokenSource for background worker management.
	/// </summary>
	public class AccountCreationSystemQueueData : RuntimeDataContainer, IAccountCreationSystemQueueData<NetworkConnection>
	{
		/// <summary>
		/// Bounded channel for queuing account creation requests.
		/// Capacity: 1000, FullMode: DropOldest to prevent memory exhaustion.
		/// </summary>
		public Channel<AccountCreationRequest<NetworkConnection>> RequestChannel { get; private set; }

		/// <summary>
		/// Cancellation token source for shutting down async worker threads.
		/// </summary>
		public CancellationTokenSource CancellationTokenSource { get; private set; }

		/// <summary>
		/// Current number of pending requests in the channel.
		/// </summary>
		public int PendingCount => RequestChannel?.Reader.Count ?? 0;

		/// <summary>
		/// Initializes the queue data container with bounded channel and cancellation token.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			// Create bounded channel with capacity 1000, drops oldest on overflow
			RequestChannel = Channel.CreateBounded<AccountCreationRequest<NetworkConnection>>(new BoundedChannelOptions(1000)
			{
				FullMode = BoundedChannelFullMode.DropOldest,
				SingleReader = false,  // Multiple workers can read
				SingleWriter = false   // Multiple network threads can write
			});

			CancellationTokenSource = new CancellationTokenSource();

			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the queue data. Does not dispose channel/CTS - only on deinitialize.
		/// </summary>
		public override void Clear()
		{
			// Don't dispose Channel/CTS during clear - only on deinitialize
		}

		/// <summary>
		/// Deinitializes the queue data container, disposing resources.
		/// </summary>
		public override void Deinitialize()
		{
			CancellationTokenSource?.Cancel();
			CancellationTokenSource?.Dispose();
			CancellationTokenSource = null;
			RequestChannel = null;
		}
	}
}