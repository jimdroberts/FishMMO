using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages an aggression (threat) table for a single NPC. Tracks how much threat each
	/// nearby character has generated through damage, healing allies, or other actions.
	/// The table decays over time so stale threats fade and combat feels dynamic.
	/// <para>
	/// This is a plain C# class owned by <see cref="AIController"/> — one instance per NPC.
	/// It does NOT derive from MonoBehaviour or ScriptableObject to avoid the shared-instance
	/// problem inherent in ScriptableObject AI states.
	/// </para>
	/// </summary>
	public class AggressionController
	{
		/// <summary>
		/// Points awarded per 1 point of damage dealt to the NPC.
		/// </summary>
		public float DamageWeight = 1.0f;

		/// <summary>
		/// Points awarded per 1 point of healing an enemy of the NPC witnesses.
		/// Healing in combat draws attention toward the healer.
		/// </summary>
		public float HealingWeight = 0.6f;

		/// <summary>
		/// Flat aggression points added per hit, regardless of damage amount.
		/// Keeps fast, low-damage attackers on the radar.
		/// </summary>
		public float HitBonusPoints = 5.0f;

		/// <summary>
		/// Points per second that each entry decays while out of combat (no new events).
		/// Prevents permanently sticky threat after combat ends.
		/// </summary>
		public float DecayRate = 3.0f;

		/// <summary>
		/// Time in seconds after the last event before an entry is removed entirely.
		/// Keeps the table clean of stale references.
		/// </summary>
		public float StaleEntryTimeout = 30.0f;

		/// <summary>
		/// Chance (0-1) that the NPC ignores the top-threat target and picks a secondary
		/// one during target selection. Keeps combat from being perfectly deterministic.
		/// </summary>
		public float TargetVarietyChance = 0.15f;

		/// <summary>
		/// The internal aggression table keyed by character ID.
		/// </summary>
		private readonly Dictionary<long, AggressionEntry> table = new Dictionary<long, AggressionEntry>();

		/// <summary>
		/// Pool of reusable <see cref="AggressionEntry"/> instances to avoid allocations.
		/// </summary>
		private readonly Stack<AggressionEntry> entryPool = new Stack<AggressionEntry>();

		/// <summary>
		/// Temporary list used during cleanup to avoid allocating during iteration.
		/// </summary>
		private readonly List<long> staleKeys = new List<long>();

		/// <summary>
		/// The aggression table — read-only access for external queries.
		/// </summary>
		public IReadOnlyDictionary<long, AggressionEntry> Table => table;

		/// <summary>
		/// Records a damage event against the NPC from the specified attacker.
		/// </summary>
		/// <param name="attackerID">The unique ID of the character that dealt damage.</param>
		/// <param name="amount">The amount of damage dealt (after mitigation).</param>
		public void RecordDamage(long attackerID, int amount)
		{
			AggressionEntry entry = GetOrCreate(attackerID);
			entry.HitCount++;
			entry.TotalDamage += amount;
			entry.Points += amount * DamageWeight + HitBonusPoints;
			entry.LastEventTime = Time.time;
		}

		/// <summary>
		/// Records a healing event that the NPC witnessed — a character healed an enemy of
		/// this NPC. Healing in combat draws threat toward the healer.
		/// </summary>
		/// <param name="healerID">The unique ID of the character that performed the heal.</param>
		/// <param name="amount">The amount healed.</param>
		public void RecordHealing(long healerID, int amount)
		{
			AggressionEntry entry = GetOrCreate(healerID);
			entry.TotalHealing += amount;
			entry.Points += amount * HealingWeight;
			entry.LastEventTime = Time.time;
		}

		/// <summary>
		/// Adds arbitrary aggression points for a character (e.g., taunt abilities,
		/// proximity aggro, or any custom threat source).
		/// </summary>
		/// <param name="characterID">The unique ID of the character.</param>
		/// <param name="points">Points to add (can be negative to reduce threat).</param>
		public void AddPoints(long characterID, float points)
		{
			AggressionEntry entry = GetOrCreate(characterID);
			entry.Points += points;
			if (entry.Points < 0f) entry.Points = 0f;
			entry.LastEventTime = Time.time;
		}

		/// <summary>
		/// Returns the aggression entry for a character, or null if not tracked.
		/// </summary>
		public AggressionEntry GetEntry(long characterID)
		{
			table.TryGetValue(characterID, out AggressionEntry entry);
			return entry;
		}

		/// <summary>
		/// Returns the aggression points for a character, or 0 if not tracked.
		/// </summary>
		public float GetPoints(long characterID)
		{
			if (table.TryGetValue(characterID, out AggressionEntry entry))
			{
				return entry.Points;
			}
			return 0f;
		}

		/// <summary>
		/// Decays all entries by <see cref="DecayRate"/> * deltaTime, removes stale entries,
		/// and returns entries with zero points to the pool. Call once per frame or per tick.
		/// </summary>
		/// <param name="deltaTime">Time since last update.</param>
		public void Tick(float deltaTime)
		{
			float now = Time.time;
			float decay = DecayRate * deltaTime;

			staleKeys.Clear();

			foreach (var kvp in table)
			{
				AggressionEntry entry = kvp.Value;

				// Decay points over time.
				entry.Points -= decay;
				if (entry.Points < 0f) entry.Points = 0f;

				// Mark entries that have gone stale for removal.
				if (entry.Points <= 0f && (now - entry.LastEventTime) > StaleEntryTimeout)
				{
					staleKeys.Add(kvp.Key);
				}
			}

			// Remove stale entries and return them to the pool.
			for (int i = 0; i < staleKeys.Count; i++)
			{
				if (table.TryGetValue(staleKeys[i], out AggressionEntry staleEntry))
				{
					table.Remove(staleKeys[i]);
					staleEntry.Reset();
					entryPool.Push(staleEntry);
				}
			}
		}

		/// <summary>
		/// Selects the best target from a list of candidates using aggression weighting.
		/// The highest-threat target is usually chosen, but there is a configurable chance
		/// (<see cref="TargetVarietyChance"/>) to pick a secondary target for variety.
		/// Candidates must be alive and have an active GameObject.
		/// </summary>
		/// <param name="candidates">List of potential target characters.</param>
		/// <returns>The chosen target, or null if no valid candidates exist.</returns>
		public ICharacter PickTarget(List<ICharacter> candidates, DeterministicRNG rng = null)
		{
			if (candidates == null || candidates.Count == 0) return null;

			ICharacter bestTarget = null;
			float bestPoints = -1f;

			ICharacter secondTarget = null;
			float secondPoints = -1f;

			for (int i = 0; i < candidates.Count; i++)
			{
				ICharacter candidate = candidates[i];
				if (candidate == null || !candidate.GameObject.activeSelf)
					continue;
				if (!candidate.TryGet(out ICharacterDamageController dmg) || !dmg.IsAlive)
					continue;

				float points = GetPoints(candidate.ID);

				if (points > bestPoints)
				{
					// Demote current best to second.
					secondTarget = bestTarget;
					secondPoints = bestPoints;

					bestTarget = candidate;
					bestPoints = points;
				}
				else if (points > secondPoints)
				{
					secondTarget = candidate;
					secondPoints = points;
				}
			}

			// Occasionally pick the second-highest threat for variety.
			float roll = (rng ?? DeterministicRNG.Shared).NextFloat();
			if (secondTarget != null && roll < TargetVarietyChance)
			{
				return secondTarget;
			}

			return bestTarget;
		}

		/// <summary>
		/// Returns true if the given character has more aggression than the current target.
		/// Useful for mid-combat target re-evaluation.
		/// </summary>
		/// <param name="currentTargetID">The current target's character ID.</param>
		/// <param name="candidateID">The candidate character's ID.</param>
		/// <param name="threshold">
		/// The candidate must exceed the current target's points by at least this much
		/// to trigger a switch, preventing constant flip-flopping.
		/// </param>
		public bool ShouldSwitchTarget(long currentTargetID, long candidateID, float threshold = 50f)
		{
			float currentPoints = GetPoints(currentTargetID);
			float candidatePoints = GetPoints(candidateID);
			return candidatePoints > currentPoints + threshold;
		}

		/// <summary>
		/// Clears all aggression data and returns entries to the pool.
		/// Call when the NPC resets, leashes, or despawns.
		/// </summary>
		public void Clear()
		{
			foreach (var kvp in table)
			{
				kvp.Value.Reset();
				entryPool.Push(kvp.Value);
			}
			table.Clear();
		}

		/// <summary>
		/// Returns the total number of tracked characters.
		/// </summary>
		public int Count => table.Count;

		/// <summary>
		/// Returns true if any character has aggression toward this NPC.
		/// </summary>
		public bool HasAggression => table.Count > 0;

		/// <summary>
		/// Gets or creates an aggression entry for the given character ID, using the pool when available.
		/// </summary>
		private AggressionEntry GetOrCreate(long characterID)
		{
			if (!table.TryGetValue(characterID, out AggressionEntry entry))
			{
				entry = entryPool.Count > 0 ? entryPool.Pop() : new AggressionEntry();
				entry.Reset();
				table[characterID] = entry;
			}
			return entry;
		}
	}
}