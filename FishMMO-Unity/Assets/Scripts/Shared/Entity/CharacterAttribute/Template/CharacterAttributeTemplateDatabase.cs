using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Character Attribute Database", menuName = "FishMMO/Character/Attribute/Database", order = 0)]
	/// <summary>
	/// ScriptableObject database that holds a list of all character attribute templates for the game.
	/// Used to manage and reference available attributes in the editor and at runtime.
	/// </summary>
	public class CharacterAttributeTemplateDatabase : ScriptableObject
	{
		/// <summary>
		/// List of all character attribute templates included in this database.
		/// Populate this list in the Unity editor to make attributes available for characters.
		/// </summary>
		public List<CharacterAttributeTemplate> Attributes = new List<CharacterAttributeTemplate>();
	}
}