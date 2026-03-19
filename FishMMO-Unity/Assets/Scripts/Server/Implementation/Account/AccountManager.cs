using FishNet.Connection;
using System;
using System.Collections.Generic;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Server.Core.Collections;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Thread-safe base manager for account and connection data, including encryption state
	/// and the unified <see cref="AuthState"/> machine.
	/// All public methods are synchronized via a shared lock to support concurrent access from
	/// network broadcast handlers and async worker threads.
	/// Subclass <see cref="SrpAccountManager"/> or <see cref="TokenAccountManager"/> for
	/// authentication-method-specific behaviour.
	/// </summary>
	public class AccountManager : IAccountManager<NetworkConnection>
	{
		/// <summary>
		/// Synchronization object for all dictionary access.
		/// Subclasses must acquire this lock before accessing any protected field.
		/// </summary>
		protected readonly object syncRoot = new object();

		/// <summary>
		/// Maps each connection to its encryption data (public key, symmetric key, session prefix, and counters).
		/// </summary>
		protected readonly Dictionary<NetworkConnection, ConnectionEncryptionData> connectionEncryptionEntries = new Dictionary<NetworkConnection, ConnectionEncryptionData>();

		/// <summary>
		/// Maps each connection to its associated account name.
		/// </summary>
		protected readonly Dictionary<NetworkConnection, string> connectionAccounts = new Dictionary<NetworkConnection, string>();

		/// <summary>
		/// Reverse lookup: maps each account name back to its connection.
		/// </summary>
		protected readonly Dictionary<string, NetworkConnection> accountConnections = new Dictionary<string, NetworkConnection>();

		/// <summary>
		/// Maps each connection to its full account data (auth state, access level, and SRP state).
		/// AccountData is created at handshake time with <see cref="AuthState.Handshake"/>.
		/// </summary>
		protected readonly Dictionary<NetworkConnection, AccountData> connectionAccountData = new Dictionary<NetworkConnection, AccountData>();

		/// <summary>
		/// Tracks unauthenticated connections in arrival order for efficient TTL sweeps.
		/// Oldest entries are at the head.
		/// </summary>
		protected readonly ArrivalOrderTracker<NetworkConnection> unauthenticatedTracker = new ArrivalOrderTracker<NetworkConnection>();

		/// <summary>
		/// Registers a connection's X25519 public key and creates initial AccountData with Handshake state.
		/// Directional encryption keys are established later by the handshake handler after
		/// X25519 ECDH key agreement completes.
		/// Uses try-add semantics: if the connection already has encryption data the call
		/// returns <c>false</c> and the existing entry is not modified. This closes the
		/// TOCTOU race where two concurrent handshake packets could both pass the
		/// <see cref="GetConnectionEncryptionData"/> idempotency guard before either writes.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="publicKey">The client's X25519 public key (32 bytes).</param>
		/// <returns><c>true</c> if the entry was added; <c>false</c> if one already existed.</returns>
		public bool TryAddConnectionEncryptionData(NetworkConnection connection, byte[] publicKey)
		{
			var data = new ConnectionEncryptionData(publicKey);
			lock (syncRoot)
			{
				// Atomic check-and-add: reject if encryption data was already set.
				if (connectionEncryptionEntries.ContainsKey(connection))
					return false;

				connectionEncryptionEntries[connection] = data;

				// Create AccountData at Handshake state if it doesn't exist yet.
				// If it already exists (re-handshake), reset it to Handshake.
				if (!connectionAccountData.TryGetValue(connection, out AccountData existing) || existing == null)
				{
					connectionAccountData[connection] = new AccountData();
				}
				else
				{
					// Reset to Handshake for re-handshake (only safe if not past Handshake).
					// If auth is in progress, the handshake gate prevents reaching here.
					existing.Clear();
					existing.AuthState = AuthState.Handshake;
				}

				TrackUnauthenticatedConnection_NoLock(connection);
			}
			return true;
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
				return connectionEncryptionEntries.TryGetValue(connection, out encryptionData);
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
				ClearAndRemoveEncryptionData_NoLock(connection);

				if (connectionAccountData.TryGetValue(connection, out AccountData data) && data != null)
				{
					data.Clear();
				}
				connectionAccountData.Remove(connection);

				UntrackUnauthenticatedConnection_NoLock(connection);

				if (connectionAccounts.TryGetValue(connection, out string accountName))
				{
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
		/// Atomically advances the authentication state for a connection (compare-and-swap).
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="required">The expected current auth state.</param>
		/// <param name="next">The new auth state to set.</param>
		/// <returns><c>true</c> if the state was advanced; otherwise, <c>false</c>.</returns>
		public bool TryAdvanceAuthState(NetworkConnection connection, AuthState required, AuthState next)
		{
			return TryAdvanceAuthState(connection, required, next, null);
		}

		/// <summary>
		/// Atomically advances the authentication state for a connection and invokes a callback.
		/// The callback runs inside the lock and must not block or re-enter the AccountManager.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="required">The expected current auth state.</param>
		/// <param name="next">The new auth state to set.</param>
		/// <param name="onSuccess">Optional callback invoked inside the lock. Must return true to confirm.</param>
		/// <returns><c>true</c> if the state was advanced and the callback (if any) succeeded.</returns>
		public bool TryAdvanceAuthState(NetworkConnection connection, AuthState required, AuthState next, Func<AccountData, bool> onSuccess)
		{
			lock (syncRoot)
			{
				if (!connectionAccountData.TryGetValue(connection, out AccountData accountData)
					|| accountData == null
					|| accountData.AuthState != required)
				{
					return false;
				}

				accountData.AuthState = next;

				if (onSuccess != null && !onSuccess.Invoke(accountData))
				{
					return false;
				}

				// Untrack from unauthenticated set when reaching terminal auth states.
				if (next == AuthState.SrpSuccess || next == AuthState.Authenticated)
				{
					UntrackUnauthenticatedConnection_NoLock(connection);
				}

				return true;
			}
		}

		/// <summary>
		/// Checks whether a connection has the specified authentication state.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="state">The auth state to check for.</param>
		/// <returns><c>true</c> if the connection has exactly the given state; otherwise, <c>false</c>.</returns>
		public bool HasAuthState(NetworkConnection connection, AuthState state)
		{
			lock (syncRoot)
			{
				return connectionAccountData.TryGetValue(connection, out AccountData accountData)
					&& accountData != null
					&& accountData.AuthState == state;
			}
		}

		/// <summary>
		/// Checks whether a connection has progressed beyond <see cref="AuthState.Handshake"/>.
		/// Used by handshake handlers to reject repeated handshakes while auth is in progress.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <returns><c>true</c> if auth is in progress (state &gt; Handshake); otherwise, <c>false</c>.</returns>
		public bool IsAuthInProgress(NetworkConnection connection)
		{
			lock (syncRoot)
			{
				return connectionAccountData.TryGetValue(connection, out AccountData accountData)
					&& accountData != null
					&& accountData.AuthState > AuthState.Handshake;
			}
		}

		/// <summary>
		/// Ensures a connection is tracked in unauthenticated-state timing map.
		/// Must be called inside <see cref="syncRoot"/> lock.
		/// </summary>
		protected void TrackUnauthenticatedConnection_NoLock(NetworkConnection connection)
		{
			if (connection == null)
			{
				return;
			}

			unauthenticatedTracker.TrackIfMissing(connection, DateTime.UtcNow);
		}

		/// <summary>
		/// Removes a connection from unauthenticated tracking structures.
		/// Must be called inside <see cref="syncRoot"/> lock.
		/// </summary>
		protected void UntrackUnauthenticatedConnection_NoLock(NetworkConnection connection)
		{
			if (connection == null)
			{
				return;
			}

			unauthenticatedTracker.Remove(connection);
		}

		/// <summary>
		/// Zeroes and removes encryption data for a single connection.
		/// Must be called inside <see cref="syncRoot"/> lock.
		/// </summary>
		protected void ClearAndRemoveEncryptionData_NoLock(NetworkConnection connection)
		{
			if (connectionEncryptionEntries.TryGetValue(connection, out ConnectionEncryptionData encData))
			{
				encData.Clear();
				connectionEncryptionEntries.Remove(connection);
			}
		}

		/// <summary>
		/// Zeroes all sensitive key material and clears all stored account and connection data.
		/// </summary>
		public void Clear()
		{
			lock (syncRoot)
			{
				foreach (ConnectionEncryptionData encData in connectionEncryptionEntries.Values)
				{
					encData.Clear();
				}
				connectionEncryptionEntries.Clear();
				connectionAccounts.Clear();
				accountConnections.Clear();

				foreach (AccountData accountData in connectionAccountData.Values)
				{
					if (accountData != null)
					{
						accountData.Clear();
					}
				}
				connectionAccountData.Clear();

				unauthenticatedTracker.Clear();
			}
		}
	}
}