using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using SecureRemotePassword;
using FishMMO.Server.Core.Account;
using FishMMO.Server.Core.Account.SRP;
using FishMMO.Shared;
using System.Runtime.CompilerServices;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Provides static methods and mappings for managing account and connection data, including SRP authentication state.
	/// </summary>
	public class AccountManager : IAccountManager<NetworkConnection>
	{
		/// <summary>
		/// Maps network connections to their encryption data.
		/// </summary>
		public readonly Dictionary<NetworkConnection, ConnectionEncryptionData> ConnectionEncryptionDatas = new Dictionary<NetworkConnection, ConnectionEncryptionData>();

		/// <summary>
		/// Maps network connections to account names.
		/// </summary>
		public readonly Dictionary<NetworkConnection, string> ConnectionAccounts = new Dictionary<NetworkConnection, string>();

		/// <summary>
		/// Maps account names to network connections.
		/// </summary>
		public readonly Dictionary<string, NetworkConnection> AccountConnections = new Dictionary<string, NetworkConnection>();

		/// <summary>
		/// Maps network connections to account data.
		/// </summary>
		public readonly Dictionary<NetworkConnection, AccountData> ConnectionAccountData = new Dictionary<NetworkConnection, AccountData>();

		/// <summary>
		/// Adds encryption data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="publicKey">The public key for encryption.</param>
		public void AddConnectionEncryptionData(NetworkConnection connection, byte[] publicKey)
		{
			ConnectionEncryptionDatas[connection] = new ConnectionEncryptionData(publicKey,
																				 CryptoHelper.GenerateKey(32),
																				 CryptoHelper.GenerateKey(16));
		}

		/// <summary>
		/// Gets the encryption data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="encryptionData">The encryption data if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetConnectionEncryptionData(NetworkConnection connection, out ConnectionEncryptionData encryptionData)
		{
			return ConnectionEncryptionDatas.TryGetValue(connection, out encryptionData);
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
			ConnectionAccountData.Remove(connection);

			ServerSrpData srpData = new ServerSrpData(SrpParameters.Create2048<SHA512>(),
													  accountName,
													  publicClientEphemeral,
													  salt,
													  verifier);

			ConnectionAccountData.Add(connection, new AccountData(accessLevel, srpData));

			ConnectionAccounts.Remove(connection);
			ConnectionAccounts.Add(connection, accountName);

			AccountConnections.Remove(accountName);
			AccountConnections.Add(accountName, connection);
		}

		/// <summary>
		/// Removes all account mappings for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		public void RemoveConnectionAccount(NetworkConnection connection)
		{
			if (ConnectionAccounts.TryGetValue(connection, out string accountName))
			{
				ConnectionEncryptionDatas.Remove(connection);
				ConnectionAccountData.Remove(connection);
				ConnectionAccounts.Remove(connection);
				AccountConnections.Remove(accountName);
			}
		}

		/// <summary>
		/// Removes all connection mappings for an account name.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		public void RemoveAccountConnection(string accountName)
		{
			if (AccountConnections.TryGetValue(accountName, out NetworkConnection connection))
			{
				ConnectionEncryptionDatas.Remove(connection);
				ConnectionAccountData.Remove(connection);
				ConnectionAccounts.Remove(connection);
				AccountConnections.Remove(accountName);
			}
		}

		/// <summary>
		/// Gets the account data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountData">The account data if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetConnectionAccountData(NetworkConnection connection, out AccountData accountData)
		{
			return ConnectionAccountData.TryGetValue(connection, out accountData);
		}

		/// <summary>
		/// Gets the account name for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetAccountNameByConnection(NetworkConnection connection, out string accountName)
		{
			return ConnectionAccounts.TryGetValue(connection, out accountName);
		}

		/// <summary>
		/// Gets the network connection for an account name.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="connection">The network connection if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetConnectionByAccountName(string accountName, out NetworkConnection connection)
		{
			return AccountConnections.TryGetValue(accountName, out connection);
		}

		/// <summary>
		/// Attempts to update the SRP state for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="requiredState">The required current SRP state.</param>
		/// <param name="nextState">The next SRP state to set if the current state matches.</param>
		/// <returns><c>true</c> if the state was updated; otherwise, <c>false</c>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryUpdateSrpState(NetworkConnection connection, SrpState requiredState, SrpState nextState)
		{
			return TryUpdateSrpState(connection, requiredState, nextState, null);
		}

		/// <summary>
		/// Attempts to update the SRP state for a connection and invokes a callback on success.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="requiredState">The required current SRP state.</param>
		/// <param name="nextState">The next SRP state to set if the current state matches.</param>
		/// <param name="onSuccess">A callback to invoke if the state is updated; should return true to continue.</param>
		/// <returns><c>true</c> if the state was updated and the callback (if provided) succeeded; otherwise, <c>false</c>.</returns>
		public bool TryUpdateSrpState(NetworkConnection connection, SrpState requiredState, SrpState nextState, Func<AccountData, bool> onSuccess)
		{
			if (!ConnectionAccountData.TryGetValue(connection, out AccountData accountData)
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
}