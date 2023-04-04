using System;

[Serializable]
public class AchievementTier
{
	public double MaxValue;
	public string TierCompleteMessage;
	public BaseItemTemplate[] Rewards;
}