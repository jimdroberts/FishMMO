using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Target-picking strategies shared by the attacking states and the behaviour tree.
	/// </summary>
	/// <remarks>
	/// Kept separate from <see cref="AggressionController"/> because these modes deliberately
	/// ignore the threat table — a rampaging beast or a critter that just bites whatever is
	/// nearest has no interest in who has done the most damage to it.
	/// </remarks>
	public static class AITargetSelection
	{
		/// <summary>
		/// Returns true when a candidate is a legal target: present, active, and alive.
		/// </summary>
		/// <param name="candidate">The candidate to test.</param>
		/// <returns>True if the candidate can be attacked.</returns>
		public static bool IsValidTarget(ICharacter candidate)
		{
			if (candidate == null || candidate.GameObject == null || !candidate.GameObject.activeSelf)
				return false;

			return candidate.TryGet(out ICharacterDamageController damageController) && damageController.IsAlive;
		}

		/// <summary>
		/// Picks a uniformly random living candidate using the NPC's seeded RNG.
		/// </summary>
		/// <param name="candidates">Candidates to choose from.</param>
		/// <param name="rng">The NPC's deterministic RNG. Falls back to the shared RNG when null.</param>
		/// <returns>A random valid candidate, or null when none are valid.</returns>
		public static ICharacter PickRandom(List<ICharacter> candidates, DeterministicRNG rng)
		{
			if (candidates == null || candidates.Count == 0)
				return null;

			// Count first so the roll is uniform over *valid* candidates rather than over the
			// whole list — otherwise a list full of corpses mostly rolls "nothing".
			int validCount = 0;
			for (int i = 0; i < candidates.Count; i++)
			{
				if (IsValidTarget(candidates[i]))
					validCount++;
			}

			if (validCount == 0)
				return null;

			int index = (rng ?? DeterministicRNG.Shared).Next(0, validCount);
			for (int i = 0; i < candidates.Count; i++)
			{
				if (!IsValidTarget(candidates[i]))
					continue;

				if (index == 0)
					return candidates[i];

				index--;
			}

			return null;
		}

		/// <summary>
		/// Picks the living candidate with the lowest health fraction.
		/// </summary>
		/// <param name="candidates">Candidates to choose from.</param>
		/// <returns>The most wounded valid candidate, or null when none are valid.</returns>
		public static ICharacter PickWeakest(List<ICharacter> candidates)
		{
			if (candidates == null) return null;

			ICharacter best = null;
			float bestPercent = float.MaxValue;

			for (int i = 0; i < candidates.Count; i++)
			{
				ICharacter candidate = candidates[i];
				if (!IsValidTarget(candidate))
					continue;

				float percent = GetHealthPercent(candidate);
				if (percent < bestPercent)
				{
					bestPercent = percent;
					best = candidate;
				}
			}

			return best;
		}

		/// <summary>
		/// Picks the living candidate physically closest to a position.
		/// </summary>
		/// <param name="candidates">Candidates to choose from.</param>
		/// <param name="origin">The position to measure from.</param>
		/// <returns>The nearest valid candidate, or null when none are valid.</returns>
		public static ICharacter PickNearest(List<ICharacter> candidates, Vector3 origin)
		{
			if (candidates == null) return null;

			ICharacter best = null;
			float bestSqrDistance = float.MaxValue;

			for (int i = 0; i < candidates.Count; i++)
			{
				ICharacter candidate = candidates[i];
				if (!IsValidTarget(candidate))
					continue;

				float sqrDistance = (candidate.Transform.position - origin).sqrMagnitude;
				if (sqrDistance < bestSqrDistance)
				{
					bestSqrDistance = sqrDistance;
					best = candidate;
				}
			}

			return best;
		}

		/// <summary>
		/// Returns a character's health as a fraction (0-1) of its maximum, or 1 when unknown.
		/// </summary>
		/// <param name="character">The character to measure.</param>
		/// <returns>The health fraction.</returns>
		public static float GetHealthPercent(ICharacter character)
		{
			if (character == null || !character.TryGet(out ICharacterDamageController damageController))
				return 1f;

			CharacterResourceAttribute health = damageController.ResourceInstance;
			if (health == null || health.FinalValue <= 0f)
				return 1f;

			return health.CurrentValue / health.FinalValue;
		}
	}
}
