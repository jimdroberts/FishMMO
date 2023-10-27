using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class AbilityTemplate : CachedScriptableObject<AbilityTemplate>
	{
		public Texture2D Icon;
		public string Description;
		public GameObject Prefab;
		public AbilitySpawnTarget AbilitySpawnTarget;
		public bool RequiresTarget;
		public int HitCount;
		public float ActivationTime;
		public float ActiveTime;
		public float Cooldown;
		public float Range;
		public float Speed;
		public float Price;
		public CharacterAttributeTemplate ActivationSpeedReductionAttribute;
		public CharacterAttributeTemplate CooldownReductionAttribute;
		public AbilityResourceDictionary Resources = new AbilityResourceDictionary();
		public AbilityResourceDictionary Requirements = new AbilityResourceDictionary();

		public string Name { get { return this.name; } }
	}
}