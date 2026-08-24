using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI Level-of-Detail tiers. Determines how frequently an NPC's brain ticks
	/// based on its proximity to the nearest player observer.
	/// </summary>
	public enum AILodTier
	{
		/// <summary>NPC is in active combat or very close to a player. Full update rate.</summary>
		Active,
		/// <summary>NPC is within medium distance. Reduced update rate.</summary>
		Nearby,
		/// <summary>NPC is far from players. Minimal updates.</summary>
		Far,
		/// <summary>NPC has no observers or is extremely far. AI is suspended.</summary>
		Dormant
	}

	/// <summary>
	/// Distance thresholds and per-tier update intervals for AI level-of-detail.
	/// Assign to <see cref="AIController.LodSettings"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Intervals are counted in <b>AI ticks</b>, not frames. The AI runs on the FishNet
	/// <c>TimeManager</c> at a fixed rate derived from <see cref="AIController.AiTickRate"/>, so an
	/// interval of 3 means "one update every 3 AI ticks" and converts to a wall-clock rate that
	/// does not move when the server's frame rate does.
	/// </para>
	/// <para>
	/// This replaced a frame-count modulus. Under that scheme the real AI rate was whatever the
	/// server's frame rate happened to be divided by the modulus — so a loaded server did not just
	/// render less often, its NPCs also thought, leashed, swept and decayed threat more slowly,
	/// exactly when the load that caused it needed them to behave predictably. Tuning was equally
	/// hopeless, because "modulus 12" meant a different number of updates per second on every
	/// machine.
	/// </para>
	/// <para>
	/// At the default 8 Hz AI tick, intervals of 1 / 3 / 10 / 40 give roughly 8 Hz, 2.7 Hz,
	/// 0.8 Hz and 0.2 Hz.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI LOD Settings", menuName = "FishMMO/Character/NPC/AI/LOD Settings")]
	public class AILodSettings : ScriptableObject
	{
		[Header("Distance Thresholds")]
		[Tooltip("Squared distance for Active tier (e.g., 40² = 1600). NPCs within this range get full updates.")]
		public float ActiveDistanceSqr = 1600f; // 40m

		[Tooltip("Squared distance for Nearby tier (e.g., 100² = 10000).")]
		public float NearbyDistanceSqr = 10000f; // 100m

		[Tooltip("Squared distance for Far tier (e.g., 300² = 90000). Beyond this, NPC is Dormant.")]
		public float FarDistanceSqr = 90000f; // 300m

		[Header("Update Interval (in AI ticks)")]
		[Tooltip("Active tier: AI ticks between updates. 1 = every AI tick. Full pipeline.")]
		[Min(1)]
		public int ActiveTickInterval = 1;

		[Tooltip("Nearby tier: AI ticks between updates. Simplified AI (no behavior tree, no boss script, no sweep).")]
		[Min(1)]
		public int NearbyTickInterval = 3;

		[Tooltip("Far tier: AI ticks between updates. Minimal AI (wander/idle only, no combat).")]
		[Min(1)]
		public int FarTickInterval = 10;

		[Tooltip("Dormant tier: AI ticks between wake-up checks. No AI runs at all in between.")]
		[Min(1)]
		public int DormantTickInterval = 40;

		[Header("Re-evaluation")]
		[Tooltip("Seconds between LOD tier re-evaluation. Lower = more responsive but more expensive.")]
		public float ReevaluateInterval = 2.0f;

		/// <summary>
		/// Returns how many AI ticks pass between updates at the given tier.
		/// </summary>
		/// <param name="tier">The tier to query.</param>
		/// <returns>The interval in AI ticks, never below 1.</returns>
		public int GetTickInterval(AILodTier tier)
		{
			switch (tier)
			{
				case AILodTier.Active: return Mathf.Max(1, ActiveTickInterval);
				case AILodTier.Nearby: return Mathf.Max(1, NearbyTickInterval);
				case AILodTier.Far: return Mathf.Max(1, FarTickInterval);
				default: return Mathf.Max(1, DormantTickInterval);
			}
		}

		/// <summary>
		/// Determines the LOD tier from a squared distance to the nearest observer.
		/// </summary>
		/// <param name="sqrDistance">Squared distance to the nearest player.</param>
		/// <returns>The tier that distance falls into.</returns>
		public AILodTier GetTier(float sqrDistance)
		{
			if (sqrDistance <= ActiveDistanceSqr) return AILodTier.Active;
			if (sqrDistance <= NearbyDistanceSqr) return AILodTier.Nearby;
			if (sqrDistance <= FarDistanceSqr) return AILodTier.Far;
			return AILodTier.Dormant;
		}

		/// <summary>
		/// Converts a tier's interval into the wall-clock rate it produces at a given AI tick rate.
		/// </summary>
		/// <remarks>
		/// Editor and diagnostics only — it exists so a designer can see what a given interval
		/// actually means in hertz rather than having to work it out.
		/// </remarks>
		/// <param name="tier">The tier to query.</param>
		/// <param name="aiTickRate">The AI tick rate in hertz.</param>
		/// <returns>Updates per second at that tier.</returns>
		public float GetTierHertz(AILodTier tier, float aiTickRate)
		{
			return aiTickRate / GetTickInterval(tier);
		}

		/// <summary>
		/// Clamps the thresholds and intervals into a usable order.
		/// </summary>
		private void OnValidate()
		{
			if (NearbyDistanceSqr < ActiveDistanceSqr) NearbyDistanceSqr = ActiveDistanceSqr;
			if (FarDistanceSqr < NearbyDistanceSqr) FarDistanceSqr = NearbyDistanceSqr;

			if (ActiveTickInterval < 1) ActiveTickInterval = 1;
			if (NearbyTickInterval < ActiveTickInterval) NearbyTickInterval = ActiveTickInterval;
			if (FarTickInterval < NearbyTickInterval) FarTickInterval = NearbyTickInterval;
			if (DormantTickInterval < FarTickInterval) DormantTickInterval = FarTickInterval;

			if (ReevaluateInterval <= 0f) ReevaluateInterval = 1f;
		}
	}
}
