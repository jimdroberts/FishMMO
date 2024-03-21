using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Race", menuName = "Character/Race/New Race", order = 1)]
	public class RaceTemplate : CachedScriptableObject<RaceTemplate>, ICachedObject
	{
		public GameObject Prefab;
		public string Description;
		//public List<CharacterAttributeTemplate> BonusAttributes;

		public string Name { get { return Prefab == null ? this.name : Prefab.gameObject.name; } }
	}
}