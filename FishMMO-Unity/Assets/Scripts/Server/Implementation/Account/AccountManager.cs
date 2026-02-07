using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using SecureRemotePassword;
using FishMMO.Server.Core.Account;
using FishMMO.Server.Core.Account.SRP;
using FishMMO.Database.Data.Enums;
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

		private readonly Dictionary<NetworkConnection, ConnectionEncryptionData> connectionEncryptionDatas = new Dictionary<NetworkConnection, ConnectionEncryptionData>();
		private readonly Dictionary<NetworkConnection, string> connectionAccounts = new Dictionary<NetworkConnection, string>();
		private readonly Dictionary<string, NetworkConnection> accountConnections = new Dictionary<string, NetworkConnection>();
		private readonly Dictionary<NetworkConnection, AccountData> connectionAccountData = new Dictionary<NetworkConnection, AccountData>();

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
				connectionEncryptionDatas[connection] = data;
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
				return connectionEncryptionDatas.TryGetValue(connection, out encryptionData);
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
				if (connectionAccounts.TryGetValue(connection, out string accountName))
				{
					connectionEncryptionDatas.Remove(connection);
					connectionAccountData.Remove(connection);
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
					connectionEncryptionDatas.Remove(connection);
					connectionAccountData.Remove(connection);
					connectionAccounts.Remove(connection);
					accountConnections.Remove(accountName);
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
				return true;
			}
		}

		/// <summary>
		/// Clears all stored account and connection data.
		/// </summary>
		public void Clear()
		{
			lock (syncRoot)
			{
				connectionEncryptionDatas.Clear();
				connectionAccounts.Clear();
				accountConnections.Clear();
				connectionAccountData.Clear();
			}
		}
	}
}