using System;
using FishMMO.Shared;

namespace FishMMO.Server.Core.Account
{
	/// <summary>
	/// Base interface for managing account and connection data shared by all server types.
	/// Contains encryption, account lookup, authentication state machine, and lifecycle methods
	/// common to both SRP-based (Login) and token-based (World/Scene) authentication flows.
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public interface IAccountManager<TConnection>
	{
		/// <summary>
		/// Adds encryption data for a connection and creates initial <see cref="AccountData"/>
		/// with <see cref="AuthState.Handshake"/>.
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
		/// Removes all account mappings for a connection.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		void RemoveConnectionAccount(TConnection connection);

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
		/// Atomically advances the authentication state for a connection.
		/// Returns <c>false</c> if the current state does not match <paramref name="required"/>.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="required">The expected current auth state (compare).</param>
		/// <param name="next">The new auth state to set if current matches (swap).</param>
		/// <returns><c>true</c> if the state was advanced; otherwise, <c>false</c>.</returns>
		bool TryAdvanceAuthState(TConnection connection, AuthState required, AuthState next);

		/// <summary>
		/// Atomically advances the authentication state for a connection and invokes
		/// a callback on success. The callback runs inside the lock and must not block
		/// or re-enter the AccountManager.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="required">The expected current auth state (compare).</param>
		/// <param name="next">The new auth state to set if current matches (swap).</param>
		/// <param name="onSuccess">Callback invoked inside the lock if the transition succeeds.
		/// Should return <c>true</c> to confirm the operation.</param>
		/// <returns><c>true</c> if the state was advanced and the callback succeeded; otherwise, <c>false</c>.</returns>
		bool TryAdvanceAuthState(TConnection connection, AuthState required, AuthState next, Func<AccountData, bool> onSuccess);

		/// <summary>
		/// Checks whether a connection has the specified authentication state.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="state">The auth state to check for.</param>
		/// <returns><c>true</c> if the connection has exactly the given state; otherwise, <c>false</c>.</returns>
		bool HasAuthState(TConnection connection, AuthState state);

		/// <summary>
		/// Checks whether a connection has progressed beyond <see cref="AuthState.Handshake"/>
		/// (i.e., an authentication flow is actively in progress).
		/// Used by handshake handlers to reject repeated handshakes during auth.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <returns><c>true</c> if auth is in progress (state &gt; Handshake); otherwise, <c>false</c>.</returns>
		bool IsAuthInProgress(TConnection connection);

		/// <summary>
		/// Zeroes all sensitive key material and clears all stored account and connection data.
		/// </summary>
		void Clear();
	}
}