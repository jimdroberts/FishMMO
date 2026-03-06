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
	/// Configuration asset for AI LOD (Level-of-Detail) distance thresholds and update rates.
	/// Assign to <see cref="AIController.LodSettings"/> to override the default values.
	/// <para>
	/// The system uses the NPC's FishNet <c>NetworkObject.Observers</c> to determine how
	/// many players can see the NPC. If no observers exist, the NPC is <see cref="AILodTier.Dormant"/>.
	/// When at least one observer exists, a fast squared-distance check against the nearest
	/// observer determines the tier.
	/// </para>
	/// <para>
	/// <b>Default thresholds:</b>
	/// <list type="table">
	///   <listheader><term>Tier</term><description>Distance / Update Interval</description></listheader>
	///   <item><term>Active</term><description>&lt; 40 m → every frame (stagger only)</description></item>
	///   <item><term>Nearby</term><description>&lt; 100 m → 0.2 s</description></item>
	///   <item><term>Far</term><description>&lt; 300 m → 1.0 s</description></item>
	///   <item><term>Dormant</term><description>≥ 300 m or no observers → suspended</description></item>
	/// </list>
	/// </para>
	/// </summary>
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

		[Header("Frame Stagger Modulus")]
		[Tooltip("Active tier stagger. 1 = every frame, 2 = every other, 3 = every third.")]
		[Min(1)]
		public int ActiveStaggerModulus = 3;

		[Tooltip("Nearby tier stagger modulus.")]
		[Min(1)]
		public int NearbyStaggerModulus = 6;

		[Tooltip("Far tier stagger modulus.")]
		[Min(1)]
		public int FarStaggerModulus = 15;

		[Header("Re-evaluation")]
		[Tooltip("Seconds between LOD tier re-evaluation. Lower = more responsive but more expensive.")]
		public float ReevaluateInterval = 2.0f;

		/// <summary>
		/// Returns the stagger modulus for the given tier.
		/// </summary>
		public int GetStaggerModulus(AILodTier tier)
		{
			switch (tier)
			{
				case AILodTier.Active:  return ActiveStaggerModulus;
				case AILodTier.Nearby:  return NearbyStaggerModulus;
				case AILodTier.Far:     return FarStaggerModulus;
				default:                return int.MaxValue; // Dormant — never update
			}
		}

		/// <summary>
		/// Determines the LOD tier from a squared distance to the nearest observer.
		/// </summary>
		public AILodTier GetTier(float sqrDistance)
		{
			if (sqrDistance <= ActiveDistanceSqr)  return AILodTier.Active;
			if (sqrDistance <= NearbyDistanceSqr)  return AILodTier.Nearby;
			if (sqrDistance <= FarDistanceSqr)     return AILodTier.Far;
			return AILodTier.Dormant;
		}
	}
}