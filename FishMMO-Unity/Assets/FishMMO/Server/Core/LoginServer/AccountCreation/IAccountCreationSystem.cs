namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Engine-agnostic public API for account creation system.
	/// Handles async account creation with rate limiting and DoS protection.
	/// </summary>
	/// <typeparam name="TConnection">The transport's connection representation.</typeparam>
	public interface IAccountCreationSystem<TConnection> : IServerBehaviour
	{
		/// <summary>
		/// Attempts to enqueue an account creation request for async processing.
		/// Returns false immediately if rate limited or queue full.
		/// </summary>
		/// <param name="request">Account creation request data containing encrypted credentials.</param>
		/// <returns>True if queued successfully; false if rejected (rate limited, queue full, etc.).</returns>
		bool TryEnqueueAccountCreation(AccountCreationRequest<TConnection> request);

		/// <summary>
		/// Current number of pending account creation requests in the async queue.
		/// </summary>
		int PendingRequestCount { get; }

		/// <summary>
		/// Total number of successfully processed account creations since server start.
		/// </summary>
		long TotalProcessed { get; }

		/// <summary>
		/// Total number of rejected requests (rate limited, queue full, validation failed) since server start.
		/// </summary>
		long TotalRejected { get; }
	}
}