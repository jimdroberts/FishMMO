using System;
using System.Collections.Generic;
using FishMMO.Auth.Core;
using FishMMO.Auth.Core.Collections;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Thread-safe base manager for account and connection data, including encryption state
	/// and the unified <see cref="AuthState"/> machine.
	/// All public methods are synchronized via a shared lock to support concurrent access from
	/// network broadcast handlers and async worker threads.
	/// Subclass <see cref="SrpAccountManager{TConnection}"/> or <see cref="TokenAccountManager{TConnection}"/>
	/// for authentication-method-specific behaviour.
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public class AccountManager<TConnection> : IAccountManager<TConnection>
	{
		/// <summary>
		/// Synchronization object for all dictionary access.
		/// Subclasses must acquire this lock before accessing any protected field.
		/// </summary>
		protected readonly object syncRoot = new object();

		/// <summary>
		/// Maps each connection to its encryption data (public key, symmetric key, session prefix, and counters).
		/// </summary>
		protected readonly Dictionary<TConnection, ConnectionEncryptionData> connectionEncryptionEntries = new Dictionary<TConnection, ConnectionEncryptionData>();

		/// <summary>
		/// Maps each connection to its associated account name.
		/// </summary>
		protected readonly Dictionary<TConnection, string> connectionAccounts = new Dictionary<TConnection, string>();

		/// <summary>
		/// Reverse lookup: maps each account name back to its connection.
		/// </summary>
		protected readonly Dictionary<string, TConnection> accountConnections = new Dictionary<string, TConnection>();

		/// <summary>
		/// Maps each connection to its full account data (auth state, access level, and SRP state).
		/// AccountData is created at handshake time with <see cref="AuthState.Handshake"/>.
		/// </summary>
		protected readonly Dictionary<TConnection, AccountData> connectionAccountData = new Dictionary<TConnection, AccountData>();

		/// <summary>
		/// Tracks unauthenticated connections in arrival order for efficient TTL sweeps.
		/// Oldest entries are at the head.
		/// </summary>
		protected readonly ArrivalOrderTracker<TConnection> unauthenticatedTracker = new ArrivalOrderTracker<TConnection>();

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
		public bool TryAddConnectionEncryptionData(TConnection connection, byte[] publicKey)
		{
			var data = new ConnectionEncryptionData(publicKey);
			lock (syncRoot)
			{
				if (connectionEncryptionEntries.ContainsKey(connection))
					return false;

				connectionEncryptionEntries[connection] = data;

				if (!connectionAccountData.TryGetValue(connection, out AccountData existing))
				{
					connectionAccountData[connection] = new AccountData();
				}
				else
				{
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
		public bool GetConnectionEncryptionData(TConnection connection, out ConnectionEncryptionData encryptionData)
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
		public void RemoveConnectionAccount(TConnection connection)
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
		public bool GetConnectionAccountData(TConnection connection, out AccountData accountData)
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
		public bool GetAccountNameByConnection(TConnection connection, out string accountName)
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
		public bool GetConnectionByAccountName(string accountName, out TConnection connection)
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
		public bool TryAdvanceAuthState(TConnection connection, AuthState required, AuthState next)
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
		public bool TryAdvanceAuthState(TConnection connection, AuthState required, AuthState next, Func<AccountData, bool>? onSuccess)
		{
			lock (syncRoot)
			{
				if (!connectionAccountData.TryGetValue(connection, out AccountData accountData)
					|| accountData == null
					|| accountData.AuthState != required)
				{
					return false;
				}

				if (onSuccess != null && !onSuccess.Invoke(accountData))
				{
					return false;
				}

				accountData.AuthState = next;

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
		public bool HasAuthState(TConnection connection, AuthState state)
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
		public bool IsAuthInProgress(TConnection connection)
		{
			lock (syncRoot)
			{
				return connectionAccountData.TryGetValue(connection, out AccountData accountData)
					&& accountData != null
					&& accountData.AuthState > AuthState.Handshake;
			}
		}

		/// <summary>
		/// Adds <paramref name="connection"/> to the unauthenticated-state arrival-order tracker
		/// so that stale handshakes can be expired by <see cref="SweepUnauthenticatedConnections"/>.
		/// Must be called inside <see cref="syncRoot"/> lock — callers must ensure they hold the lock.
		/// </summary>
		protected void TrackUnauthenticatedConnection_NoLock(TConnection connection)
		{
			if (connection == null)
			{
				return;
			}

			unauthenticatedTracker.TrackIfMissing(connection, DateTime.UtcNow);
		}

		/// <summary>
		/// Removes <paramref name="connection"/> from the unauthenticated-state tracker.
		/// Call when the connection advances to an authenticated state (SrpSuccess/Authenticated)
		/// or when it is disconnected, to keep the sweep's scan bound tight.
		/// Must be called inside <see cref="syncRoot"/> lock.
		/// </summary>
		protected void UntrackUnauthenticatedConnection_NoLock(TConnection connection)
		{
			if (connection == null)
			{
				return;
			}

			unauthenticatedTracker.Remove(connection);
		}

		/// <summary>
		/// Calls <see cref="ConnectionEncryptionData.Clear"/> on the connection's encryption data,
		/// zeroing AES session keys, then removes the entry from the dictionary.
		/// Use when tearing down connection state (disconnect, sweep, failed auth).
		/// Must be called inside <see cref="syncRoot"/> lock.
		/// </summary>
		protected void ClearAndRemoveEncryptionData_NoLock(TConnection connection)
		{
			if (connectionEncryptionEntries.TryGetValue(connection, out ConnectionEncryptionData encData))
			{
				encData.Clear();
				connectionEncryptionEntries.Remove(connection);
			}
		}

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