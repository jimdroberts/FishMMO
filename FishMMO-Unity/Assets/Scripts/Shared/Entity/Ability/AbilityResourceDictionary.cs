using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping character attribute templates to integer values for ability resources.
	/// </summary>
	[Serializable]
	public class AbilityResourceDictionary : SerializableDictionary<CharacterAttributeTemplate, int> { }
}