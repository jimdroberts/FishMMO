using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for a character's buff controller, handling application, removal, and events for buffs and debuffs.
	/// </summary>
	public interface IBuffController : ICharacterBehaviour
	{
		/// <summary>
		/// Static event triggered when time is added to a buff.
		/// </summary>
		static Action<Buff> OnAddTime;

		/// <summary>
		/// Static event triggered when time is subtracted from a buff.
		/// </summary>
		static Action<Buff> OnSubtractTime;

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
		/// Dictionary of all active buffs for the character, indexed by template ID.
		/// </summary>
		Dictionary<int, Buff> Buffs { get; }

		/// <summary>
		/// Applies a buff to the character by template, creating a new instance if needed and handling stacking.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
		void Apply(BaseBuffTemplate template);

		/// <summary>
		/// Applies a buff instance to the character if not already present, invoking appropriate events.
		/// </summary>
		/// <param name="buff">The buff instance to apply.</param>
		void Apply(Buff buff);

		/// <summary>
		/// Removes a buff by template ID, invoking removal events and cleaning up.
		/// </summary>
		/// <param name="buffID">The template ID of the buff to remove.</param>
		void Remove(int buffID);

		/// <summary>
		/// Removes a random buff or debuff from the character, with options to include buffs and/or debuffs.
		/// </summary>
		/// <param name="rng">The random number generator to use.</param>
		/// <param name="includeBuffs">Whether to include buffs in the selection.</param>
		/// <param name="includeDebuffs">Whether to include debuffs in the selection.</param>
		void RemoveRandom(Random rng, bool includeBuffs = false, bool includeDebuffs = false);

		/// <summary>
		/// Removes all non-permanent buffs from the character, optionally suppressing removal events.
		/// </summary>
		/// <param name="ignoreInvokeRemove">If true, does not invoke OnRemoveBuff/OnRemoveDebuff events.</param>
		void RemoveAll(bool ignoreInvokeRemove = false);
	}
}