using FishMMO.Shared;
using FishMMO.Server.Core.Account.SRP;

namespace FishMMO.Server.Core.Account
{
	/// <summary>
	/// Holds account-related data for a server session, including access level and SRP authentication data.
	/// </summary>
	public class AccountData
	{
		/// <summary>
		/// Gets the access level of the account.
		/// </summary>
		public AccessLevel AccessLevel { get; private set; }

		/// <summary>
		/// Gets the SRP authentication data for the account.
		/// </summary>
		public ServerSrpData SrpData { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountData"/> class.
		/// </summary>
		/// <param name="accessLevel">The access level of the account.</param>
		/// <param name="srpData">The SRP authentication data for the account.</param>
		public AccountData(AccessLevel accessLevel, ServerSrpData srpData)
		{
			AccessLevel = accessLevel;
			SrpData = srpData;
		}
	}
}