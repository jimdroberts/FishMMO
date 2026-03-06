using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages the aggression (threat) system for a single NPC. Owns the
	/// <see cref="AggressionController"/> instance, subscribes to global damage/heal/kill
	/// events, and tracks the per-NPC target re-evaluation timer.
	/// <para>
	/// Extracted from <see cref="AIController"/> to keep the controller focused on
	/// navigation and state management. One instance per NPC — plain C# class, not a
	/// MonoBehaviour or ScriptableObject.
	/// </para>
	/// </summary>
	public class AggressionState
	{
		/// <summary>
		/// The owning character whose aggression this state manages.
		/// </summary>
		private readonly ICharacter character;

		/// <summary>
		/// The underlying threat table.
		/// </summary>
		public AggressionController Controller { get; private set; }

		/// <summary>
		/// Per-NPC timer for mid-combat target re-evaluation. Decremented by the
		/// attacking state; reset when re-evaluation fires. Stored here rather than
		/// on the ScriptableObject state to avoid the shared-instance mutable state problem.
		/// </summary>
		public float TargetReevaluationTimer;

		/// <summary>
		/// Creates a new aggression state, initialises the threat table, and subscribes
		/// to the global damage/heal/kill events.
		/// </summary>
		/// <param name="character">The owning NPC character.</param>
		/// <param name="damageWeight">Points per 1 damage taken.</param>
		/// <param name="healingWeight">Points per 1 healing witnessed.</param>
		/// <param name="hitBonus">Flat points per hit.</param>
		/// <param name="decayRate">Point decay per second.</param>
		/// <param name="staleTimeout">Seconds before stale entries are pruned.</param>
		/// <param name="varietyChance">Chance to pick a non-top-threat target.</param>
		public AggressionState(
			ICharacter character,
			float damageWeight,
			float healingWeight,
			float hitBonus,
			float decayRate,
			float staleTimeout,
			float varietyChance)
		{
			this.character = character;

			Controller = new AggressionController()
			{
				DamageWeight = damageWeight,
				HealingWeight = healingWeight,
				HitBonusPoints = hitBonus,
				DecayRate = decayRate,
				StaleEntryTimeout = staleTimeout,
				TargetVarietyChance = varietyChance,
			};

			ICharacterDamageController.OnDamaged += OnCharacterDamaged;
			ICharacterDamageController.OnHealed += OnCharacterHealed;
			ICharacterDamageController.OnKilled += OnCharacterKilled;
		}

		/// <summary>
		/// Unsubscribes from global events and clears the threat table.
		/// Call from <see cref="AIController.OnDestroying"/>.
		/// </summary>
		public void Destroy()
		{
			ICharacterDamageController.OnDamaged -= OnCharacterDamaged;
			ICharacterDamageController.OnHealed -= OnCharacterHealed;
			ICharacterDamageController.OnKilled -= OnCharacterKilled;

			Controller?.Clear();
		}

		/// <summary>
		/// Decays threat entries and prunes stale ones. Call once per frame or per tick.
		/// </summary>
		public void Tick(float deltaTime)
		{
			Controller?.Tick(deltaTime);
		}

		/// <summary>
		/// Clears all aggression data. Call when the NPC resets, leashes, or despawns.
		/// </summary>
		public void Clear()
		{
			Controller?.Clear();
			TargetReevaluationTimer = 0f;
		}

		/// <summary>
		/// Called by the global OnDamaged event. If this NPC is the defender, record aggression
		/// from the attacker so threat-based targeting can prioritize them.
		/// </summary>
		private void OnCharacterDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageAttribute)
		{
			// Only care when *this* NPC is the one being damaged.
			if (defender != character || attacker == null || attacker == character)
				return;

			Controller?.RecordDamage(attacker.ID, amount);
		}

		/// <summary>
		/// Called by the global OnHealed event. If a character heals someone we are in combat with
		/// (or heals themselves while fighting us), record aggression against the healer.
		/// </summary>
		private void OnCharacterHealed(ICharacter healer, ICharacter healed, int amount)
		{
			if (healer == null || healer == character)
				return;

			// Only generate healing aggression if we are currently tracking anyone
			// and the healed character or healer is already on our threat table.
			if (Controller == null || !Controller.HasAggression)
				return;

			bool healedIsTracked = healed != null && Controller.GetPoints(healed.ID) > 0f;
			bool healerIsTracked = Controller.GetPoints(healer.ID) > 0f;

			if (healedIsTracked || healerIsTracked)
			{
				Controller.RecordHealing(healer.ID, amount);
			}
		}

		/// <summary>
		/// Called by the global OnKilled event. If a character on our threat table dies,
		/// remove their entry so we don't keep targeting a corpse.
		/// </summary>
		private void OnCharacterKilled(ICharacter killer, ICharacter victim)
		{
			if (victim == null || Controller == null)
				return;

			// Zero out — will be pruned next tick.
			Controller.AddPoints(victim.ID, -99999f);
		}
	}
}
