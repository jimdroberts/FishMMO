using UnityEngine;

public abstract class AbilityEvent : CachedScriptableObject<AbilityEvent>
{
	public Texture2D Icon;
	public string Description;
	public float ActivationTime;
	public float Cooldown;
	public float Range;
	public float Speed;
	public AbilityResourceDictionary Resources = new AbilityResourceDictionary();
	public AbilityResourceDictionary Requirements = new AbilityResourceDictionary();

	public string Name { get { return this.name; } }
}