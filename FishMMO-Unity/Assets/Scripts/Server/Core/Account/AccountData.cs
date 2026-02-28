using FishMMO.Shared;
using FishMMO.Server.Core.Account.SRP;

namespace FishMMO.Server.Core.Account
{
	/// <summary>
	/// Holds account-related data for a server session, including authentication state,
	/// access level, and optional SRP authentication data.
	/// </summary>
	/// <remarks>
	/// <see cref="AuthState"/> is the single source of truth for a connection's position
	/// in the authentication lifecycle. All transitions are performed atomically under
	/// the AccountManager lock via <c>TryAdvanceAuthState</c>.
	/// </remarks>
	public class AccountData
	{
		/// <summary>
		/// Gets or sets the authentication state for this connection.
		/// Transitions are guarded by AccountManager's <c>syncRoot</c> lock.
		/// </summary>
		public AuthState AuthState { get; set; }

		/// <summary>
		/// Gets the access level of the account.
		/// </summary>
		public AccessLevel AccessLevel { get; private set; }

		/// <summary>
		/// Gets the SRP authentication data for the account.
		/// Null for token-authenticated connections and after SRP material is cleared.
		/// </summary>
		public ServerSrpData SrpData { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountData"/> class at Handshake state.
		/// Used when encryption data is first established, before any auth-specific data is known.
		/// </summary>
		public AccountData()
		{
			AuthState = AuthState.Handshake;
			AccessLevel = AccessLevel.Player;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountData"/> class with SRP data.
		/// </summary>
		/// <param name="accessLevel">The access level of the account.</param>
		/// <param name="srpData">The SRP authentication data for the account.</param>
		public AccountData(AccessLevel accessLevel, ServerSrpData srpData)
		{
			AuthState = AuthState.None;
			AccessLevel = accessLevel;
			SrpData = srpData;
		}

		/// <summary>
		/// Populates SRP authentication data on an existing AccountData.
		/// Called by the verify worker after creating ServerSrpData.
		/// </summary>
		/// <param name="accessLevel">The access level from the database.</param>
		/// <param name="srpData">The SRP session data.</param>
		public void SetSrpData(AccessLevel accessLevel, ServerSrpData srpData)
		{
			AccessLevel = accessLevel;
			SrpData = srpData;
		}

		/// <summary>
		/// Updates the access level. Used by token auth after database lookup.
		/// </summary>
		/// <param name="accessLevel">The access level to set.</param>
		public void SetAccessLevel(AccessLevel accessLevel)
		{
			AccessLevel = accessLevel;
		}

		/// <summary>
		/// Clears the account data, resetting authentication state, access level, and SRP data.
		/// </summary>
		public void Clear()
		{
			AuthState = AuthState.None;
			AccessLevel = AccessLevel.Player;
			if (SrpData != null)
			{
				SrpData.Clear();
				SrpData = null;
			}
		}

		/// <summary>
		/// Clears only the SRP authentication data, preserving the access level and auth state.
		/// Calls <see cref="ServerSrpData.Clear"/> to null sensitive string references
		/// before releasing the SrpData reference itself.
		/// Use this after SRP success to remove sensitive SRP material from memory
		/// without demoting the account's privilege level.
		/// </summary>
		public void ClearSrpData()
		{
			if (SrpData != null)
			{
				SrpData.Clear();
				SrpData = null;
			}
		}
	}
}