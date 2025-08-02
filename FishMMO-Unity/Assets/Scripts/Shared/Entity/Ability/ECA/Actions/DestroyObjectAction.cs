using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that destroys a specified game object in the scene.
	/// </summary>
	[CreateAssetMenu(fileName = "New Destroy Object Action", menuName = "FishMMO/Triggers/Actions/Destroy Object")]
	public class DestroyObjectAction : BaseAction
	{
		/// <summary>
		/// Destroys the target game object if present in the event data.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target object.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="TargetEventData"/> from the event data. If successful, it destroys the target game object and deactivates it.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Try to get the event data for a target object. If not present, log a warning and exit.
			if (eventData.TryGet(out TargetEventData targetEventData))
			{
				// If the target exists, destroy and deactivate it.
				if (targetEventData.Target != null)
				{
					Destroy(targetEventData.Target);
					targetEventData.Target.SetActive(false);
				}
			}
			else
			{
				Log.Warning("DestroyObjectAction", "Expected TargetEventData.");
			}
		}

		/// <summary>
		/// Returns a formatted description of the destroy object action for UI display.
		/// </summary>
		/// <returns>A string describing the effect of the destroy action.</returns>
		public override string GetFormattedDescription()
		{
			return "Destroys the specified game object.";
		}
	}
}