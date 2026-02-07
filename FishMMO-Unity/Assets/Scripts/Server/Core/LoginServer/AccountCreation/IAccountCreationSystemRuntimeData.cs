using System.Threading;
using System.Threading.Tasks;

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
		long TotalProcessed { get; set; }

		/// <summary>
		/// Total number of rejected account creation requests (rate limited, queue full, validation failed) since server start.
		/// </summary>
		long TotalRejected { get; set; }

		/// <summary>
		/// Total number of failed account creations due to errors (database errors, exceptions, etc.) since server start.
		/// </summary>
		long TotalFailed { get; set; }

		/// <summary>
		/// Tracked worker tasks for health monitoring and respawning.
		/// </summary>
		Task[] WorkerTasks { get; set; }

		/// <summary>
		/// Cancellation token used by all workers. Cached from QueueData on startup.
		/// </summary>
		CancellationToken WorkerCancellationToken { get; set; }

		/// <summary>
		/// Timer accumulator for periodic mapping data cleanup.
		/// </summary>
		float CleanupTimer { get; set; }
	}
}
