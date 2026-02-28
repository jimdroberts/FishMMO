using FishNet.Connection;
using FishMMO.Server.Core.Account;
using FishMMO.Shared;

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
					// Defensive fallback: create AccountData if it doesn't exist (shouldn't happen).
					connectionAccountData[connection] = new AccountData(accessLevel, null);
				}

				connectionAccounts.Remove(connection);
				connectionAccounts.Add(connection, accountName);

				accountConnections.Remove(accountName);
				accountConnections.Add(accountName, connection);
			}
		}
	}
}