namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character party membership data transfer object.
	/// </summary>
	public struct CharacterPartyData : IVersioned<CharacterPartyData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly long PartyID;
		public readonly byte Rank;
		public readonly float HealthPCT;
		/// <summary>The kit of the mercenary this member has seated in the party, or 0 for none.</summary>
		public readonly int MercenaryKitID;
		/// <summary>The seated mercenary's name, or empty.</summary>
		public readonly string MercenaryName;

		long IVersioned<CharacterPartyData>.Version => Version;

		public CharacterPartyData(long id, long characterID, long partyID, byte rank, float healthPCT)
			: this(id, version: 0, characterID, partyID, rank, healthPCT)
		{
		}

		public CharacterPartyData(long id, long version, long characterID, long partyID, byte rank, float healthPCT)
			: this(id, version, characterID, partyID, rank, healthPCT, 0, null)
		{
		}

		public CharacterPartyData(long id, long version, long characterID, long partyID, byte rank, float healthPCT, int mercenaryKitID, string mercenaryName)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			PartyID = partyID;
			Rank = rank;
			HealthPCT = healthPCT;
			MercenaryKitID = mercenaryKitID;
			MercenaryName = mercenaryName;
		}

		/// <summary>True when this member has a mercenary seated: the row holds two seats.</summary>
		public bool HasMercenarySeat => MercenaryKitID != 0;

		/// <summary>Seats the rows take: one per member and one per seated mercenary.</summary>
		public static int CountSeats(System.Collections.Generic.IReadOnlyList<CharacterPartyData> rows)
		{
			if (rows == null)
			{
				return 0;
			}
			int seats = rows.Count;
			for (int i = 0; i < rows.Count; ++i)
			{
				if (rows[i].HasMercenarySeat)
				{
					++seats;
				}
			}
			return seats;
		}

		public CharacterPartyData WithVersion(long newVersion)
		{
			return new CharacterPartyData(ID, newVersion, CharacterID, PartyID, Rank, HealthPCT, MercenaryKitID, MercenaryName);
		}
	}
}