using FishMMO.Auth.Implementation;
using FishMMO.Server.Core.Collections;

namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Runtime statistics and metrics for account creation system.
	/// Tracks system performance for monitoring and observability.
	/// </summary>
	public interface IAccountCreationSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Total number of successfully processed account creations since server start.
		/// </summary>
		long TotalProcessed { get; }

		/// <summary>
		/// Total number of rejected account creation requests (rate limited, queue full, validation failed) since server start.
		/// </summary>
		long TotalRejected { get; }

		/// <summary>
		/// Total number of failed account creations due to errors (database errors, exceptions, etc.) since server start.
		/// </summary>
		long TotalFailed { get; }

		/// <summary>
		/// Atomically increments the processed counter.
		/// </summary>
		void IncrementProcessed();

		/// <summary>
		/// Atomically increments the rejected counter.
		/// </summary>
		void IncrementRejected();

		/// <summary>
		/// Atomically increments the failed counter.
		/// </summary>
		void IncrementFailed();

		/// <summary>
		/// Timer accumulator for periodic mapping data cleanup.
		/// </summary>
		float CleanupTimer { get; set; }

		/// <summary>
		/// Per-connection IP cache used by account-creation ingress validation.
		/// </summary>
		/// <remarks>
		/// Timed on <c>MonotonicClock.NowSeconds</c> (the tracker's monotonic overloads) by every
		/// reader and writer, the authenticators included. One cache, one clock.
		/// </remarks>
		LastSeenCacheTracker<int, string> ConnectionIpCache { get; }

		/// <summary>
		/// Per-connection encryption-data cache used by account-creation ingress validation.
		/// </summary>
		/// <remarks>Timed on <c>MonotonicClock.NowSeconds</c>, like <see cref="ConnectionIpCache"/>.</remarks>
		LastSeenCacheTracker<int, ConnectionEncryptionData> ConnectionEncryptionCache { get; }
	}
}