using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies a specified buff to a target character, potentially stacking it multiple times.
	/// </summary>
	[CreateAssetMenu(fileName = "New Apply Buff Action", menuName = "FishMMO/Triggers/Actions/Character/Apply Buff")]
	public class ApplyBuffAction : BaseAction
	{
		/// <summary>
		/// The number of stacks of the buff to apply to the target.
		/// </summary>
		public int Stacks;

		/// <summary>
		/// The buff template to apply to the target.
		/// </summary>
		public BaseBuffTemplate BuffTemplate;

		/// <summary>
		/// Applies the specified buff to the target character, stacking it the specified number of times.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="CharacterHitEventData"/> from the event data. If successful, it applies the buff to the target's buff controller.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Try to get the event data for a character hit. If not present, log a warning and exit.
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				// Try to get the buff controller from the target. If present, apply the buff the specified number of times.
				if (targetEventData.Target.TryGet(out IBuffController buffController))
				{
					for (int i = 0; i < Stacks; ++i)
					{
						buffController.Apply(BuffTemplate); // Apply the buff for each stack.
					}
				}
			}
			else
			{
				Log.Warning("ApplyBuffAction", "Expected CharacterHitEventData.");
			}
		}
		/// <summary>
		/// Returns a formatted description of the apply buff action for UI display.
		/// </summary>
		/// <returns>A string describing the number of stacks and the buff applied.</returns>
		public override string GetFormattedDescription()
		{
			return $"Applies <color=#FFD700>{Stacks}</color> stack(s) of <color=#FFD700>{BuffTemplate?.name ?? "Buff"}</color> to the target.";
		}
	}
}