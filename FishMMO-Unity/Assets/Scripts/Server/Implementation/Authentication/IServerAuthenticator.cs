using FishNet.Connection;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Common interface for server authenticators (both SRP-based and token-based).
	/// Provides the Server reference and worker lifecycle methods needed by
	/// <see cref="FishNetNetworkWrapper.AttachLoginAuthenticator"/>.
	/// </summary>
	public interface IServerAuthenticator
	{
		/// <summary>
		/// The server instance providing access to AccountManager and other infrastructure.
		/// </summary>
		IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> Server { get; set; }

		/// <summary>
		/// Initializes bounded channels and starts async workers for processing auth requests.
		/// Called after the Server reference is assigned and infrastructure is ready.
		/// </summary>
		void InitializeWorkers();

		/// <summary>
		/// Gracefully shuts down all async workers and disposes channel resources.
		/// </summary>
		void ShutdownWorkers();
	}
}