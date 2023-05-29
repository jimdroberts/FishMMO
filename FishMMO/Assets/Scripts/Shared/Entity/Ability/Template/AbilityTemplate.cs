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
	public AbilityEvent OnStartEvent = null;
	public AbilityEvent OnUpdateEvent = null;
	public AbilityHitEvent OnHitEvent = null;
	public AbilityEvent OnFinishEvent = null;
	public AbilityEvent OnInterruptEvent = null;

	public string Name { get { return this.name; } }
}