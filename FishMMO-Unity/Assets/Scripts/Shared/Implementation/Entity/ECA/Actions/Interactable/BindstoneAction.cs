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

			/* A bind point is an open-world location, so it cannot be taken inside an instance.
			 *
			 * BindScene and BindPosition are consumed by the respawn-at-bind-point path, which
			 * assigns BindScene to SceneName and hands the character to the world server's
			 * open-world routing. A BindScene naming a dungeon would send that routing looking
			 * for open-world instances of a dungeon scene, find none, and request one be
			 * created — a trap the character carries until it binds somewhere else.
			 *
			 * This was previously refused only by accident: SceneName keeps naming the open
			 * world while the character is inside an instance, so it never matched the
			 * instance's own scene and the check below rejected it for the wrong reason. In the
			 * one case where the two names DO coincide it would have passed, recording instance
			 * coordinates against an open-world scene — which is how a player ends up respawning
			 * inside terrain. */
			if (player.IsInInstance())
			{
				Log.Debug("BindstoneAction", "Character cannot bind while inside an instance.");
				return;
			}

			/* Compare the scene the character is physically standing in, by handle.
			 *
			 * Scene stacking means several instances of one scene are loaded at once and share a
			 * name, so a name comparison cannot tell a bindstone in this character's instance
			 * from one in a different channel of the same scene. The handle is unambiguous
			 * within the process, which is the only place this check runs. */
			if (player.GameObject.scene.handle != data.Interactable.GameObject.scene.handle)
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