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
		/// Static event fired each tick for every active buff, providing the current network tick
		/// for UI duration-bar updates via <see cref="Buff.RemainingSeconds"/>.
		/// </summary>
		static Action<Buff, uint> OnBuffTick;

		/// <summary>
		/// Static event triggered when a buff (positive effect) is added.
		/// </summary>
		static Action<Buff> OnAddBuff;

		/// <summary>
		/// Static event triggered when a buff (positive effect) is removed.
		/// </summary>
		static Action<Buff> OnRemoveBuff;

		/// <summary>
		/// Static event triggered when a debuff (negative effect) is added.
		/// </summary>
		static Action<Buff> OnAddDebuff;

		/// <summary>
		/// Static event triggered when a debuff (negative effect) is removed.
		/// </summary>
		static Action<Buff> OnRemoveDebuff;

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
		void Apply(BaseBuffTemplate template, PredictionTick currentTick);

		/// <summary>
		/// Applies a buff from a server-authoritative context. Implementations should stamp the
		/// buff with their current replicate-domain tick when one is available.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
		/// <param name="serverTick">Fallback authoritative tick used before any replicate tick exists.</param>
		void ApplyAuthoritative(BaseBuffTemplate template, uint serverTick);

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