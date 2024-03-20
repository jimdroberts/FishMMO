using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Race", menuName = "Character/Race/Race", order = 1)]
	public class RaceTemplate : CachedScriptableObject<RaceTemplate>, ICachedObject
	{
		public GameObject MalePrefab;
		public GameObject FemalePrefab;
		public string Description;
		//public List<CharacterAttributeTemplate> BonusAttributes;

		public string Name { get { return this.name; } }
	}
}