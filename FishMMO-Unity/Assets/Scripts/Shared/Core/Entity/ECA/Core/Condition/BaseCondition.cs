using System;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// How a <see cref="BaseCondition"/> with a <see cref="BaseCondition.TargetSelector"/> combines
	/// per-target evaluation results.
	/// </summary>
	public enum ConditionTargetCombine
	{
		/// <summary>All selected targets must satisfy the condition (logical AND).</summary>
		All = 0,
		/// <summary>At least one selected target must satisfy the condition (logical OR).</summary>
		Any = 1,
	}

	/// <summary>
	/// Abstract base class for all ECA conditions. Serialized inline via [SerializeReference] on Trigger assets.
	/// Derive from this class and add [Serializable] to create concrete conditions.
	/// </summary>
	[Serializable]
	public abstract class BaseCondition : ICondition
	{
		/// <summary>
		/// Optional selector that picks one or more targets for this condition. When set, the
		/// condition is evaluated once per selected target and the results combined per
		/// <see cref="Combine"/>. When unset, the condition runs against the current event
		/// data (reading <see cref="EventData.TargetCharacter"/> or falling back to the initiator).
		/// </summary>
		[Tooltip("Optional selector for this condition. When unset the condition runs against the current event target.")]
		[SerializeReference, SubclassSelector]
		public TargetSelector TargetSelector;

		/// <summary>
		/// How per-target results are combined when <see cref="TargetSelector"/> yields multiple targets.
		/// </summary>
		[Tooltip("Combine mode for multi-target evaluation when TargetSelector is set.")]
		public ConditionTargetCombine Combine = ConditionTargetCombine.All;

		/// <summary>
		/// Evaluates the condition. Must be implemented by derived classes.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Optional event data for the condition.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public abstract bool Evaluate(ICharacter initiator, EventData eventData = null);
	}
}