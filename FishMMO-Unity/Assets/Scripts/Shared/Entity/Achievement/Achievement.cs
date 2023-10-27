namespace FishMMO.Shared
{
	public class Achievement
	{
		public byte CurrentTier;
		public uint CurrentValue;

		public AchievementTemplate Template { get; private set; }

		public Achievement(int templateID)
		{
			Template = AchievementTemplate.Get<AchievementTemplate>(templateID);
			CurrentTier = 0;
			CurrentValue = 0;
		}

		public Achievement(int templateID, byte tier, uint value)
		{
			Template = AchievementTemplate.Get<AchievementTemplate>(templateID);
			CurrentTier = tier;
			CurrentValue = value;
		}
	}
}