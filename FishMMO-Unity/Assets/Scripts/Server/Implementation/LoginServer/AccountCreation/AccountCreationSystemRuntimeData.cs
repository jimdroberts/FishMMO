using FishMMO.Server.Core;
using FishMMO.Server.Core.Account;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.LoginServer;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Runtime statistics and worker tracking for account creation system.
	/// Tracks performance metrics and worker lifecycle state.
	/// </summary>
	public class AccountCreationSystemRuntimeData : RuntimeDataContainer, IAccountCreationSystemRuntimeData
	{
		/// <inheritdoc/>
		public LastSeenCacheTracker<int, string> ConnectionIpCache { get; private set; }

		/// <inheritdoc/>
		public LastSeenCacheTracker<int, ConnectionEncryptionData> ConnectionEncryptionCache { get; private set; }

		/// <summary>
		/// Total number of successfully processed account creations.
		/// </summary>
		public long TotalProcessed { get; set; }

		/// <summary>
		/// Total number of rejected account creation requests.
		/// </summary>
		public long TotalRejected { get; set; }

		/// <summary>
		/// Total number of failed account creations (errors).
		/// </summary>
		public long TotalFailed { get; set; }

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
			TotalProcessed = 0;
			TotalRejected = 0;
			TotalFailed = 0;
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
			TotalProcessed = 0;
			TotalRejected = 0;
			TotalFailed = 0;
			CleanupTimer = 0f;
		}

		/// <summary>
		/// Deinitializes the runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
			ConnectionIpCache = null;
			ConnectionEncryptionCache = null;
		}
	}
}