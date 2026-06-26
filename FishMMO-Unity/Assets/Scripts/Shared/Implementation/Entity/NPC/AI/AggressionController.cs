using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages an aggression (threat) table for a single NPC. Tracks threat from damage,
	/// healing, resource expenditure, and arbitrary point adjustments. Threat decays over
	/// time. Target selection uses both raw threat points and a vulnerability multiplier
	/// based on the target's current health and mana percentages.
	/// <para>
	/// Plain C# class — one instance per NPC, owned by <see cref="AggressionState"/>.
	/// </para>
	/// </summary>
	public class AggressionController
	{
		/// <summary>Points per 1 damage dealt to the NPC.</summary>
		public float DamageWeight = 1.0f;

		/// <summary>Points per 1 healing witnessed on an enemy of the NPC.</summary>
		public float HealingWeight = 0.6f;

		/// <summary>Points per 1 resource point (mana/stamina) spent casting near the NPC.</summary>
		public float ResourceWeight = 0.4f;

		/// <summary>Flat points added per hit regardless of damage amount.</summary>
		public float HitBonusPoints = 5.0f;

		/// <summary>Points per second each entry decays while no new events arrive.</summary>
		public float DecayRate = 3.0f;

		/// <summary>Seconds after last event before a zero-point entry is removed.</summary>
		public float StaleEntryTimeout = 30.0f;

		/// <summary>Chance (0-1) to pick a secondary target for variety.</summary>
		public float TargetVarietyChance = 0.15f;

		/// <summary>
		/// Multiplier applied to threat points for targets below 30% health.
		/// Makes the AI prefer to finish off wounded enemies.
		/// </summary>
		public float LowHealthThreatMultiplier = 1.5f;

		/// <summary>
		/// Multiplier applied to threat points for targets below 20% mana.
		/// Makes the AI pressure casters who are running out of resources.
		/// </summary>
		public float LowResourceThreatMultiplier = 1.3f;

		/// <summary>Health threshold (0-1) below which LowHealthThreatMultiplier activates.</summary>
		public float LowHealthThreshold = 0.3f;

		/// <summary>Resource threshold (0-1) below which LowResourceThreatMultiplier activates.</summary>
		public float LowResourceThreshold = 0.2f;

		private readonly Dictionary<long, AggressionEntry> table = new Dictionary<long, AggressionEntry>();
		private readonly Stack<AggressionEntry> entryPool = new Stack<AggressionEntry>();
		private readonly List<long> staleKeys = new List<long>();

		/// <summary>The raw aggression table keyed by character ID. Read-only.</summary>
	public IReadOnlyDictionary<long, AggressionEntry> Table => table;
		/// <summary>Number of entries currently tracked in the aggression table.</summary>
	public int Count => table.Count;
		/// <summary>Returns true if any characters are tracked in the table.</summary>
	public bool HasAggression => table.Count > 0;

		/// <summary>
		/// Records damage dealt to this NPC.
		/// </summary>
		public void RecordDamage(long attackerId, int amount)
		{
			AggressionEntry entry = GetOrCreate(attackerId);
			entry.HitCount++;
			entry.TotalDamage += amount;
			entry.Points += amount * DamageWeight + HitBonusPoints;
			entry.LastEventTime = Time.time;
		}

		/// <summary>
		/// Records healing witnessed by this NPC on one of its enemies.
		/// </summary>
		public void RecordHealing(long healerId, int amount)
		{
			AggressionEntry entry = GetOrCreate(healerId);
			entry.TotalHealing += amount;
			entry.Points += amount * HealingWeight;
			entry.LastEventTime = Time.time;
		}

		/// <summary>
		/// Records resource expenditure (mana/stamina) from a character casting near this NPC.
		/// Casters who spend heavily to damage or heal draw additional threat.
		/// </summary>
		public void RecordResourceSpent(long characterId, int amount)
		{
			AggressionEntry entry = GetOrCreate(characterId);
			entry.TotalResourceSpent += amount;
			entry.Points += amount * ResourceWeight;
			entry.LastEventTime = Time.time;
		}

		/// <summary>
		/// Adds arbitrary points (positive for taunt, negative for de-aggro).
		/// </summary>
		public void AddPoints(long characterId, float points)
		{
			AggressionEntry entry = GetOrCreate(characterId);
			entry.Points += points;
			if (entry.Points < 0f) entry.Points = 0f;
			entry.LastEventTime = Time.time;
		}

		/// <summary>
		/// Returns the aggression entry for a character, or null if not tracked.
		/// </summary>
		public AggressionEntry GetEntry(long characterId)
		{
			table.TryGetValue(characterId, out AggressionEntry entry);
			return entry;
		}

		/// <summary>
		/// Returns raw threat points for a character, or 0 if not tracked.
		/// </summary>
		public float GetPoints(long characterId)
		{
			if (table.TryGetValue(characterId, out AggressionEntry entry))
				return entry.Points;
			return 0f;
		}

		/// <summary>
		/// Computes a threat score for a character, factoring in vulnerability.
		/// Low health and low mana targets get a multiplier so the AI finishes weak enemies
		/// and pressures casters running out of resources.
		/// </summary>
		public float GetThreatScore(long characterId, ICharacter character)
		{
			float points = GetPoints(characterId);
			if (points <= 0f || character == null) return points;

			// Apply vulnerability multipliers.
			if (character.TryGet(out ICharacterAttributeController attrs))
			{
				if (attrs.TryGetHealthAttribute(out CharacterResourceAttribute health) && health.FinalValue > 0)
				{
					float healthPct = health.CurrentValue / health.FinalValue;
					if (healthPct < LowHealthThreshold)
						points *= LowHealthThreatMultiplier;
				}

				if (attrs.TryGetManaAttribute(out CharacterResourceAttribute mana) && mana.FinalValue > 0)
				{
					float manaPct = mana.CurrentValue / mana.FinalValue;
					if (manaPct < LowResourceThreshold)
						points *= LowResourceThreatMultiplier;
				}
			}

			return points;
		}

		/// <summary>
		/// Decays all entries and removes stale ones.
		/// </summary>
		public void Tick(float deltaTime)
		{
			float now = Time.time;
			float decay = DecayRate * deltaTime;
			staleKeys.Clear();

			foreach (var kvp in table)
			{
				AggressionEntry entry = kvp.Value;
				entry.Points -= decay;
				if (entry.Points < 0f) entry.Points = 0f;
				if (entry.Points <= 0f && (now - entry.LastEventTime) > StaleEntryTimeout)
					staleKeys.Add(kvp.Key);
			}

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
		/// Selects the best target from candidates using threat scoring with vulnerability.
		/// </summary>
		public ICharacter PickTarget(List<ICharacter> candidates, DeterministicRNG rng = null)
		{
			if (candidates == null || candidates.Count == 0) return null;

			ICharacter bestTarget = null;
			float bestScore = -1f;
			ICharacter secondTarget = null;
			float secondScore = -1f;

			for (int i = 0; i < candidates.Count; i++)
			{
				ICharacter c = candidates[i];
				if (c == null || !c.GameObject.activeSelf) continue;
				if (!c.TryGet(out ICharacterDamageController dmg) || !dmg.IsAlive) continue;

				float score = GetThreatScore(c.ID, c);

				if (score > bestScore)
				{
					secondTarget = bestTarget;
					secondScore = bestScore;
					bestTarget = c;
					bestScore = score;
				}
				else if (score > secondScore)
				{
					secondTarget = c;
					secondScore = score;
				}
			}

			float roll = (rng ?? DeterministicRNG.Shared).NextFloat();
			if (secondTarget != null && roll < TargetVarietyChance)
				return secondTarget;

			return bestTarget;
		}

		/// <summary>
		/// Returns true if candidate should replace current target based on threat delta.
		/// </summary>
		public bool ShouldSwitchTarget(long currentId, long candidateId, float threshold = 50f)
		{
			return GetPoints(candidateId) > GetPoints(currentId) + threshold;
		}

		/// <summary>
		/// Clears all entries and returns them to the pool.
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

		private AggressionEntry GetOrCreate(long characterId)
		{
			if (!table.TryGetValue(characterId, out AggressionEntry entry))
			{
				entry = entryPool.Count > 0 ? entryPool.Pop() : new AggressionEntry();
				entry.Reset();
				table[characterId] = entry;
			}
			return entry;
		}
	}
}
