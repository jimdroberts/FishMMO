using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that allows an ability to pierce additional targets by increasing its hit count.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability Pierce Hit Action", menuName = "FishMMO/Triggers/Actions/Ability/Pierce Hit")]
	public class AbilityPierceHitAction : BaseAction
	{
		/// <summary>
		/// The number of additional targets the ability can pierce.
		/// </summary>
		public int PierceCount = -1;

		/// <summary>
		/// Executes the pierce hit action, increasing the ability's hit count to allow piercing more targets.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability collision information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out AbilityCollisionEventData pierceEventData))
			{
				pierceEventData.AbilityObject.HitCount += PierceCount;
			}
		}

		/// <summary>
		/// Returns a formatted description of the pierce hit action for UI display.
		/// </summary>
		/// <returns>A string describing the number of additional targets pierced.</returns>
		public override string GetFormattedDescription()
		{
			return $"Pierces <color=#FFD700>{PierceCount}</color> additional targets.";
		}
	}
}