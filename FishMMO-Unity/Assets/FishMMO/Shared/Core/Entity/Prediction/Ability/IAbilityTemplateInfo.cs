using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What the core reads of an ability template (ability templates are the Abilities module's): character
	/// creation writes a new character's starting abilities, each with the events it is built from.
	/// </summary>
	public interface IAbilityTemplateInfo
	{
		/// <summary>The template's ID.</summary>
		int ID { get; }

		/// <summary>The IDs of every event the ability is built from.</summary>
		List<int> GetAllAbilityEventIDs();
	}
}
