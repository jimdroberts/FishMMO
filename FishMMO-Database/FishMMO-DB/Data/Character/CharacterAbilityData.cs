using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character ability data transfer object.
	/// </summary>
	public struct CharacterAbilityData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly List<int> AbilityEvents;
		public readonly float Cooldown;

		public CharacterAbilityData(long id, long characterID, int templateID, List<int> abilityEvents, float cooldown)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			AbilityEvents = abilityEvents;
			Cooldown = cooldown;
		}
	}
}