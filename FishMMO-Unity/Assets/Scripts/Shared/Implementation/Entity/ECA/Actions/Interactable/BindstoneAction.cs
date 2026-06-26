using System;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that binds the player's respawn location to their current position and scene.
	/// Requires the interactable to implement <see cref="IBindstone"/>.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class BindstoneAction : BaseAction
	{
		/// <summary>
		/// Sets the player's <see cref="IPlayerCharacter.BindPosition"/> and <see cref="IPlayerCharacter.BindScene"/>
		/// to their current motor position and scene name.
		/// </summary>
		/// <param name="initiator">The character binding to the bindstone.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			IBindstone bindstone = data.Interactable as IBindstone;

			// Validate same scene — the bindstone and the player must share a scene to avoid
			// cross-scene bind exploits.
			if (player.SceneName != data.Interactable.GameObject.scene.name)
			{
				Log.Debug("BindstoneAction", "Character is not in the same scene as the bindstone.");
				return;
			}

			player.BindPosition = player.Motor.Transform.position;
			player.BindScene = player.SceneName;

			if (bindstone?.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(bindstone.AchievementTemplate, 1);
			}
#endif
		}
	}
}