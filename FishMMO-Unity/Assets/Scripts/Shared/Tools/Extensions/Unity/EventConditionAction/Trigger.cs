using UnityEngine;
using System.Collections.Generic;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a trigger that executes actions when all conditions are met for a given event.
	/// </summary>
	[CreateAssetMenu(fileName = "New Trigger", menuName = "FishMMO/Triggers/Trigger", order = 0)]
	public class Trigger : CachedScriptableObject<Trigger>, ICachedObject
	{
		/// <summary>
		/// Conditions that must be met for actions to execute.
		/// </summary>
		[Tooltip("Conditions that must be met for actions to execute.")]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Actions to execute if all conditions are met.
		/// </summary>
		[Tooltip("Actions to execute if all conditions are met.")]
		public List<BaseAction> Actions = new List<BaseAction>();

		/// <summary>
		/// Executes all actions if all conditions are met for the given event data.
		/// Logs warnings and debug info for failed or successful triggers.
		/// </summary>
		/// <param name="eventData">The event data used for condition evaluation and action execution.</param>
		public void Execute(EventData eventData)
		{
			if (eventData.Initiator == null)
			{
				Log.Warning("Trigger", $"Trigger '{name}' attempted to execute without a valid Initiator.");
				return;
			}

			// Evaluate all conditions
			foreach (var condition in Conditions)
			{
				if (condition != null)
				{
					// If any condition fails, abort execution
					if (!condition.Evaluate(eventData.Initiator, eventData))
					{
						Log.Debug("Trigger", $"Trigger '{name}' conditions not met for {eventData.Initiator?.Name}. Event: {eventData.GetType().Name}.");
						return;
					}
				}
			}

			// If all conditions pass, execute all actions
			Log.Debug("Trigger", $"Trigger '{name}' conditions met for {eventData.Initiator?.Name}. Executing actions for Event: {eventData.GetType().Name}...");
			foreach (var action in Actions)
			{
				if (action != null)
				{
					action.Execute(eventData.Initiator, eventData);
				}
			}
		}
	}
}