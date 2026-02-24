using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using SecureRemotePassword;
using FishMMO.Server.Core.Account;
using FishMMO.Server.Core.Account.SRP;
using FishMMO.Server.Core.Collections;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Thread-safe manager for account and connection data, including encryption and SRP authentication state.
	/// All public methods are synchronized via a shared lock to support concurrent access from
	/// network broadcast handlers and async worker threads.
	/// </summary>
	public class AccountManager : IAccountManager<NetworkConnection>
	{
		/// <summary>
		/// Synchronization object for all dictionary access.
		/// </summary>
		private readonly object syncRoot = new object();

		/// <summary>
		/// Maps each connection to its encryption data (public key, symmetric key, IV).
		/// </summary>
		private readonly Dictionary<NetworkConnection, ConnectionEncryptionData> connectionEncryptionEntries = new Dictionary<NetworkConnection, ConnectionEncryptionData>();

		/// <summary>
		/// Maps each connection to its associated account name.
		/// </summary>
		private readonly Dictionary<NetworkConnection, string> connectionAccounts = new Dictionary<NetworkConnection, string>();

		/// <summary>
		/// Reverse lookup: maps each account name back to its connection.
		/// </summary>
		private readonly Dictionary<string, NetworkConnection> accountConnections = new Dictionary<string, NetworkConnection>();

		/// <summary>
		/// Maps each connection to its full account data (access level and SRP state).
		/// </summary>
		private readonly Dictionary<NetworkConnection, AccountData> connectionAccountData = new Dictionary<NetworkConnection, AccountData>();

		/// <summary>
		/// Tracks unauthenticated connections in arrival order for efficient TTL sweeps.
		/// Oldest entries are at the head.
		/// </summary>
		private readonly ArrivalOrderTracker<NetworkConnection> unauthenticatedTracker = new ArrivalOrderTracker<NetworkConnection>();

		/// <summary>
		/// Adds encryption data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="publicKey">The public key for encryption.</param>
		public void AddConnectionEncryptionData(NetworkConnection connection, byte[] publicKey)
		{
			var data = new ConnectionEncryptionData(publicKey,
													CryptoHelper.GenerateKey(32),
													CryptoHelper.GenerateKey(16));
			lock (syncRoot)
			{
				connectionEncryptionEntries[connection] = data;
				TrackUnauthenticatedConnection_NoLock(connection);
			}
		}

		/// <summary>
		/// Gets the encryption data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="encryptionData">The encryption data if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		public bool GetConnectionEncryptionData(NetworkConnection connection, out ConnectionEncryptionData encryptionData)
		{
			lock (syncRoot)
			{
				return connectionEncryptionEntries.TryGetValue(connection, out encryptionData);
			}
		}

		/// <summary>
		/// Adds or updates account data and mappings for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="publicClientEphemeral">The public ephemeral value from the client.</param>
		/// <param name="salt">The salt for SRP.</param>
		/// <param name="verifier">The verifier for SRP.</param>
		/// <param name="accessLevel">The access level for the account.</param>
		public void AddConnectionAccount(NetworkConnection connection, string accountName, string publicClientEphemeral, string salt, string verifier, AccessLevel accessLevel)
		{
			ServerSrpData srpData = new ServerSrpData(SrpParameters.Create2048<SHA512>(),
													  accountName,
													  publicClientEphemeral,
													  salt,
													  verifier);

			lock (syncRoot)
			{
				connectionAccountData.Remove(connection);
				connectionAccountData.Add(connection, new AccountData(accessLevel, srpData));

				connectionAccounts.Remove(connection);
				connectionAccounts.Add(connection, accountName);

				accountConnections.Remove(accountName);
				accountConnections.Add(accountName, connection);

				TrackUnauthenticatedConnection_NoLock(connection);
			}
		}

		/// <summary>
		/// Removes all account mappings for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		public void RemoveConnectionAccount(NetworkConnection connection)
		{
			lock (syncRoot)
			{
				connectionEncryptionEntries.Remove(connection);
				connectionAccountData.Remove(connection);
				UntrackUnauthenticatedConnection_NoLock(connection);

				if (connectionAccounts.TryGetValue(connection, out string accountName))
				{
					connectionAccounts.Remove(connection);
					accountConnections.Remove(accountName);
				}
			}
		}

		/// <summary>
		/// Removes all connection mappings for an account name.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		public void RemoveAccountConnection(string accountName)
		{
			lock (syncRoot)
			{
				if (accountConnections.TryGetValue(accountName, out NetworkConnection connection))
				{
					connectionEncryptionEntries.Remove(connection);
					connectionAccountData.Remove(connection);
					connectionAccounts.Remove(connection);
					accountConnections.Remove(accountName);
					UntrackUnauthenticatedConnection_NoLock(connection);
				}
			}
		}

		/// <summary>
		/// Gets the account data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountData">The account data if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		public bool GetConnectionAccountData(NetworkConnection connection, out AccountData accountData)
		{
			lock (syncRoot)
			{
				return connectionAccountData.TryGetValue(connection, out accountData);
			}
		}

		/// <summary>
		/// Gets the account name for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		public bool GetAccountNameByConnection(NetworkConnection connection, out string accountName)
		{
			lock (syncRoot)
			{
				return connectionAccounts.TryGetValue(connection, out accountName);
			}
		}

		/// <summary>
		/// Gets the network connection for an account name.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="connection">The network connection if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		public bool GetConnectionByAccountName(string accountName, out NetworkConnection connection)
		{
			lock (syncRoot)
			{
				return accountConnections.TryGetValue(accountName, out connection);
			}
		}

		/// <summary>
		/// Attempts to update the SRP state for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="requiredState">The required current SRP state.</param>
		/// <param name="nextState">The next SRP state to set if the current state matches.</param>
		/// <returns><c>true</c> if the state was updated; otherwise, <c>false</c>.</returns>
		public bool TryUpdateSrpState(NetworkConnection connection, SrpState requiredState, SrpState nextState)
		{
			return TryUpdateSrpState(connection, requiredState, nextState, null);
		}

		/// <summary>
		/// Attempts to update the SRP state for a connection and invokes a callback on success.
		/// The callback is invoked inside the lock, so it must not block or re-enter the AccountManager.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="requiredState">The required current SRP state.</param>
		/// <param name="nextState">The next SRP state to set if the current state matches.</param>
		/// <param name="onSuccess">A callback to invoke if the state is updated; should return true to continue.</param>
		/// <returns><c>true</c> if the state was updated and the callback (if provided) succeeded; otherwise, <c>false</c>.</returns>
		public bool TryUpdateSrpState(NetworkConnection connection, SrpState requiredState, SrpState nextState, Func<AccountData, bool> onSuccess)
		{
			lock (syncRoot)
			{
				if (!connectionAccountData.TryGetValue(connection, out AccountData accountData)
					|| accountData == null
					|| accountData.SrpData == null
					|| accountData.SrpData.State != requiredState)
				{
					return false;
				}
				accountData.SrpData.State = nextState;
				if (onSuccess != null &&
					!onSuccess.Invoke(accountData))
				{
					return false;
				}

				if (nextState == SrpState.SrpSuccess)
				{
					UntrackUnauthenticatedConnection_NoLock(connection);
				}

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
					connectionEncryptionEntries.Remove(connection);
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
		/// Ensures a connection is tracked in unauthenticated-state timing map.
		/// Must be called inside <see cref="syncRoot"/> lock.
		/// </summary>
		private void TrackUnauthenticatedConnection_NoLock(NetworkConnection connection)
		{
			if (connection == null)
			{
				return;
			}

			unauthenticatedTracker.TrackIfMissing(connection, DateTime.UtcNow);
		}

		/// <summary>
		/// Removes a connection from unauthenticated tracking structures.
		/// Must be called inside <see cref="syncRoot"/> lock.
		/// </summary>
		private void UntrackUnauthenticatedConnection_NoLock(NetworkConnection connection)
		{
			if (connection == null)
			{
				return;
			}

			unauthenticatedTracker.Remove(connection);
		}

		/// <summary>
		/// Clears all stored account and connection data.
		/// </summary>
		public void Clear()
		{
			lock (syncRoot)
			{
				connectionEncryptionEntries.Clear();
				connectionAccounts.Clear();
				accountConnections.Clear();
				connectionAccountData.Clear();
				unauthenticatedTracker.Clear();
			}
		}
	}
}