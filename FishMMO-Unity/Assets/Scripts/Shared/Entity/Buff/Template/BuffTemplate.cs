using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Buff", menuName = "Character/Buff/Buff", order = 1)]
	public class BuffTemplate : CachedScriptableObject<BuffTemplate>, ICachedObject
	{
		public GameObject FXPrefab;
		public string Description;
		public Texture2D Icon;
		public float Duration;
		public float TickRate;
		public uint UseCount;
		public uint MaxStacks;
		public bool IsPermanent;
		// is this considered a debuff? debuffs only apply to enemies
		public bool IsDebuff;
		// do we want independant timers on buff stacks?
		public bool IndependantStackTimer;
		public List<BuffAttributeTemplate> BonusAttributes;
		//public AudioClip OnApplySounds;
		//public AudioClip OnTickSounds;
		//public AudioClip OnRemoveSounds;

		public string Name { get { return this.name; } }
	}
}