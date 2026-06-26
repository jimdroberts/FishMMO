namespace FishMMO.Shared
{
	/// <summary>
	/// Tracks accumulated aggression (threat) from a single character toward an NPC.
	/// Points are gained when the character damages the NPC, heals an enemy, spends resources,
	/// or takes other aggressive actions. Points decay over time so stale threats fade.
	/// </summary>
	public class AggressionEntry
	{
		/// <summary>Total accumulated aggression points. Higher = higher-priority target.</summary>
		public float Points;

		/// <summary>Number of times this character has damaged the NPC.</summary>
		public int HitCount;

		/// <summary>Total damage dealt to the NPC by this character.</summary>
		public int TotalDamage;

		/// <summary>Total healing performed on enemies of the NPC.</summary>
		public int TotalHealing;

		/// <summary>Total resource (mana/stamina) spent casting abilities against the NPC.</summary>
		public int TotalResourceSpent;

		/// <summary>Timestamp (Time.time) of the last aggression event.</summary>
		public float LastEventTime;

		/// <summary>
		/// Resets all tracked aggression data to default values.
		/// </summary>
		public void Reset()
		{
			Points = 0f;
			HitCount = 0;
			TotalDamage = 0;
			TotalHealing = 0;
			TotalResourceSpent = 0;
			LastEventTime = 0f;
		}
	}
}
