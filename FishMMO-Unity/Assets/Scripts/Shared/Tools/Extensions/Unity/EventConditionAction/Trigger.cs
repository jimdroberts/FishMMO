using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Trigger", menuName = "FishMMO/Triggers/Trigger", order = 0)]
	public class Trigger : CachedScriptableObject<BaseCondition>, ICachedObject
	{
		[Tooltip("Conditions that must be met for actions to execute.")]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		[Tooltip("Actions to execute if all conditions are met.")]
		public List<BaseAction> Actions = new List<BaseAction>();

		public void Execute(EventData eventData)
		{
			if (eventData.Initiator == null)
			{
				Log.Warning($"Trigger '{name}' attempted to execute without a valid Initiator.");
				return;
			}

			// Evaluate all conditions
			foreach (var condition in Conditions)
			{
				if (condition != null)
				{
					if (!condition.Evaluate(eventData.Initiator, eventData)) // Pass the base EventData
					{
						Log.Debug($"Trigger '{name}' conditions not met for {eventData.Initiator?.Name}. Event: {eventData.GetType().Name}.");
						return;
					}
				}
			}

			// If all conditions pass, execute all actions
			Log.Debug($"Trigger '{name}' conditions met for {eventData.Initiator?.Name}. Executing actions for Event: {eventData.GetType().Name}...");
			foreach (var action in Actions)
			{
				if (action != null)
				{
					action.Execute(eventData.Initiator, eventData); // Pass the base EventData
				}
			}
		}
	}
}