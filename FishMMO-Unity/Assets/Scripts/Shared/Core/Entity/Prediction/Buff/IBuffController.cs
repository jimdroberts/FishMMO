using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for a character's buff controller, handling application, removal, and events for buffs and debuffs.
	/// </summary>
	public interface IBuffController : ICharacterBehaviour
	{
		/// <summary>
		/// Static event fired each tick for every active buff, providing the owning controller and
		/// the current network tick for UI duration-bar updates via <see cref="Buff.RemainingSeconds"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>All five of these events carry their owner.</b> They are static — one invocation list
		/// shared by every buff controller in the process — and they used to carry only the
		/// <see cref="Buff"/>, which has no owner either. The local player's buff and debuff strips
		/// subscribe to them, so every buff applied to every NPC, pet and other player in view was
		/// rendered onto the local player's own strips. The strips looked correct only because
		/// keying by template ID hid duplicates; a debuff landing on a mob genuinely appeared on
		/// the player.
		/// </para>
		/// <para>
		/// Subscribers MUST compare the controller against the one they care about. There is no way
		/// to scope a static event at subscription time, so the filter has to be at the callback.
		/// </para>
		/// </remarks>
		static Action<IBuffController, Buff, uint> OnBuffTick;

		/// <summary>
		/// Static event triggered when a buff (positive effect) is added. Carries the owner.
		/// </summary>
		static Action<IBuffController, Buff> OnAddBuff;

		/// <summary>
		/// Static event triggered when a buff (positive effect) is removed. Carries the owner.
		/// </summary>
		static Action<IBuffController, Buff> OnRemoveBuff;

		/// <summary>
		/// Static event triggered when a debuff (negative effect) is added. Carries the owner.
		/// </summary>
		static Action<IBuffController, Buff> OnAddDebuff;

		/// <summary>
		/// Static event triggered when a debuff (negative effect) is removed. Carries the owner.
		/// </summary>
		static Action<IBuffController, Buff> OnRemoveDebuff;

		/// <summary>
		/// Raised on a client when the server-filtered observed-buff list for this character
		/// changes. Display-only; see <see cref="ObservedBuffs"/>.
		/// </summary>
		static Action<IBuffController> OnObservedBuffsChanged;

		/// <summary>
		/// The buffs this character is showing to observers, exactly as the SERVER chose them.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Separate from <see cref="Buffs"/> on purpose. <see cref="Buffs"/> is simulation state:
		/// on a client it is only correct for the character that client owns, because the ongoing
		/// corrections that keep it correct arrive through the owner-only reconcile. For anyone
		/// else it is whatever the spawn payload happened to contain, frozen. Rendering another
		/// player's buffs from it would show a snapshot taken when they came into view.
		/// </para>
		/// <para>
		/// This list is pushed by the server on every structural change, already filtered by
		/// <see cref="BaseBuffTemplate.HiddenFromOthers"/>, and carries no tick-domain state and no
		/// template hooks — a client cannot act on it, only draw it.
		/// </para>
		/// </remarks>
		IReadOnlyList<ObservedBuffEntry> ObservedBuffs { get; }

		/// <summary>
		/// Unscaled client time at which <see cref="ObservedBuffs"/> was last received, used to
		/// count the entries' remaining seconds down locally.
		/// </summary>
		float ObservedBuffsReceivedTime { get; }

		/// <summary>
		/// Triggers invoked when a buff or debuff is applied to this character. EventData: BuffEventData.
		/// </summary>
		List<Trigger> OnBuffApplyTriggers { get; }

		/// <summary>
		/// Triggers invoked when a buff or debuff is removed from this character. EventData: BuffEventData.
		/// </summary>
		List<Trigger> OnBuffRemoveTriggers { get; }

		/// <summary>
		/// Dictionary of all active buffs for the character, indexed by template ID.
		/// </summary>
		SortedDictionary<int, Buff> Buffs { get; }

		/// <summary>
		/// Gets the current tick in the controller's replicate-domain. This is the reference tick used for all buff comparisons.
		/// </summary>
		/// <returns>The current replicate-domain tick.</returns>
		uint GetCurrentDomainTick();

		/// <summary>
		/// Deterministic buff tick — evaluates expiry and tick conditions for all active buffs.
		/// Use this for CSP instead of relying on Unity's Update.
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		void Tick(uint currentTick);

		/// <summary>
		/// Applies a buff to the character by template, creating a new instance if needed and handling stacking.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
		/// <param name="currentTick">The replicate-domain tick at the moment of application.</param>
		/// <param name="caster">
		/// The character applying the buff, snapshotted so periodic effects have somebody to
		/// credit. May be null when the source has no initiator.
		/// </param>
		void Apply(BaseBuffTemplate template, PredictionTick currentTick, ICharacter caster = null);

		/// <summary>
		/// Applies a buff from a server-authoritative context. Implementations should stamp the
		/// buff with their current replicate-domain tick when one is available.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
		/// <param name="serverTick">Fallback authoritative tick used before any replicate tick exists.</param>
		/// <param name="caster">
		/// The character applying the buff, snapshotted so periodic effects have somebody to
		/// credit. May be null when the source has no initiator.
		/// </param>
		void ApplyAuthoritative(BaseBuffTemplate template, uint serverTick, ICharacter caster = null);

		/// <summary>
		/// Maps a raw authoritative tick to the controller's current replicate-domain tick
		/// when a replicate tick is available.
		/// </summary>
		/// <param name="serverTick">Fallback authoritative tick used before any replicate tick exists.</param>
		/// <returns>The effective tick to use for buff comparisons.</returns>
		uint ResolveAuthoritativeTick(uint serverTick);

		/// <summary>
		/// Applies a buff instance to the character if not already present, invoking appropriate events.
		/// </summary>
		/// <param name="buff">The buff instance to apply.</param>
		void Apply(Buff buff, bool suppressFX = false);


		/// <summary>
		/// Removes a buff by template ID, invoking removal events and cleaning up.
		/// </summary>
		/// <param name="buffID">The template ID of the buff to remove.</param>
		void Remove(int buffID);

		/// <summary>
		/// Removes a random non-permanent buff or debuff, filtered by inclusion flags.
		/// </summary>
		/// <param name="rng">The deterministic random number generator to use.</param>
		/// <param name="includeBuffs">Whether to include buffs in the selection.</param>
		/// <param name="includeDebuffs">Whether to include debuffs in the selection.</param>
		void RemoveRandom(DeterministicRNG rng, bool includeBuffs = false, bool includeDebuffs = false);

		/// <summary>
		/// Removes all non-permanent buffs from the character, optionally suppressing removal events.
		/// </summary>
		/// <param name="ignoreInvokeRemove">If true, does not invoke OnRemoveBuff/OnRemoveDebuff events.</param>
		void RemoveAll(bool ignoreInvokeRemove = false);

		/// <summary>
		/// Creates a reconcile snapshot of all active buffs.
		/// Returns null when no buffs are active.
		/// </summary>
		BuffReconcileEntry[] CreateReconcileSnapshot();

		/// <summary>
		/// Restores buff state from a reconcile snapshot.
		/// </summary>
		/// <param name="entries">Authoritative buff snapshot.</param>
		/// <param name="reconcileTick">Replicate tick associated with the reconcile snapshot.</param>
		void RestoreFromReconcile(BuffReconcileEntry[] entries, uint reconcileTick);
	}
}