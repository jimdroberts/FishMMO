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

		/// <summary>The NPC this table belongs to.</summary>
		public ICharacter Character => character;

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
		/// Creates a new aggression state with a threat table at the default weights, and subscribes
		/// to global damage/heal/kill events. Call <see cref="Configure"/> to give it an archetype's
		/// numbers.
		/// </summary>
		/// <param name="character">The NPC this threat table belongs to.</param>
		public AggressionState(ICharacter character)
		{
			this.character = character;

			Controller = new AggressionController();

			/* Registered with the shared dispatcher rather than subscribing to the global events
			 * directly. Per-NPC subscriptions turned every single hit in the scene into one
			 * delegate call per NPC alive, of which at most one was relevant. */
			AggressionDispatcher.Register(character, this);
		}

		/// <summary>
		/// Sets the threat table's weights. Existing entries keep their points; only how future
		/// events are scored and how fast entries decay changes.
		/// </summary>
		/// <remarks>
		/// Separate from the constructor so a controller whose archetype changes after it has been
		/// initialised — a spawner override on a recycled instance — can retune the table it already
		/// has instead of rebuilding it and losing its dispatcher registration.
		/// </remarks>
		/// <param name="damageWeight">Threat per 1 point of damage taken.</param>
		/// <param name="healingWeight">Threat per 1 point of healing witnessed on a combat participant.</param>
		/// <param name="hitBonus">Flat threat added per hit, regardless of damage.</param>
		/// <param name="decayRate">Threat lost per second while no new events arrive.</param>
		/// <param name="staleTimeout">Seconds before a drained threat entry is forgotten.</param>
		/// <param name="varietyChance">Chance target selection picks the second-highest threat.</param>
		public void Configure(
			float damageWeight,
			float healingWeight,
			float hitBonus,
			float decayRate,
			float staleTimeout,
			float varietyChance)
		{
			if (Controller == null)
			{
				return;
			}
			Controller.DamageWeight = damageWeight;
			Controller.HealingWeight = healingWeight;
			Controller.HitBonusPoints = hitBonus;
			Controller.DecayRate = decayRate;
			Controller.StaleEntryTimeout = staleTimeout;
			Controller.TargetVarietyChance = varietyChance;
		}

		/// <summary>
		/// Returns the threat table to <see cref="AggressionController"/>'s own default weights.
		/// Used for an NPC with no archetype.
		/// </summary>
		public void ConfigureDefaults()
		{
			AggressionController defaults = new AggressionController();
			Configure(
				defaults.DamageWeight,
				defaults.HealingWeight,
				defaults.HitBonusPoints,
				defaults.DecayRate,
				defaults.StaleEntryTimeout,
				defaults.TargetVarietyChance);
		}

		/// <summary>
		/// Unsubscribes from global events and clears the threat table.
		/// </summary>
		public void Destroy()
		{
			AggressionDispatcher.Unregister(character);

			Controller?.Clear();
		}

		/// <summary>
		/// True when this NPC is tracking anyone at all.
		/// </summary>
		/// <remarks>
		/// Read by <see cref="AggressionDispatcher"/> to skip uninvolved NPCs without entering a
		/// handler. An out-of-combat NPC is the common case, so this needs to stay a field read.
		/// </remarks>
		public bool HasAggression => Controller != null && Controller.HasAggression;

		/// <summary>
		/// Decays threat entries. Call once per tick or frame.
		/// </summary>
		public void Tick(float deltaTime) => Controller?.Tick(deltaTime);

		/// <summary>
		/// Clears all aggression data.
		/// </summary>
		public void Clear()
		{
			// Reset rather than Clear: a leash or a pool reuse should not leave the table's
			// staleness clock carrying the previous engagement's elapsed time.
			Controller?.Reset();
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

		/// <summary>
		/// Records damage this NPC took. Called by <see cref="AggressionDispatcher"/>, which has
		/// already established that this NPC is the defender.
		/// </summary>
		/// <param name="attacker">The character that dealt the damage.</param>
		/// <param name="amount">Damage dealt.</param>
		public void HandleDamaged(ICharacter attacker, int amount)
		{
			if (attacker == null || attacker == character)
				return;
			if (!IsSpawnedAndAuthoritative()) return;

			bool wasEmpty = Controller == null || !Controller.HasAggression;
			Controller?.RecordDamage(attacker.ID, amount);

			if (wasEmpty)
				OnCombatInitiated?.Invoke(attacker);
		}

		/// <summary>
		/// Records threat for a heal this NPC witnessed on one of its enemies.
		/// </summary>
		/// <param name="healer">The healing character.</param>
		/// <param name="healed">The healed character.</param>
		/// <param name="amount">Amount healed.</param>
		public void HandleHealed(ICharacter healer, ICharacter healed, int amount)
		{
			if (healer == null || healer == character) return;
			if (!IsSpawnedAndAuthoritative()) return;
			if (Controller == null || !Controller.HasAggression) return;

			bool healedIsTracked = healed != null && Controller.GetPoints(healed.ID) > 0f;
			bool healerIsTracked = Controller.GetPoints(healer.ID) > 0f;

			if (healedIsTracked || healerIsTracked)
				Controller.RecordHealing(healer.ID, amount);
		}

		/// <summary>
		/// Forgets a character that has died.
		/// </summary>
		/// <param name="victim">The character that died.</param>
		public void HandleKilled(ICharacter victim)
		{
			if (victim == null || Controller == null) return;
			if (!IsSpawnedAndAuthoritative()) return;

			/* Only drop threat for someone this NPC was actually tracking.
			 *
			 * This is a global event: it fires for every death anywhere in the scene. The previous
			 * AddPoints call ran through GetOrCreate, so each unrelated kill inserted a fresh
			 * zero-point entry into this NPC's threat table. Two things then broke: HasAggression
			 * became true for an NPC that had never been touched, which changed how PickTarget
			 * behaves; and the empty-to-non-empty edge in OnCharacterDamaged had already been
			 * consumed, so the *real* first hit no longer fired OnCombatInitiated and the NPC did
			 * not enter combat until its next physics sweep. */
			Controller.RemoveEntry(victim.ID);
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
