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
	/// <para>
	/// Concrete classes implement <see cref="Evaluate"/> with their plain pass/fail logic.
	/// Framework code (TriggerExecution, TargetSelector, CompositeCondition) should call <see cref="Check"/>
	/// instead — it wraps <see cref="Evaluate"/> and applies the universal <see cref="Invert"/> flag.
	/// </para>
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
		/// When true, the final result of <see cref="Check"/> is logically negated. Apply this at
		/// the condition's boundary rather than open-coding inversion inside each derived class —
		/// every condition type gets the flag uniformly, and per-target <see cref="Combine"/>
		/// semantics still hold (Invert flips the aggregate, not each per-target evaluation).
		/// </summary>
		[Tooltip("If true, negate this condition's final result (after multi-target Combine).")]
		public bool Invert;

		/// <summary>
		/// Evaluates the condition's raw pass/fail logic. Must be implemented by derived classes.
		/// Derived classes should NOT apply <see cref="Invert"/> themselves — the framework calls
		/// <see cref="Check"/>, which wraps this method and applies <see cref="Invert"/> uniformly.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Optional event data for the condition.</param>
		/// <returns>True if the underlying check passes; otherwise, false.</returns>
		public abstract bool Evaluate(ICharacter initiator, EventData eventData = null);

		/// <summary>
		/// Evaluates the condition and applies <see cref="Invert"/> to the result. Framework
		/// code (TriggerExecution, TargetSelector, CompositeCondition, Ability activation
		/// checks) should call this method rather than <see cref="Evaluate"/> directly so the
		/// inversion semantics stay consistent everywhere.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Optional event data for the condition.</param>
		/// <returns>True when the condition (after optional inversion) is satisfied.</returns>
		public bool Check(ICharacter initiator, EventData eventData = null)
		{
			bool raw = Evaluate(initiator, eventData);
			return Invert ? !raw : raw;
		}
	}
}