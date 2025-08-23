using System;
using FishMMO.Shared;
using FishMMO.Server.Core.Account.SRP;

namespace FishMMO.Server.Core.Account
{
	/// <summary>
	/// Defines an interface for managing account and connection data, including encryption and SRP authentication state.
	/// This interface is generic and does not depend on any specific network implementation.
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public interface IAccountManager<TConnection>
	{
		/// <summary>
		/// Adds encryption data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="publicKey">The public key for encryption.</param>
		void AddConnectionEncryptionData(TConnection connection, byte[] publicKey);

		/// <summary>
		/// Gets the encryption data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="encryptionData">The encryption data if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		bool GetConnectionEncryptionData(TConnection connection, out ConnectionEncryptionData encryptionData);

		/// <summary>
		/// Adds or updates account data and mappings for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="publicClientEphemeral">The public ephemeral value from the client.</param>
		/// <param name="salt">The salt for SRP.</param>
		/// <param name="verifier">The verifier for SRP.</param>
		/// <param name="accessLevel">The access level for the account.</param>
		void AddConnectionAccount(TConnection connection, string accountName, string publicClientEphemeral, string salt, string verifier, AccessLevel accessLevel);

		/// <summary>
		/// Removes all account mappings for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		void RemoveConnectionAccount(TConnection connection);

		/// <summary>
		/// Removes all connection mappings for an account name.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		void RemoveAccountConnection(string accountName);

		/// <summary>
		/// Gets the account data for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountData">The account data if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		bool GetConnectionAccountData(TConnection connection, out AccountData accountData);

		/// <summary>
		/// Gets the account name for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="accountName">The account name if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		bool GetAccountNameByConnection(TConnection connection, out string accountName);

		/// <summary>
		/// Gets the network connection for an account name.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="connection">The network connection if found.</param>
		/// <returns><c>true</c> if found; otherwise, <c>false</c>.</returns>
		bool GetConnectionByAccountName(string accountName, out TConnection connection);

		/// <summary>
		/// Attempts to update the SRP state for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="requiredState">The required current SRP state.</param>
		/// <param name="nextState">The next SRP state to set if the current state matches.</param>
		/// <returns><c>true</c> if the state was updated; otherwise, <c>false</c>.</returns>
		bool TryUpdateSrpState(TConnection connection, SrpState requiredState, SrpState nextState);

		/// <summary>
		/// Attempts to update the SRP state for a connection and invokes a callback on success.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="requiredState">The required current SRP state.</param>
		/// <param name="nextState">The next SRP state to set if the current state matches.</param>
		/// <param name="onSuccess">A callback to invoke if the state is updated; should return true to continue.</param>
		/// <returns><c>true</c> if the state was updated and the callback (if provided) succeeded; otherwise, <c>false</c>.</returns>
		bool TryUpdateSrpState(TConnection connection, SrpState requiredState, SrpState nextState, Func<AccountData, bool> onSuccess);
	}
}