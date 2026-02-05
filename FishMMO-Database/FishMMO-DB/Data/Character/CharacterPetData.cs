using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet data transfer object.
	/// </summary>
	public struct CharacterPetData : IVersioned<CharacterPetData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly List<int> Abilities;
		public readonly bool Spawned;

		long IVersioned<CharacterPetData>.Version => Version;

		public CharacterPetData(long id, long characterID, int templateID, List<int> abilities, bool spawned)
			: this(id, version: 0, characterID, templateID, abilities, spawned)
		{
		}

		public CharacterPetData(long id, long version, long characterID, int templateID, List<int> abilities, bool spawned)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Abilities = abilities;
			Spawned = spawned;
		}

		public CharacterPetData WithVersion(long newVersion)
		{
			return new CharacterPetData(ID, newVersion, CharacterID, TemplateID, Abilities, Spawned);
		}
	}
}