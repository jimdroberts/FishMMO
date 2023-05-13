using FishNet.Connection;
using System.Collections.Generic;

namespace FishMMO.Server
{
	/// <summary>
	/// AccountManager maps connections to usernames and usernames to connections. This is a helper class. Adding a connection should only be done in your authenticator.
	/// </summary>
	public static class AccountManager
	{
		public static Dictionary<NetworkConnection, string> connectionAccounts = new Dictionary<NetworkConnection, string>();
		public static Dictionary<string, NetworkConnection> accountConnections = new Dictionary<string, NetworkConnection>();

		public static void AddConnectionAccount(NetworkConnection connection, string accountName)
		{
			connectionAccounts.Remove(connection);

			connectionAccounts.Add(connection, accountName);

			accountConnections.Remove(accountName);

			accountConnections.Add(accountName, connection);
		}

		public static void RemoveConnectionAccount(NetworkConnection connection)
		{
			if (connectionAccounts.TryGetValue(connection, out string accountName))
			{
				connectionAccounts.Remove(connection);
				accountConnections.Remove(accountName);
			}
		}

		public static void RemoveAccountConnection(string accountName)
		{
			if (accountConnections.TryGetValue(accountName, out NetworkConnection connection))
			{
				connectionAccounts.Remove(connection);
				accountConnections.Remove(accountName);
			}
		}

		public static bool GetAccountNameByConnection(NetworkConnection connection, out string accountName)
		{
			return connectionAccounts.TryGetValue(connection, out accountName);
		}

		public static bool GetConnectionByAccountName(string accountName, out NetworkConnection connection)
		{
			return accountConnections.TryGetValue(accountName, out connection);
		}
	}
}