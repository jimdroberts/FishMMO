using FishNet.Connection;
using System;
using System.Security.Cryptography;
using SecureRemotePassword;
using FishMMO.Server.Core.Account;
using FishMMO.Server.Core.Account.SRP;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// SRP-specific account manager for LoginServer authentication.
	/// Extends <see cref="AccountManager"/> with SRP data population,
	/// connection account creation, and periodic sweep of stale
	/// unauthenticated connections.
	/// Auth state machine methods (<c>TryAdvanceAuthState</c>, <c>HasAuthState</c>)
	/// are inherited from the base <see cref="AccountManager"/>.
	/// </summary>
	public class SrpAccountManager : AccountManager, ISrpAccountManager<NetworkConnection>
	{
		/// <summary>
		/// Populates SRP authentication data on an existing connection's AccountData.
		/// Requires the connection to be in <see cref="AuthState.VerifyPending"/> state
		/// (set by the verify broadcast handler). On success, advances the state to
		/// <see cref="AuthState.WaitingForProof"/> and registers the account name mappings.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="publicClientEphemeral">The public ephemeral value from the client.</param>
		/// <param name="salt">The salt for SRP.</param>
		/// <param name="verifier">The verifier for SRP.</param>
		/// <param name="accessLevel">The access level for the account.</param>
		/// <returns><c>true</c> if SRP data was set and state advanced; <c>false</c> if the
		/// connection was not in the expected state.</returns>
		public bool AddConnectionAccount(NetworkConnection connection, string accountName, string publicClientEphemeral, string salt, string verifier, AccessLevel accessLevel)
		{
			ServerSrpData srpData = new ServerSrpData(SrpParameters.Create2048<SHA512>(),
													  accountName,
													  publicClientEphemeral,
													  salt,
													  verifier);

			lock (syncRoot)
			{
				if (!connectionAccountData.TryGetValue(connection, out AccountData accountData)
					|| accountData == null
					|| accountData.AuthState != AuthState.VerifyPending)
				{
					return false;
				}

				accountData.SetSrpData(accessLevel, srpData);
				accountData.AuthState = AuthState.WaitingForProof;

				connectionAccounts.Remove(connection);
				connectionAccounts.Add(connection, accountName);

				accountConnections.Remove(accountName);
				accountConnections.Add(accountName, connection);

				return true;
			}
		}

		/// <summary>
		/// Sweeps and removes stale unauthenticated connection state to bound SRP/encryption memory growth.
		/// Authenticated connections are only untracked from the unauthenticated timer map.
		/// </summary>
		/// <param name="maxUnauthenticatedAge">Maximum allowed age for unauthenticated state.</param>
		/// <param name="isAuthenticated">Connection authentication predicate.</param>
		/// <param name="maxScan">Maximum tracked entries to evaluate this sweep.</param>
		/// <param name="maxRemovals">Maximum stale entries to purge this sweep.</param>
		/// <returns>Number of stale unauthenticated entries purged.</returns>
		public int SweepUnauthenticatedConnections(TimeSpan maxUnauthenticatedAge, Func<NetworkConnection, bool> isAuthenticated, int maxScan, int maxRemovals)
		{
			if (maxUnauthenticatedAge <= TimeSpan.Zero || maxScan <= 0 || maxRemovals <= 0)
			{
				return 0;
			}

			lock (syncRoot)
			{
				if (unauthenticatedTracker.Count == 0)
				{
					return 0;
				}

				DateTime now = DateTime.UtcNow;
				int scanned = 0;
				int removed = 0;

				while (scanned < maxScan && removed < maxRemovals)
				{
					if (!unauthenticatedTracker.TryPeekOldest(out NetworkConnection connection, out DateTime firstSeenUtc))
					{
						break;
					}

					if (connection == null)
					{
						unauthenticatedTracker.PopOldest(out _, out _);
						scanned++;
						continue;
					}

					bool authenticated = isAuthenticated != null && isAuthenticated(connection);
					if (authenticated)
					{
						UntrackUnauthenticatedConnection_NoLock(connection);
						scanned++;
						continue;
					}

					if ((now - firstSeenUtc) < maxUnauthenticatedAge)
					{
						// Queue is ordered oldest->newest. If head is fresh, the rest are fresh.
						break;
					}

					// Purge stale unauthenticated connection state.
					ClearAndRemoveEncryptionData_NoLock(connection);

					if (connectionAccountData.TryGetValue(connection, out AccountData data) && data != null)
					{
						data.Clear();
					}
					connectionAccountData.Remove(connection);

					if (connectionAccounts.TryGetValue(connection, out string accountName))
					{
						connectionAccounts.Remove(connection);
						accountConnections.Remove(accountName);
					}

					UntrackUnauthenticatedConnection_NoLock(connection);
					scanned++;
					removed++;
				}

				return removed;
			}
		}

		/// <summary>
		/// Clears SRP state for a connection while preserving account mappings and access level.
		/// Calls <see cref="ServerSrpData.Clear"/> to null sensitive string references,
		/// then nulls the SrpData reference itself via <see cref="AccountData.ClearSrpData"/>.
		/// Use this after SRP success to remove sensitive SRP material from memory.
		/// </summary>
		/// <param name="connection">The connection whose SRP state will be cleared.</param>
		public void ClearSrpState(NetworkConnection connection)
		{
			lock (syncRoot)
			{
				if (connectionAccountData.TryGetValue(connection, out AccountData accountData) && accountData != null)
				{
					accountData.ClearSrpData();
				}
			}
		}
	}
}