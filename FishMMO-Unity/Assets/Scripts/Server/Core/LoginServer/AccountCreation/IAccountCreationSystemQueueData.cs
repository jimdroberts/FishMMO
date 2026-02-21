using System.Threading;
using System.Threading.Channels;

namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Runtime queue data for async account creation processing.
	/// Generic over connection type to maintain engine independence.
	/// </summary>
	/// <typeparam name="TConnection">The transport's connection representation.</typeparam>
	public interface IAccountCreationSystemQueueData<TConnection> : IRuntimeDataContainer
	{
		/// <summary>
		/// Channel for queuing account creation requests for async processing.
		/// Bounded channel to prevent memory exhaustion during DoS attacks.
		/// </summary>
		Channel<AccountCreationRequest<TConnection>> RequestChannel { get; }

		/// <summary>
		/// Cancellation token source for shutting down async processing workers.
		/// </summary>
		CancellationTokenSource CancellationTokenSource { get; }

		/// <summary>
		/// Current number of pending requests in the channel.
		/// </summary>
		int PendingCount { get; }
	}
}