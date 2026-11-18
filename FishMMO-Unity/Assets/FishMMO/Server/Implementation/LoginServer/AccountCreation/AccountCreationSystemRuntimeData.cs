using System.Threading;
using FishMMO.Server.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.LoginServer;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Runtime statistics and worker tracking for account creation system.
	/// Tracks performance metrics and worker lifecycle state.
	/// Counter fields use Interlocked for thread-safe increments from async workers.
	/// </summary>
	public class AccountCreationSystemRuntimeData : RuntimeDataContainer, IAccountCreationSystemRuntimeData
	{
		/// <inheritdoc/>
		public LastSeenCacheTracker<int, string> ConnectionIpCache { get; private set; }

		/// <inheritdoc/>
		public LastSeenCacheTracker<int, ConnectionEncryptionData> ConnectionEncryptionCache { get; private set; }

		private long totalProcessed;
		private long totalRejected;
		private long totalFailed;

		/// <summary>
		/// Total number of successfully processed account creations.
		/// </summary>
		public long TotalProcessed => Interlocked.Read(ref totalProcessed);

		/// <summary>
		/// Total number of rejected account creation requests.
		/// </summary>
		public long TotalRejected => Interlocked.Read(ref totalRejected);

		/// <summary>
		/// Total number of failed account creations (errors).
		/// </summary>
		public long TotalFailed => Interlocked.Read(ref totalFailed);

		/// <summary>
		/// Atomically increments the processed counter.
		/// </summary>
		public void IncrementProcessed() => Interlocked.Increment(ref totalProcessed);

		/// <summary>
		/// Atomically increments the rejected counter.
		/// </summary>
		public void IncrementRejected() => Interlocked.Increment(ref totalRejected);

		/// <summary>
		/// Atomically increments the failed counter.
		/// </summary>
		public void IncrementFailed() => Interlocked.Increment(ref totalFailed);

		/// <summary>
		/// Timer accumulator for periodic mapping data cleanup.
		/// </summary>
		public float CleanupTimer { get; set; }

		/// <summary>
		/// Initializes the runtime data container with zero counters.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			ConnectionIpCache = new LastSeenCacheTracker<int, string>();
			ConnectionEncryptionCache = new LastSeenCacheTracker<int, ConnectionEncryptionData>();
			Interlocked.Exchange(ref totalProcessed, 0);
			Interlocked.Exchange(ref totalRejected, 0);
			Interlocked.Exchange(ref totalFailed, 0);
			CleanupTimer = 0f;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all runtime statistics and worker references.
		/// </summary>
		public override void Clear()
		{
			ConnectionIpCache?.Clear();
			ConnectionEncryptionCache?.Clear();
			Interlocked.Exchange(ref totalProcessed, 0);
			Interlocked.Exchange(ref totalRejected, 0);
			Interlocked.Exchange(ref totalFailed, 0);
			CleanupTimer = 0f;
		}

		/// <summary>
		/// Deinitializes the runtime data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			ConnectionIpCache = null;
			ConnectionEncryptionCache = null;
		}
	}
}