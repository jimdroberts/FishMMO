namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a player's progress toward a specific achievement, including current tier, value, and template reference.
	/// </summary>
	public class Achievement
	{
		/// <summary>
		/// Persistence version. Advanced by every mutation (<see cref="MarkChanged"/>) and again
		/// by the save snapshot, and compared by <see cref="MarkPersisted"/> so a change made while
		/// a write was in flight keeps the row dirty. Loaded from the database row on login.
		/// </summary>
		/// <remarks>
		/// The mutation-side bump is load-bearing. A version advanced only by the snapshot cannot
		/// guard an in-flight change — the value that moved still carries the exact version the
		/// write is confirming — which is the trap the attribute and ability tables fell into.
		/// </remarks>
		public long Version;

		/// <summary>
		/// Whether this achievement has changed since the database last confirmed it. The save
		/// path skips clean rows.
		/// </summary>
		public bool PersistenceDirty { get; private set; }

		/// <summary>
		/// Records a mutation: dirty, and a new version so a confirmation for the previous state
		/// cannot clear it.
		/// </summary>
		public void MarkChanged()
		{
			PersistenceDirty = true;
			++Version;
		}

		/// <summary>
		/// Records that the database holds the state stamped with <paramref name="persistedVersion"/>.
		/// Clears the dirty mark only if nothing has changed since that snapshot was taken.
		/// </summary>
		/// <param name="persistedVersion">The version the confirmed write carried.</param>
		public void MarkPersisted(long persistedVersion)
		{
			if (Version == persistedVersion)
			{
				PersistenceDirty = false;
			}
		}

		/// <summary>
		/// The current tier of the achievement (0-based index).
		/// </summary>
		public byte CurrentTier;

		/// <summary>
		/// The current value/progress for the achievement (e.g., kills, points).
		/// </summary>
		public uint CurrentValue;

		/// <summary>
		/// The template that defines this achievement's structure, tiers, and rewards.
		/// </summary>
		public AchievementTemplate Template { get; private set; }

		/// <summary>
		/// The value required to reach the next tier, or 0 if at max tier.
		/// </summary>
		/// <remarks>
		/// Returns 0 if there are no tiers or if the current tier is the last one.
		/// </remarks>
		public uint NextTierValue
		{
			get
			{
				// Check if tiers exist and are valid
				if (Template.Tiers == null || Template.Tiers.Count < 1)
				{
					return 0; // No tiers defined
				}

				// If we're already at the last tier, return 0 (no next tier)
				if (CurrentTier >= Template.Tiers.Count)
				{
					return 0;
				}

				// Return the value of the next tier
				return Template.Tiers[CurrentTier].Value;
			}
		}

		/// <summary>
		/// Creates a new achievement with the given template ID, starting at tier 0 and value 0.
		/// </summary>
		/// <param name="templateID">The template ID for the achievement.</param>
		public Achievement(int templateID)
		{
			Template = AchievementTemplate.Get<AchievementTemplate>(templateID);
			if (Template == null)
			{
				UnityEngine.Debug.LogError($"[Achievement] Template not found for ID {templateID}. Achievement will be non-functional.");
			}
			CurrentTier = 0;
			CurrentValue = 0;
		}

		/// <summary>
		/// Creates a new achievement with the given template ID, tier, and value.
		/// </summary>
		/// <param name="templateID">The template ID for the achievement.</param>
		/// <param name="tier">The current tier (0-based index).</param>
		/// <param name="value">The current value/progress.</param>
		public Achievement(int templateID, byte tier, uint value)
		{
			Template = AchievementTemplate.Get<AchievementTemplate>(templateID);
			CurrentTier = tier;
			CurrentValue = value;
		}
	}
}