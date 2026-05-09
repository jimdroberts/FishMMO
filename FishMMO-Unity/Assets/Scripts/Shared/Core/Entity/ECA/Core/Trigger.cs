using UnityEngine;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared;
using UnityEngine.Serialization;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Shared helpers for evaluating trigger conditions and executing trigger action lists.
	/// All helpers honor optional per-condition / per-action <see cref="BaseAction.TargetSelector"/>
	/// and <see cref="BaseCondition.TargetSelector"/> fan-out.
	/// </summary>
	public static class TriggerExecution
	{
		/// <summary>
		/// Evaluates a condition list against the supplied event data, honoring per-condition
		/// <see cref="BaseCondition.TargetSelector"/> fan-out.
		/// </summary>
		/// <param name="conditions">The conditions to evaluate.</param>
		/// <param name="eventData">The event data passed to each condition.</param>
		/// <param name="filter">Optional filter to skip individual conditions.</param>
		/// <returns>True when every (non-skipped) condition passes; otherwise false.</returns>
		public static bool AreConditionsMet(List<BaseCondition> conditions, EventData eventData, System.Func<BaseCondition, bool> filter = null)
		{
			if (eventData == null)
			{
				return false;
			}
			if (conditions == null)
			{
				return true;
			}

			for (int i = 0; i < conditions.Count; ++i)
			{
				BaseCondition condition = conditions[i];
				if (condition == null)
				{
					continue;
				}
				if (filter != null && !filter(condition))
				{
					continue;
				}
				if (!EvaluateCondition(condition, eventData))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Evaluates a single condition, fanning out across <see cref="BaseCondition.TargetSelector"/>
		/// when set and combining results per <see cref="BaseCondition.Combine"/>.
		/// </summary>
		/// <param name="condition">The condition to evaluate.</param>
		/// <param name="eventData">The event data passed to the condition.</param>
		/// <returns>True when the condition is satisfied.</returns>
		public static bool EvaluateCondition(BaseCondition condition, EventData eventData)
		{
			if (condition.TargetSelector == null)
			{
				return condition.Evaluate(eventData.Initiator, eventData);
			}

			foreach (GameObject target in condition.TargetSelector.SelectTargets(eventData))
			{
				if (target == null)
				{
					continue;
				}
				EventData scoped = eventData.Fork(target);
				bool passed = condition.Evaluate(scoped.Initiator, scoped);
				if (passed && condition.Combine == ConditionTargetCombine.Any)
				{
					return true;
				}
				if (!passed && condition.Combine == ConditionTargetCombine.All)
				{
					return false;
				}
			}

			// Loop ended without short-circuiting:
			//   All  → no failure observed (or empty selection) → true (vacuous).
			//   Any  → no success observed → false.
			return condition.Combine == ConditionTargetCombine.All;
		}

		/// <summary>
		/// Executes an action list against the supplied event data, fanning out across
		/// per-action <see cref="BaseAction.TargetSelector"/> when set.
		/// </summary>
		/// <param name="actions">The actions to execute.</param>
		/// <param name="eventData">The event data passed to each action.</param>
		public static void ExecuteActions(List<BaseAction> actions, EventData eventData)
		{
			if (actions == null || eventData == null)
			{
				return;
			}

			for (int i = 0; i < actions.Count; ++i)
			{
				BaseAction action = actions[i];
				if (action == null)
				{
					continue;
				}

				if (action.TargetSelector == null)
				{
					action.Execute(eventData.Initiator, eventData);
					continue;
				}

				foreach (GameObject target in action.TargetSelector.SelectTargets(eventData))
				{
					if (target == null)
					{
						continue;
					}
					EventData scoped = eventData.Fork(target);
					action.Execute(scoped.Initiator, scoped);
				}
			}
		}
	}

	/// <summary>
	/// A trigger executes a list of actions against a set of targets selected by <see cref="TargetSelector"/>,
	/// branching on whether <see cref="Conditions"/> are satisfied for each selected target.
	/// <para>
	/// Lifecycle (per fire):
	/// <list type="number">
	///   <item><description>Caller builds an <see cref="EventData"/> and invokes <see cref="Execute(EventData)"/>.</description></item>
	///   <item><description><see cref="TargetSelector"/> yields N targets (using <see cref="EventData.Initiator"/> / <see cref="EventData.Target"/> as context).</description></item>
	///   <item><description>For each target, the trigger forks <paramref name="eventData"/> with the new target, evaluates conditions, and runs the matching action branch.</description></item>
	/// </list>
	/// The <see cref="EventData.Initiator"/> never changes during the trigger's lifecycle.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New Trigger", menuName = "FishMMO/ECA/Trigger", order = 0)]
	public class Trigger : CachedScriptableObject<Trigger>, ICachedObject
	{
		/// <summary>
		/// Selector that produces the targets this trigger fires for. Required at runtime —
		/// when null, the trigger logs a warning and treats the trigger as firing once with
		/// no target (initiator-only).
		/// </summary>
		[Tooltip("Selector that produces the targets this trigger fires for. Use InitiatorTargetSelector for self-only effects.")]
		[SerializeReference, SubclassSelector]
		public TargetSelector TargetSelector;

		/// <summary>
		/// Conditions that must be met for actions to execute. Each condition may have its own
		/// optional <see cref="BaseCondition.TargetSelector"/> for refined per-target evaluation.
		/// </summary>
		[Tooltip("Conditions that must be met for actions to execute.")]
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Actions to execute when all conditions are met for the current target.
		/// Each action may have its own optional <see cref="BaseAction.TargetSelector"/> for additional fan-out.
		/// </summary>
		[Tooltip("Actions to execute if all conditions are met.")]
		[FormerlySerializedAs("Actions")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsMetActions = new List<BaseAction>();

		/// <summary>
		/// Actions to execute when one or more conditions fail for the current target.
		/// </summary>
		[Tooltip("Actions to execute if one or more conditions are not met.")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsNotMetActions = new List<BaseAction>();

		/// <summary>
		/// Fires this trigger. The trigger fans out across <see cref="TargetSelector"/> and runs
		/// the condition/action branches once per selected target. The initiator on
		/// <paramref name="eventData"/> is never reassigned.
		/// </summary>
		/// <param name="eventData">The event data driving execution.</param>
		public virtual void Execute(EventData eventData)
		{
			if (eventData == null)
			{
				Log.Warning("Trigger", $"Trigger '{name}' executed without event data.");
				return;
			}

			if (TargetSelector == null)
			{
				Log.Warning("Trigger", $"Trigger '{name}' has no TargetSelector — executing once against the initiator.");
				ExecuteForTarget(eventData);
				return;
			}

			bool any = false;
			foreach (GameObject target in TargetSelector.SelectTargets(eventData))
			{
				if (target == null)
				{
					continue;
				}
				any = true;
				EventData scoped = eventData.Fork(target);
				ExecuteForTarget(scoped);
			}

			if (!any)
			{
				Log.Debug("Trigger", $"Trigger '{name}' produced no targets for {eventData.Initiator?.Name}.");
			}
		}

		/// <summary>
		/// Evaluates conditions and runs the matching action branch for a single (already-scoped) event.
		/// </summary>
		/// <param name="eventData">The event data scoped to one target.</param>
		private void ExecuteForTarget(EventData eventData)
		{
			System.Func<BaseCondition, bool> filter = ShouldEvaluateCondition;
			if (TriggerExecution.AreConditionsMet(Conditions, eventData, filter))
			{
				Log.Debug("Trigger", $"Trigger '{name}' conditions met for {eventData.Initiator?.Name}, target {eventData.Target?.name}.");
				TriggerExecution.ExecuteActions(OnConditionsMetActions, eventData);
			}
			else
			{
				Log.Debug("Trigger", $"Trigger '{name}' conditions not met for {eventData.Initiator?.Name}, target {eventData.Target?.name}.");
				TriggerExecution.ExecuteActions(OnConditionsNotMetActions, eventData);
			}
		}

		/// <summary>
		/// Determines whether a condition should participate in this trigger execution.
		/// Subclasses may override to skip categories of conditions (e.g. resource cost conditions
		/// already paid at activation time).
		/// </summary>
		/// <param name="condition">The condition being considered.</param>
		/// <returns>True when the condition should be evaluated; otherwise, false.</returns>
		protected virtual bool ShouldEvaluateCondition(BaseCondition condition)
		{
			return true;
		}
	}
}