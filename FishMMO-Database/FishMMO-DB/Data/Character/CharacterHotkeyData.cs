namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character hotkey data transfer object.
	/// </summary>
	public struct CharacterHotkeyData : IVersioned<CharacterHotkeyData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this hotkey.</summary>
		public readonly long CharacterID;
		/// <summary>Hotkey type identifier.</summary>
		public readonly byte Type;
		/// <summary>Hotkey slot index.</summary>
		public readonly int Slot;
		/// <summary>Referenced ability or item ID.</summary>
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