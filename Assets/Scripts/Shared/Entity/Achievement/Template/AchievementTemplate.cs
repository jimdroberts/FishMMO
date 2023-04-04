using System.Collections.Generic;

public abstract class AchievementTemplate : CachedScriptableObject<AchievementTemplate>
{
	public uint InitialValue;
	public uint MaxValue;
	public string Description;
	public List<AchievementTier> Tiers;
	//public AudioEvent OnCompleteSounds;
	public AchievementEventTemplate OnGainValue;
	public AchievementEventTemplate OnComplete;

	public string Name { get { return this.name; } }
}