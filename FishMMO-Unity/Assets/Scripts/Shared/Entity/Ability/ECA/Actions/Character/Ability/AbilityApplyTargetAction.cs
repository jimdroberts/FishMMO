using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies an ability effect to a single targeted character.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability Apply Target Action", menuName = "FishMMO/Triggers/Actions/Ability/Target/Apply Target")]
	public class AbilityApplyTargetAction : BaseAction
	{
		/// <summary>
		/// Executes the action, applying the ability to the targeted character.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data containing context for the action.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				AbilityObject abilityObject = abilityEventData.AbilityObject;

				if (abilityObject != null)
				{
					foreach (var action in abilityObject.Ability.OnHitEvents.Values)
					{
						action?.Execute(abilityEventData);
					}
				}
				else
				{
					Log.Warning("AbilityApplyTargetAction", "AbilityObject is null.");
				}
			}
			else
			{
				Log.Warning("AbilityApplyTargetAction", "Expected AbilityCollisionEventData.");
			}
		}

		/// <inheritdoc/>
		public override string GetFormattedDescription()
		{
			return "Applies ability effects to the targeted character.";
		}
	}
}