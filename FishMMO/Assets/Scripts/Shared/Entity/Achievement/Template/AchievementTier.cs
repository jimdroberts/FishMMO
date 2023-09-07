using System;

[Serializable]
public class AchievementTier
{
	public string Description;
	public uint MaxValue;
	public string TierCompleteMessage;
	//public AudioEvent CompleteSound;
	public BaseItemTemplate[] ItemRewards;
	public BuffTemplate[] BuffRewards;
}