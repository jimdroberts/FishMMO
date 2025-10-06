using FishNet.Connection;
using FishMMO.Server.Core.Account;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Public interface for the server composition root.
	/// This surface represents the instance-level public API of a server MonoBehaviour implementation.
	/// </summary>
	public interface IServer
	{
		/// <summary>
		/// The core server logic instance responsible for login, world, and scene servers and for holding address state.
		/// </summary>
		ICoreServer CoreServer { get; }

		/// <summary>
		/// Provider that exposes the server's local bind address, external/remote address and port resolution.
		/// </summary>
		IServerAddressProvider AddressProvider { get; }

		/// <summary>
		/// Active server configuration used to construct and initialize core server components.
		/// </summary>
		IServerConfiguration Configuration { get; }

		/// <summary>
		/// Exposes server lifecycle events such as login/world/scene initialization and other runtime signals.
		/// </summary>
		IServerEvents ServerEvents { get; }
	}

	/// <summary>
	/// Public interface for the server composition root.
	/// This surface represents the instance-level public API of a server MonoBehaviour implementation.
	/// </summary>
	/// <typeparam name="TNetworkManager">Type of the network manager or wrapper exposed by the implementation (for example a network wrapper interface).</typeparam>
	/// <typeparam name="TConnection">Type used to represent transport connections (for example <see cref="NetworkConnection"/> for FishNet).</typeparam>
	/// <typeparam name="TServerBehaviour">Type used to represent server behaviours (for example <see cref="FishMMO.Server.Implementation.ServerBehaviour"/>).</typeparam>
	public interface IServer<TNetworkManager, TConnection, TServerBehaviour> : IServer
	{
		/// <summary>
		/// Network manager or wrapper instance used to interact with the transport layer and FishNet abstractions.
		/// </summary>
		TNetworkManager NetworkWrapper { get; }

		/// <summary>
		/// Account manager responsible for account lifecycle, lookups and persistence operations.
		/// The generic connection type corresponds to the transport's connection representation.
		/// </summary>
		IAccountManager<TConnection> AccountManager { get; }

		/// <summary>
		/// Registry that manages all server behaviours.
		/// </summary>
		IServerBehaviourRegistry<TNetworkManager, TConnection, TServerBehaviour> BehaviourRegistry { get; }
	}
}