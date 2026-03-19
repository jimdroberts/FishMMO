using System;
using FishNet.Connection;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Token-specific account manager for World and Scene server authentication.
	/// Extends <see cref="AccountManager"/> with a simplified connection account
	/// creation method that does not require SRP state.
	/// </summary>
	public class TokenAccountManager : AccountManager, ITokenAccountManager<NetworkConnection>
	{
		/// <summary>
		/// Registers account name and access level for a token-authenticated connection.
		/// Updates the existing <see cref="AccountData"/> (created at handshake time) with
		/// the resolved access level. The connection is <b>not</b> untracked from the
		/// unauthenticated timer here — that happens when <see cref="AuthState.Authenticated"/>
		/// is reached via <see cref="AccountManager.TryAdvanceAuthState"/>.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="accessLevel">The access level for the account.</param>
		public void AddConnectionAccount(NetworkConnection connection, string accountName, AccessLevel accessLevel)
		{
			lock (syncRoot)
			{
				// Update existing AccountData if present (created at handshake time).
				if (connectionAccountData.TryGetValue(connection, out AccountData accountData) && accountData != null)
				{
					accountData.SetAccessLevel(accessLevel);
				}
				else
				{
					// Missing AccountData means the handshake path failed to create it.
					// Creating a fallback here would leave the connection with AuthState.None,
					// bypassing the entire auth state machine and permanently stalling login.
					// Treat this as a hard error — callers must handle the exception.
					UnityEngine.Debug.LogError($"[TokenAccountManager] AddConnectionAccount: no AccountData for clientId={connection.ClientId}. Handshake may have been skipped or purged.");
					throw new InvalidOperationException($"No AccountData found for connection {connection.ClientId}. Cannot register account '{accountName}' without prior handshake state.");
				}

				connectionAccounts.Remove(connection);
				connectionAccounts.Add(connection, accountName);

				// Silently replaces if the same account is already mapped to a different
				// connection (narrow race with online-check). Log for diagnostics.
				if (accountConnections.TryGetValue(accountName, out NetworkConnection existingConn) && existingConn != connection)
				{
					UnityEngine.Debug.LogWarning($"[TokenAccountManager] Replacing existing connection for account '{accountName}' (old clientId={existingConn.ClientId}, new clientId={connection.ClientId}).");
				}
				accountConnections.Remove(accountName);
				accountConnections.Add(accountName, connection);
			}
		}
	}
}