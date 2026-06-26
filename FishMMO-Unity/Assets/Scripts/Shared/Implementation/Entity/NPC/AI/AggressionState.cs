using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages the aggression (threat) system for a single NPC. Owns the
	/// <see cref="AggressionController"/> instance, subscribes to global damage/heal/kill
	/// events, and tracks the per-NPC target re-evaluation timer.
	/// <para>
	/// Extracted from <see cref="AIController"/> to keep the controller focused on
	/// navigation and state management. One instance per NPC — plain C# class.
	/// </para>
	/// <para>
	/// <b>Event-driven combat entry:</b> When the threat table transitions from empty
	/// to non-empty (first damage received), the <see cref="OnCombatInitiated"/> callback
	/// is invoked for immediate combat entry without waiting for the next physics sweep.
	/// </para>
	/// <para>
	/// <b>Replay safety:</b> Event handlers are guarded to ignore damage/heal/kill events
	/// fired during prediction replay. Global combat events are not replay-suppressed
	/// by the damage controller, so we suppress them here.
	/// </para>
	/// </summary>
	public class AggressionState
	{
		private readonly ICharacter character;

		/// <summary>The aggression controller that tracks per-character threat scores and handles target selection.</summary>
	public AggressionController Controller { get; private set; }

		/// <summary>
		/// Per-NPC timer for mid-combat target re-evaluation. Decremented by the
		/// attacking state; reset when re-evaluation fires.
		/// </summary>
		public float TargetReevaluationTimer;

		/// <summary>
		/// Callback invoked when the threat table transitions from empty to non-empty.
		/// </summary>
		public System.Action<ICharacter> OnCombatInitiated;

		/// <summary>
		/// Creates a new aggression state, initialises the threat table, and subscribes
		/// to global damage/heal/kill events.
		/// </summary>
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

			// These global events are NOT replay-suppressed by CharacterDamageController.
			// We guard against duplicate processing during prediction replay below.
			ICharacterDamageController.OnDamaged += OnCharacterDamaged;
			ICharacterDamageController.OnHealed += OnCharacterHealed;
			ICharacterDamageController.OnKilled += OnCharacterKilled;
		}

		/// <summary>
		/// Unsubscribes from global events and clears the threat table.
		/// </summary>
		public void Destroy()
		{
			ICharacterDamageController.OnDamaged -= OnCharacterDamaged;
			ICharacterDamageController.OnHealed -= OnCharacterHealed;
			ICharacterDamageController.OnKilled -= OnCharacterKilled;

			Controller?.Clear();
		}

		/// <summary>
		/// Decays threat entries. Call once per tick or frame.
		/// </summary>
		public void Tick(float deltaTime) => Controller?.Tick(deltaTime);

		/// <summary>
		/// Clears all aggression data.
		/// </summary>
		public void Clear()
		{
			Controller?.Clear();
			TargetReevaluationTimer = 0f;
		}

		/// <summary>
		/// Records resource expenditure threat against a specific character.
		/// Called externally (e.g., from AbilityController when a caster spends mana).
		/// </summary>
		public void RecordResourceSpent(long characterId, int amount)
		{
			if (Controller == null || !Controller.HasAggression) return;
			Controller.RecordResourceSpent(characterId, amount);
		}

		private void OnCharacterDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageAttribute)
		{
			if (defender != character || attacker == null || attacker == character)
				return;
			if (!IsSpawnedAndAuthoritative()) return;

			bool wasEmpty = Controller == null || !Controller.HasAggression;
			Controller?.RecordDamage(attacker.ID, amount);

			if (wasEmpty)
				OnCombatInitiated?.Invoke(attacker);
		}

		private void OnCharacterHealed(ICharacter healer, ICharacter healed, int amount)
		{
			if (healer == null || healer == character) return;
			if (!IsSpawnedAndAuthoritative()) return;
			if (Controller == null || !Controller.HasAggression) return;

			bool healedIsTracked = healed != null && Controller.GetPoints(healed.ID) > 0f;
			bool healerIsTracked = Controller.GetPoints(healer.ID) > 0f;

			if (healedIsTracked || healerIsTracked)
				Controller.RecordHealing(healer.ID, amount);
		}

		private void OnCharacterKilled(ICharacter killer, ICharacter victim)
		{
			if (victim == null || Controller == null) return;
			if (!IsSpawnedAndAuthoritative()) return;

			Controller.AddPoints(victim.ID, -99999f);
		}

		/// <summary>
		/// Returns true if this NPC's character is spawned and server-authoritative.
		/// Guards against processing global combat events during client-side prediction
		/// replay, which would double-count threat.
		/// </summary>
		private bool IsSpawnedAndAuthoritative()
		{
			return character != null
				&& character.IsSpawned
				&& character.NetworkObject != null
				&& character.NetworkObject.IsServerStarted;
		}
	}
}
