namespace FishMMO.Auth.Core
{
	/// <summary>
	/// Extended account manager interface for token-based authentication on World and Scene servers.
	/// Adds a simplified connection account creation method that does not require SRP state.
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public interface ITokenAccountManager<TConnection> : IAccountManager<TConnection>
	{
		/// <summary>
		/// Adds account data and mappings for a token-authenticated connection.
		/// No SRP state is created. Used by token-based authenticators on World/Scene servers.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="accessLevel">The access level for the account.</param>
		void AddConnectionAccount(TConnection connection, string accountName, AccessLevel accessLevel);
	}
}