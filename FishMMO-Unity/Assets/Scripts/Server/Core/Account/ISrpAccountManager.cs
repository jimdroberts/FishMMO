using System;
using FishMMO.Shared;

namespace FishMMO.Server.Core.Account
{
	/// <summary>
	/// Extended account manager interface for SRP-based authentication on the LoginServer.
	/// Adds SRP-specific connection account creation with SRP data,
	/// and periodic sweep of stale unauthenticated connections.
	/// Auth state machine methods (<c>TryAdvanceAuthState</c>, <c>HasAuthState</c>) live on
	/// the base <see cref="IAccountManager{TConnection}"/> interface.
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public interface ISrpAccountManager<TConnection> : IAccountManager<TConnection>
	{
		/// <summary>
		/// Populates SRP authentication data on an existing connection's AccountData.
		/// The connection must already have AccountData in <see cref="AuthState.VerifyPending"/>
		/// state (created at handshake time). On success, advances the state to
		/// <see cref="AuthState.WaitingForProof"/>.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="publicClientEphemeral">The public ephemeral value from the client.</param>
		/// <param name="salt">The salt for SRP.</param>
		/// <param name="verifier">The verifier for SRP.</param>
		/// <param name="accessLevel">The access level for the account.</param>
		/// <returns><c>true</c> if SRP data was set and state advanced; <c>false</c> if the
		/// connection was not in the expected state.</returns>
		bool AddConnectionAccount(TConnection connection, string accountName, string publicClientEphemeral, string salt, string verifier, AccessLevel accessLevel);

		/// <summary>
		/// Sweeps and removes stale unauthenticated connection state to bound SRP/encryption memory growth.
		/// Authenticated connections are only untracked from the unauthenticated timer map.
		/// </summary>
		/// <param name="maxUnauthenticatedAge">Maximum allowed age for unauthenticated state.</param>
		/// <param name="isAuthenticated">Connection authentication predicate.</param>
		/// <param name="maxScan">Maximum tracked entries to evaluate this sweep.</param>
		/// <param name="maxRemovals">Maximum stale entries to purge this sweep.</param>
		/// <returns>Number of stale unauthenticated entries purged.</returns>
		int SweepUnauthenticatedConnections(TimeSpan maxUnauthenticatedAge, Func<TConnection, bool> isAuthenticated, int maxScan, int maxRemovals);

		/// <summary>
		/// Clears SRP state for a connection while preserving account mappings and access level.
		/// Calls <see cref="ServerSrpData.Clear"/> to null sensitive string references,
		/// then nulls the SrpData reference itself.
		/// Use this after SRP success to remove sensitive SRP material from memory.
		/// </summary>
		/// <param name="connection">The connection whose SRP state will be cleared.</param>
		void ClearSrpState(TConnection connection);
	}
}