using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Data transfer object for a two-factor recovery code record.
	/// </summary>
	public struct TwoFactorRecoveryCodeData
	{
		public readonly long ID;
		public readonly string AccountName;
		public readonly string CodeHash;
		public readonly DateTime? UsedAt;
		public readonly DateTime TimeCreated;

		public TwoFactorRecoveryCodeData(long id, string accountName, string codeHash,
			DateTime? usedAt, DateTime timeCreated)
		{
			ID = id;
			AccountName = accountName;
			CodeHash = codeHash;
			UsedAt = usedAt;
			TimeCreated = timeCreated;
		}
	}
}
