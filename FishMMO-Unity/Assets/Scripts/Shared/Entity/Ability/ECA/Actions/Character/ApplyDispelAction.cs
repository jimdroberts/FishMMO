using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that dispels (removes) a specified number of buffs and/or debuffs from a target character.
	/// </summary>
	[CreateAssetMenu(fileName = "New Apply Dispel Action", menuName = "FishMMO/Triggers/Actions/Character/Apply Dispel")]
	public class ApplyDispelAction : BaseAction
	{
		/// <summary>
		/// The number of buffs and/or debuffs to remove from the target.
		/// </summary>
		public byte AmountToRemove;

		/// <summary>
		/// Whether to include debuffs in the dispel operation.
		/// </summary>
		public bool IncludeDebuffs;

		/// <summary>
		/// Whether to include buffs in the dispel operation.
		/// </summary>
		public bool IncludeBuffs;

		/// <summary>
		/// Removes a specified number of buffs and/or debuffs from the target character.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="CharacterHitEventData"/> from the event data. If successful, it removes random buffs/debuffs from the target's buff controller.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Try to get the event data for a character hit. If not present, log a warning and exit.
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				// Try to get the buff controller from the target. If present, remove random buffs/debuffs.
				if (targetEventData.Target.TryGet(out IBuffController defenderBuffController))
				{
					// Remove up to AmountToRemove buffs/debuffs, as long as there are any left.
					for (int i = 0; i < AmountToRemove && defenderBuffController.Buffs.Count > 0; ++i)
					{
						defenderBuffController.RemoveRandom(targetEventData.RNG, IncludeBuffs, IncludeDebuffs);
					}
				}
			}
			else
			{
				Log.Warning("ApplyDispelAction", "Expected CharacterHitEventData.");
			}
		}
		/// <summary>
		/// Returns a formatted description of the apply dispel action for UI display.
		/// </summary>
		/// <returns>A string describing the number and type of effects dispelled.</returns>
		public override string GetFormattedDescription()
		{
			string type = (IncludeBuffs && IncludeDebuffs) ? "buffs and debuffs" : (IncludeBuffs ? "buffs" : (IncludeDebuffs ? "debuffs" : "effects"));
			return $"Dispels <color=#FFD700>{AmountToRemove}</color> {type} from the target.";
		}
	}
}