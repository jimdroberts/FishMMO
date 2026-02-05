namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character hotkey data transfer object.
	/// </summary>
	public struct CharacterHotkeyData : IVersioned<CharacterHotkeyData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly byte Type;
		public readonly int Slot;
		public readonly long ReferenceID;

		long IVersioned<CharacterHotkeyData>.Version => Version;

		public CharacterHotkeyData(long id, long characterID, byte type, int slot, long referenceID)
			: this(id, version: 0, characterID, type, slot, referenceID)
		{
		}

		public CharacterHotkeyData(long id, long version, long characterID, byte type, int slot, long referenceID)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			Type = type;
			Slot = slot;
			ReferenceID = referenceID;
		}

		public CharacterHotkeyData WithVersion(long newVersion)
		{
			return new CharacterHotkeyData(ID, newVersion, CharacterID, Type, Slot, ReferenceID);
		}
	}
}