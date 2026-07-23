using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet data transfer object.
	/// </summary>
	public struct CharacterPetData : IVersioned<CharacterPetData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this pet.</summary>
		public readonly long CharacterID;
		/// <summary>Pet template ID.</summary>
		public readonly int TemplateID;
		/// <summary>List of pet ability template IDs.</summary>
		public readonly List<int> Abilities;
		/// <summary>Whether the pet is currently spawned.</summary>
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