using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using SecureRemotePassword;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// AccountManager maps connections to usernames and usernames to connections. This is a helper class. Adding a connection should only be done in your authenticator.
	/// </summary>
	public static class AccountManager
	{
		public readonly static Dictionary<NetworkConnection, string> ConnectionAccounts = new Dictionary<NetworkConnection, string>();
		public readonly static Dictionary<string, NetworkConnection> AccountConnections = new Dictionary<string, NetworkConnection>();
		public readonly static Dictionary<NetworkConnection, AccountData> ConnectionAccountData = new Dictionary<NetworkConnection, AccountData>();

		public static void AddConnectionAccount(NetworkConnection connection, string accountName, string publicClientEphemeral, string salt, string verifier, AccessLevel accessLevel)
		{
			ConnectionAccountData.Remove(connection);

			ServerSRPData srpData = new ServerSRPData(SrpParameters.Create2048<SHA512>(),
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

		public static void RemoveConnectionAccount(NetworkConnection connection)
		{
			if (ConnectionAccounts.TryGetValue(connection, out string accountName))
			{
				ConnectionAccountData.Remove(connection);
				ConnectionAccounts.Remove(connection);
				AccountConnections.Remove(accountName);
			}
		}

		public static void RemoveAccountConnection(string accountName)
		{
			if (AccountConnections.TryGetValue(accountName, out NetworkConnection connection))
			{
				ConnectionAccountData.Remove(connection);
				ConnectionAccounts.Remove(connection);
				AccountConnections.Remove(accountName);
			}
		}

		public static bool GetConnectionAccountData(NetworkConnection connection, out AccountData accountData)
		{
			return ConnectionAccountData.TryGetValue(connection, out accountData);
		}

		public static bool GetAccountNameByConnection(NetworkConnection connection, out string accountName)
		{
			return ConnectionAccounts.TryGetValue(connection, out accountName);
		}

		public static bool GetConnectionByAccountName(string accountName, out NetworkConnection connection)
		{
			return AccountConnections.TryGetValue(accountName, out connection);
		}

		public static bool TryUpdateSrpState(NetworkConnection connection, SRPState requiredState, SRPState nextState)
		{
			return TryUpdateSrpState(connection, requiredState, nextState, null);
		}
		public static bool TryUpdateSrpState(NetworkConnection connection, SRPState requiredState, SRPState nextState, Func<AccountData, bool> onSuccess)
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