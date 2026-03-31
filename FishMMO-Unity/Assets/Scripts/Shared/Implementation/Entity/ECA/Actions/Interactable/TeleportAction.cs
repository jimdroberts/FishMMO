using System;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that teleports the interacting player via a <see cref="ITeleporter"/>.
	/// If the teleporter has a <see cref="ITeleporter.Target"/> transform set, the player is moved
	/// to that world position and rotation. Otherwise the player is teleported by scene name
	/// via <see cref="IPlayerCharacter.Teleport"/>.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class TeleportAction : BaseAction
	{
		/// <summary>
		/// Moves the player to the teleporter's target position/rotation or triggers a named teleport.
		/// No-op if the player is already teleporting.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (initiator is not IPlayerCharacter player) return;
			if (player.IsTeleporting) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			ITeleporter teleporter = data.Interactable as ITeleporter;
			if (teleporter == null) return;

			if (teleporter.Target != null)
			{
				player.Motor.SetPositionAndRotationAndVelocity(
					teleporter.Target.position,
					teleporter.Target.rotation,
					Vector3.zero);
			}
			else
			{
				player.Teleport(data.Interactable.GameObject.name);
			}

			if (teleporter.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(teleporter.AchievementTemplate, 1);
			}
#endif
		}
	}
}