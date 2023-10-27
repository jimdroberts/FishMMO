using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BuffTemplate : CachedScriptableObject<BuffTemplate>
	{
		public string Description;
		public Texture2D Icon;
		public float Duration;
		public float TickRate;
		public uint UseCount;
		public uint MaxStacks;
		public bool IsPermanent;
		//do we want independant timers on buff stacks?
		public bool IndependantStackTimer;
		public List<BuffAttributeTemplate> BonusAttributes;
		//public AudioEvent OnApplySounds;
		//public AudioEvent OnTickSounds;
		//public AudioEvent OnRemoveSounds;

		public string Name { get { return this.name; } }
		public abstract void OnApply(Buff instance, Character target);
		public abstract void OnTick(Buff instance, Character target);
		public abstract void OnRemove(Buff instance, Character target);

		public abstract void OnApplyStack(Buff stack, Character target);
		public abstract void OnRemoveStack(Buff stack, Character target);
	}
}