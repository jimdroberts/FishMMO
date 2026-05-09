using System;
using FishMMO.Auth.Core;
using FishMMO.Logging;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Token-specific account manager for World and Scene server authentication.
	/// Extends <see cref="AccountManager{TConnection}"/> with a simplified connection account
	/// creation method that does not require SRP state.
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public class TokenAccountManager<TConnection> : AccountManager<TConnection>, ITokenAccountManager<TConnection>
	{
		private readonly string logPrefix;
		private readonly Func<TConnection, string> getConnectionId;

		/// <summary>
		/// Initializes a new TokenAccountManager.
		/// </summary>
		/// <param name="getConnectionId">Delegate that returns a diagnostic identifier for a connection (e.g., client ID).</param>
		/// <param name="logPrefix">Prefix used in diagnostic log messages.</param>
		public TokenAccountManager(Func<TConnection, string> getConnectionId, string logPrefix = nameof(TokenAccountManager<TConnection>))
		{
			this.getConnectionId = getConnectionId ?? (c => c?.ToString() ?? "null");
			this.logPrefix = logPrefix;
		}

		/// <summary>
		/// Registers account name and access level for a token-authenticated connection.
		/// Updates the existing <see cref="AccountData"/> (created at handshake time) with
		/// the resolved access level. The connection is <b>not</b> untracked from the
		/// unauthenticated timer here — that happens when <see cref="AuthState.Authenticated"/>
		/// is reached via <see cref="AccountManager{TConnection}.TryAdvanceAuthState"/>.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="accessLevel">The access level for the account.</param>
		/// <exception cref="InvalidOperationException">Thrown when no AccountData exists for the connection,
		/// indicating the handshake path was skipped or the connection was purged.</exception>
		public void AddConnectionAccount(TConnection connection, string accountName, AccessLevel accessLevel)
		{
			lock (syncRoot)
			{
				if (connectionAccountData.TryGetValue(connection, out AccountData accountData) && accountData != null)
				{
					accountData.SetAccessLevel(accessLevel);
				}
				else
				{
					// Missing AccountData means the handshake path failed to create it.
					// Creating a fallback here would leave the connection with AuthState.None,
					// bypassing the entire auth state machine and permanently stalling login.
					string connId = getConnectionId(connection);
					_ = Log.Error(logPrefix, $"AddConnectionAccount: no AccountData for connection {connId}. Handshake may have been skipped or purged.");
					throw new InvalidOperationException($"No AccountData found for connection {connId}. Cannot register account '{accountName}' without prior handshake state.");
				}

				connectionAccounts.Remove(connection);
				connectionAccounts.Add(connection, accountName);

				if (accountConnections.TryGetValue(accountName, out TConnection existingConn) && !ReferenceEquals(existingConn, connection))
				{
					string existingId = getConnectionId(existingConn);
					string newId = getConnectionId(connection);
					_ = Log.Warning(logPrefix, $"Replacing existing connection for account '{accountName}' (old={existingId}, new={newId}).");
				}
				accountConnections.Remove(accountName);
				accountConnections.Add(accountName, connection);
			}
		}
	}
}