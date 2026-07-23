using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character ability data transfer object.
	/// </summary>
	public struct CharacterAbilityData : IVersioned<CharacterAbilityData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this ability.</summary>
		public readonly long CharacterID;
		/// <summary>Ability template ID.</summary>
		public readonly int TemplateID;
		/// <summary>List of ability event IDs.</summary>
		public readonly List<int> AbilityEvents;
		/// <summary>Remaining cooldown duration.</summary>
		public readonly float Cooldown;

		long IVersioned<CharacterAbilityData>.Version => Version;

		public CharacterAbilityData(long id, long characterID, int templateID, List<int> abilityEvents, float cooldown)
			: this(id, version: 0, characterID, templateID, abilityEvents, cooldown)
		{
		}

		public CharacterAbilityData(long id, long version, long characterID, int templateID, List<int> abilityEvents, float cooldown)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			AbilityEvents = abilityEvents;
			Cooldown = cooldown;
		}

		public CharacterAbilityData WithVersion(long newVersion)
		{
			return new CharacterAbilityData(ID, newVersion, CharacterID, TemplateID, AbilityEvents, Cooldown);
		}
	}
}