using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for character damage controllers, providing damage, healing, and death logic for a character.
	/// Includes static events for global damage/heal/kill notifications and resource management properties.
	/// </summary>
	public interface ICharacterDamageController : ICharacterBehaviour, IDamageable, IHealable
	{
		/// <summary>
		/// Event invoked when a character is damaged. Parameters: attacker, defender, amount, damage type.
		/// </summary>
		static Action<ICharacter, ICharacter, int, DamageAttributeTemplate> OnDamaged;

		/// <summary>
		/// Event invoked when a character is killed. Parameters: killer, victim.
		/// </summary>
		static Action<ICharacter, ICharacter> OnKilled;

		/// <summary>
		/// Event invoked when a character is resurrected. Params: resurrector, resurrected.
		/// </summary>
		static Action<ICharacter, ICharacter> OnResurrected;

		/// <summary>
		/// Event invoked when a resurrect is offered to a character but not yet applied.
		/// Params: resurrector, target, health amount the offer would restore.
		/// </summary>
		/// <remarks>
		/// Raised instead of reviving when the target is a player who can be asked. The server's
		/// character system records the offer and notifies the client; the revive happens only
		/// if the player accepts. Exists as an event because the action that offers lives in
		/// shared code and must not reference server types.
		/// </remarks>
		static Action<ICharacter, ICharacter, int> OnResurrectOffered;

		/// <summary>
		/// Event invoked when a character is healed. Parameters: healer, healed, amount.
		/// </summary>
		static Action<ICharacter, ICharacter, int> OnHealed;

		/// <summary>
		/// Gets or sets whether the character is immortal (cannot be damaged or killed).
		/// </summary>
		bool Immortal { get; set; }

		/// <summary>
		/// Returns true if the character is alive (resource attribute's current value is above zero).
		/// </summary>
		bool IsAlive { get; }

		/// <summary>
		/// Gets the cached health resource attribute for this character.
		/// </summary>
		CharacterResourceAttribute ResourceInstance { get; }

		// ───── ECA Trigger Lists ─────────────────────────────────────────────

		/// <summary>Triggers invoked when this character deals damage to another. EventData: DamageEventData.</summary>
		List<Trigger> OnDamageTriggers { get; }
		/// <summary>Triggers invoked when this character receives damage from another. EventData: DamageEventData.</summary>
		List<Trigger> OnDamagedTriggers { get; }
		/// <summary>Triggers invoked when this character heals another. EventData: HealEventData.</summary>
		List<Trigger> OnHealTriggers { get; }
		/// <summary>Triggers invoked when this character is healed by another. EventData: HealEventData.</summary>
		List<Trigger> OnHealedTriggers { get; }
		/// <summary>Triggers invoked when this character kills another. EventData with TargetCharacter set.</summary>
		List<Trigger> OnKillTriggers { get; }
		/// <summary>Triggers invoked when this character is killed by another. EventData with TargetCharacter set.</summary>
		List<Trigger> OnKilledTriggers { get; }
		/// <summary>Triggers invoked when this character resurrects another. EventData with TargetCharacter set.</summary>
		List<Trigger> OnResurrectTriggers { get; }
		/// <summary>Triggers invoked when this character is resurrected by another. EventData with TargetCharacter set.</summary>
		List<Trigger> OnResurrectedTriggers { get; }

		/// <summary>
		/// Kills the character, optionally specifying the killer.
		/// </summary>
		/// <param name="killer">The character responsible for the kill (can be null).</param>
		void Kill(ICharacter killer);

		/// <summary>
		/// Fully heals the character to maximum resource value.
		/// </summary>
		void CompleteHeal();

		/// <summary>
		/// Revives (resurrects) a dead character, ADDING the given amount to its health.
		/// Equivalent to setting it only when the character is at zero, which a dead one is.
		/// Unlike Heal(), this works when CurrentValue is 0. Resets death animation.
		/// </summary>
		/// <param name="resurrector">The character performing the resurrection, or null.</param>
		/// <param name="amount">The amount of health to restore.</param>
		void Revive(ICharacter resurrector, int amount);

		/// <summary>
		/// Gets whether this character is currently in combat (within the combat duration window).
		/// </summary>
		bool IsInCombat { get; }

		/// <summary>
		/// Gets the tick of the last combat action.
		/// </summary>
		uint LastCombatTick { get; }

		/// <summary>
		/// Gets the configured combat duration in ticks.
		/// </summary>
		uint CombatDurationTicks { get; }

		/// <summary>
		/// Enters combat state, refreshing the combat timer. Sets IsInCombat flag
		/// and records the current tick. Safe to call repeatedly — refreshes expiry.
		/// </summary>
		void EnterCombat();

		// ───── Loot Contribution ────────────────────────────────────────────

		/// <summary>
		/// Credits <paramref name="contributor"/> with a share of this character's death.
		/// </summary>
		/// <remarks>
		/// Server-only, and idempotent per contributor — a thousand hits earn the same single
		/// share as one. Credit is resolved to the controlling player, so a pet's damage counts
		/// for its owner; contributions from anything that is not ultimately a player are
		/// discarded, since only a player can open a loot window.
		/// </remarks>
		/// <param name="contributor">The character whose action earned the credit.</param>
		/// <param name="kind">How the credit was earned.</param>
		void RecordCombatContribution(ICharacter contributor, CombatContributionKind kind);

		/// <summary>
		/// Extends this character's own contribution credit to <paramref name="supporter"/>.
		/// </summary>
		/// <remarks>
		/// This is how healing earns loot rights. A healer never touches the victim, so there is
		/// nothing for <see cref="RecordCombatContribution"/> to key off; instead the character
		/// who WAS healed pushes its credit outward to everyone still fighting whatever it is
		/// fighting. Called on the healed character, not on the victim.
		/// </remarks>
		/// <param name="supporter">The character to extend credit to.</param>
		void PropagateCombatContribution(ICharacter supporter);

		/// <summary>
		/// Takes the accumulated contributor list and clears it.
		/// </summary>
		/// <remarks>
		/// Consuming rather than reading keeps the death path single-shot: whoever takes the list
		/// owns it, and a second call cannot hand the same corpse's loot rights out twice.
		/// </remarks>
		/// <param name="contributors">Receives the character IDs credited with the death.</param>
		/// <returns>True when at least one contributor was credited.</returns>
		bool TryConsumeContributors(out List<long> contributors);

		/// <summary>
		/// Drops all contribution bookkeeping in both directions.
		/// </summary>
		void ClearCombatContributions();
	}
}