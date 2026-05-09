using UnityEngine;
using System.Collections.Generic;
using FishMMO.Logging;
using UnityEngine.Serialization;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Shared helpers for evaluating trigger conditions and executing trigger action lists.
	/// </summary>
	public static class TriggerExecution
	{
		/// <summary>
		/// Determines whether all supplied conditions pass for the supplied event data.
		/// </summary>
		/// <param name="conditions">The conditions to evaluate.</param>
		/// <param name="eventData">The event data used for evaluation.</param>
		/// <returns>True when all conditions pass; otherwise, false.</returns>
		public static bool AreConditionsMet(List<BaseCondition> conditions, EventData eventData)
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
				if (condition != null && !condition.Evaluate(eventData.Initiator, eventData))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Executes the supplied actions against the supplied event data.
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
				if (action != null)
				{
					action.Execute(eventData.Initiator, eventData);
				}
			}
		}
	}

	/// <summary>
	/// Represents a trigger that executes actions when all conditions are met for a given event.
	/// </summary>
	[CreateAssetMenu(fileName = "New Trigger", menuName = "FishMMO/ECA/Trigger", order = 0)]
	public class Trigger : CachedScriptableObject<Trigger>, ICachedObject
	{
		/// <summary>
		/// Conditions that must be met for actions to execute.
		/// </summary>
		[Tooltip("Conditions that must be met for actions to execute.")]
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Actions to execute if all conditions are met.
		/// </summary>
		[Tooltip("Actions to execute if all conditions are met.")]
		[FormerlySerializedAs("Actions")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsMetActions = new List<BaseAction>();

		/// <summary>
		/// Actions to execute if one or more conditions are not met.
		/// </summary>
		[Tooltip("Actions to execute if one or more conditions are not met.")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsNotMetActions = new List<BaseAction>();

		/// <summary>
		/// Executes all actions if all conditions are met for the given event data.
		/// Logs warnings and debug info for failed or successful triggers.
		/// </summary>
		/// <param name="eventData">The event data used for condition evaluation and action execution.</param>
		public virtual void Execute(EventData eventData)
		{
			if (eventData == null)
			{
				Log.Warning("Trigger", $"Trigger '{name}' executed without event data.");
				return;
			}

			if (eventData.Initiator == null)
			{
				Log.Warning("Trigger", $"Trigger '{name}' executed without a valid Initiator — world-level event.");
			}

			if (!AreConditionsMet(eventData))
			{
				Log.Debug("Trigger", $"Trigger '{name}' conditions not met for {eventData.Initiator?.Name}. Event: {eventData.GetType().Name}.");
				ExecuteActions(OnConditionsNotMetActions, eventData);
				return;
			}

			Log.Debug("Trigger", $"Trigger '{name}' conditions met for {eventData.Initiator?.Name}. Executing actions for Event: {eventData.GetType().Name}...");
			ExecuteActions(OnConditionsMetActions, eventData);
		}

		/// <summary>
		/// Determines whether this trigger's conditions are met for the supplied event data.
		/// </summary>
		/// <param name="eventData">The event data used for condition evaluation.</param>
		/// <returns>True if all evaluated conditions pass; otherwise, false.</returns>
		protected bool AreConditionsMet(EventData eventData)
		{
			if (Conditions == null)
			{
				return true;
			}

			for (int i = 0; i < Conditions.Count; ++i)
			{
				BaseCondition condition = Conditions[i];
				if (condition != null && ShouldEvaluateCondition(condition, eventData) && !condition.Evaluate(eventData.Initiator, eventData))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Determines whether a condition should participate in this trigger execution.
		/// </summary>
		/// <param name="condition">The condition being considered.</param>
		/// <param name="eventData">The event data used for execution.</param>
		/// <returns>True when the condition should be evaluated; otherwise, false.</returns>
		protected virtual bool ShouldEvaluateCondition(BaseCondition condition, EventData eventData)
		{
			return true;
		}

		/// <summary>
		/// Executes the supplied actions against the supplied event data.
		/// </summary>
		/// <param name="actions">The actions to execute.</param>
		/// <param name="eventData">The event data passed to each action.</param>
		protected void ExecuteActions(List<BaseAction> actions, EventData eventData)
		{
			TriggerExecution.ExecuteActions(actions, eventData);
		}
	}
}