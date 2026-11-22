using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The guard a held <see cref="AbilityType.Block"/> ability raises: how much it takes off a hit from in front, and what
	/// each hit it blocks costs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Reduces, never negates</b> (Jim, 2026-11-22): <see cref="ReducePercent"/> of a frontal hit is blocked and the rest
	/// lands. Damage with no attacker position behind it (a damage-over-time tick, the weather) is never blocked, and nor is
	/// anything from outside <see cref="ArcDegrees"/>.
	/// </para>
	/// <para>
	/// <b>Each block costs.</b> A blocked hit spends <see cref="CostPerHit"/> plus <see cref="CostPerDamage"/> for each point
	/// it took off, from <see cref="CostResource"/> (stamina). When the pool cannot pay, the guard BREAKS: that hit lands in
	/// full and the block ends, its cooldown starting as on a release. Pure arithmetic in <see cref="ResolveBlocked"/> and
	/// <see cref="ResolveCost"/>, so the rule is pinned without a scene.
	/// </para>
	/// </remarks>
	[Serializable]
	public class AbilityGuard
	{
		/// <summary>Percent taken off a frontal hit (0–100).</summary>
		[Tooltip("Percent taken off each frontal hit (0-100). The rest lands.")]
		[Range(0, 100)]
		public int ReducePercent = 75;

		/// <summary>Total spread of the guarded arc, degrees: 150 covers 75 either side of where the character faces.</summary>
		[Tooltip("Total spread of the guarded arc in degrees. 150 covers 75 degrees either side of forward.")]
		[Range(0f, 360f)]
		public float ArcDegrees = 150f;

		/// <summary>The resource each block spends (stamina). None: blocking is free.</summary>
		[Tooltip("The resource each blocked hit spends (stamina). None: blocking costs nothing.")]
		public CharacterAttributeTemplate CostResource;

		/// <summary>Flat cost of each blocked hit.</summary>
		[Tooltip("Flat cost of each blocked hit.")]
		[Min(0)]
		public int CostPerHit = 5;

		/// <summary>Cost of each point of damage the guard took off.</summary>
		[Tooltip("Cost of each point of damage the guard took off.")]
		[Min(0f)]
		public float CostPerDamage = 0.5f;

		/// <summary>The points of <paramref name="incoming"/> a guard of <paramref name="percent"/>% takes off, rounded down.</summary>
		public static int ResolveBlocked(int incoming, int percent)
		{
			if (incoming <= 0 || percent <= 0)
			{
				return 0;
			}
			int clamped = percent > 100 ? 100 : percent;
			return (int)((long)incoming * clamped / 100L);
		}

		/// <summary>What blocking <paramref name="blocked"/> points costs: the flat cost plus the per-point cost, rounded up.</summary>
		public int ResolveCost(int blocked)
		{
			if (blocked <= 0)
			{
				return 0;
			}
			int perDamage = (int)Math.Ceiling(blocked * (double)Mathf.Max(0f, CostPerDamage));
			return Mathf.Max(0, CostPerHit) + perDamage;
		}
	}
}
