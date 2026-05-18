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
				bool passed;
				try
				{
					passed = EvaluateCondition(condition, eventData);
				}
				catch (System.Exception ex)
				{
					Log.Error("Trigger", $"Condition '{condition.GetType().Name}' threw: {ex}");
					passed = false;
				}
				if (!passed)
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
				return condition.Check(eventData.Initiator, eventData);
			}

			foreach (GameObject target in condition.TargetSelector.SelectTargets(eventData))
			{
				if (target == null)
				{
					continue;
				}
				EventData scoped = eventData.Fork(target);
				bool passed = condition.Check(scoped.Initiator, scoped);
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
		/// per-action <see cref="BaseAction.TargetSelector"/> when set. Actions implementing
		/// <see cref="IAbortableAction"/> with <see cref="BaseAction.StopChainOnFailure"/> set
		/// will abort the rest of the chain when their <see cref="IAbortableAction.TryExecute"/>
		/// returns false (e.g. a resource cost that couldn't be paid).
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
					if (!RunOne(action, eventData.Initiator, eventData))
					{
						return;
					}
					continue;
				}

				foreach (GameObject target in action.TargetSelector.SelectTargets(eventData))
				{
					if (target == null)
					{
						continue;
					}
					EventData scoped = eventData.Fork(target);
					if (!RunOne(action, scoped.Initiator, scoped))
					{
						return;
					}
				}
			}
		}

		/// <summary>
		/// Invokes a single action instance, routing through <see cref="IAbortableAction"/> when
		/// the action opts in. Exceptions thrown by an action are caught and logged so a single
		/// malformed designer-authored action cannot poison the rest of the action chain or
		/// sibling targets in a fan-out. Returns false only when an abortable action signals
		/// failure AND has <see cref="BaseAction.StopChainOnFailure"/> set — in which case the
		/// caller should stop processing further actions/targets in the current list.
		/// </summary>
		private static bool RunOne(BaseAction action, ICharacter initiator, EventData eventData)
		{
			try
			{
				if (action is IAbortableAction abortable)
				{
					bool ok = abortable.TryExecute(initiator, eventData);
					return ok || !action.StopChainOnFailure;
				}
				action.Execute(initiator, eventData);
				return true;
			}
			catch (System.Exception ex)
			{
				Log.Error("Trigger", $"Action '{action.GetType().Name}' threw: {ex}");
				// Fault isolation: a throwing action does not abort sibling actions.
				return true;
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
		/// Selector that produces the targets this trigger fires for. When null, the trigger
		/// fires once against the event data as-is (i.e. against whatever <see cref="EventData.Target"/>
		/// the caller already set) — equivalent to using <see cref="EventTargetSelector"/> but
		/// without per-target conditions or override slots. Set a concrete selector for
		/// spatial fan-out (Area, Cone, Chain, …) or for explicit per-target conditions.
		/// </summary>
		[Tooltip("Selector that produces the targets this trigger fires for. Leave null to act on the event's existing Target. Use InitiatorTargetSelector for self-only, EventTargetSelector for explicit hit-target semantics.")]
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
				// Intentional fallback: act on the event's existing Target (or initiator
				// for events with no target). This is the common path for OnHit / region /
				// dialogue triggers whose caller already resolved the target.
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