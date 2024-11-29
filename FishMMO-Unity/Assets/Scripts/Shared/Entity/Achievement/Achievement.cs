namespace FishMMO.Shared
{
	public class Achievement
	{
		public byte CurrentTier;
		public uint CurrentValue;

		public AchievementTemplate Template { get; private set; }

		public uint NextTierValue
		{
			get
			{
				// Check if tiers exist and are valid
				if (Template.Tiers == null || Template.Tiers.Count < 1)
				{
					return 0; // Or any default value you prefer
				}

				// If we're already at the last tier, return a default value (no next tier)
				if (CurrentTier >= Template.Tiers.Count)
				{
					return 0; // Or any value that indicates no next tier
				}

				// Return the value of the next tier
				return Template.Tiers[CurrentTier].Value;
			}
		}

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