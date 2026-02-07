using System.Threading;
using System.Threading.Channels;

namespace FishMMO.Server.Core.Authentication
{
	/// <summary>
	/// Runtime queue data for async SRP authentication processing.
	/// Holds bounded channels for SRP verify and proof requests plus a CancellationTokenSource
	/// for graceful worker shutdown.
	/// Generic over connection type to maintain engine independence.
	/// </summary>
	/// <typeparam name="TConnection">The transport's connection representation.</typeparam>
	public interface IAuthenticatorQueueData<TConnection> : IRuntimeDataContainer
	{
		/// <summary>
		/// Bounded channel for queuing SRP verify requests for async processing.
		/// </summary>
		Channel<SrpVerifyRequest<TConnection>> VerifyChannel { get; }

		/// <summary>
		/// Bounded channel for queuing SRP proof requests for async processing.
		/// </summary>
		Channel<SrpProofRequest<TConnection>> ProofChannel { get; }

		/// <summary>
		/// Cancellation token source for shutting down async processing workers.
		/// </summary>
		CancellationTokenSource CancellationTokenSource { get; }
	}
}
