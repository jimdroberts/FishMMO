namespace FishMMO.Shared
{
	/// <summary>
	/// Tracks accumulated aggression (threat) from a single character toward this NPC.
	/// Points are gained when the character damages the NPC, heals an enemy of the NPC,
	/// or takes other aggressive actions. Points decay over time so stale threats fade.
	/// </summary>
	public class AggressionEntry
	{
		/// <summary>
		/// Total accumulated aggression points from this character.
		/// Higher values mean this character is a higher-priority target.
		/// </summary>
		public float Points;

		/// <summary>
		/// Number of times this character has damaged the NPC.
		/// Used as a tiebreaker and for AI awareness of persistent attackers.
		/// </summary>
		public int HitCount;

		/// <summary>
		/// Total damage dealt to the NPC by this character.
		/// Contributes to aggression points and helps identify burst threats.
		/// </summary>
		public int TotalDamage;

		/// <summary>
		/// Total healing performed by this character on enemies of the NPC.
		/// Healing others in combat draws NPC attention toward the healer.
		/// </summary>
		public int TotalHealing;

		/// <summary>
		/// Timestamp (Time.time) of the last aggression event from this character.
		/// Used for decay calculations and staleness checks.
		/// </summary>
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
			LastEventTime = 0f;
		}
	}
}