using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Character Attribute Database", menuName = "FishMMO/Character/Attribute/Database", order = 0)]
	public class CharacterAttributeTemplateDatabase : ScriptableObject
	{
		public List<CharacterAttributeTemplate> Attributes = new List<CharacterAttributeTemplate>();
	}
}