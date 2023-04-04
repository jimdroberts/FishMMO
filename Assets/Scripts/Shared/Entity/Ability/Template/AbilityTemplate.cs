using UnityEngine;

public abstract class AbilityTemplate : CachedScriptableObject<AbilityTemplate>
{
	public Texture2D Icon;
	public string Description;
	public bool RequiresTarget;
	public bool IsHoldToActivate;
	public float ActivationTime;
	public float Cooldown;
	public float Range;
	public float Speed;
	public float Price;
	public CharacterAttributeTemplate ActivationSpeedReductionAttribute;
	public CharacterAttributeTemplate CooldownReductionAttribute;
	public AbilityResourceDictionary Resources = new AbilityResourceDictionary();
	public AbilityResourceDictionary Requirements = new AbilityResourceDictionary();

	public string Name { get { return this.name; } }

	public abstract void OnStart(Ability ability, Character self, TargetInfo targetInfo);
	public abstract void OnUpdate(Ability ability, Character self, TargetInfo targetInfo);
	public abstract void OnFinish(Ability ability, Character self, TargetInfo targetInfo);
	public abstract void OnInterrupt(Ability ability, Character self, TargetInfo attacker);
}