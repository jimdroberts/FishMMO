namespace FishMMO.Database.Data
{
	/// <summary>
	/// A currency hold, as read back for reconciliation.
	/// </summary>
	public struct CurrencyEscrowData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly long Amount;
		public readonly int Reason;

		public CurrencyEscrowData(long id, long characterID, long amount, int reason)
		{
			ID = id;
			CharacterID = characterID;
			Amount = amount;
			Reason = reason;
		}
	}
}
