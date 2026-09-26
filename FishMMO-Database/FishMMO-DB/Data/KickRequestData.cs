using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Kick request data transfer object.
	/// </summary>
	public struct KickRequestData
	{
		public readonly long ID;
		public readonly string AccountName;
		public readonly DateTime TimeCreated;

		/// <summary>
		/// The kicked account's last successful login, read in the same query as the request, or
		/// null when no account row matches <see cref="AccountName"/>. A login after
		/// <see cref="TimeCreated"/> means the account reconnected and the kick is stale.
		/// </summary>
		public readonly DateTime? AccountLastLogin;

		public KickRequestData(long id, string accountName, DateTime timeCreated, DateTime? accountLastLogin = null)
		{
			ID = id;
			AccountName = accountName;
			TimeCreated = timeCreated;
			AccountLastLogin = accountLastLogin;
		}
	}
}