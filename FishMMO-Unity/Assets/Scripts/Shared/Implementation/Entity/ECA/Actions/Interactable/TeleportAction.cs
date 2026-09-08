using System;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that teleports the interacting player via a <see cref="ITeleporter"/>.
	/// If the teleporter has a <see cref="ITeleporter.Target"/> transform set, the player is moved
	/// to that world position and rotation. Otherwise the player is teleported by the teleporter's
	/// key via <see cref="IPlayerCharacter.Teleport"/>, which the character system resolves
	/// against the world scene details cache: a same-scene destination is a local move, any
	/// other is a scene transfer.
	/// Server-only.
	/// </summary>
	/// <remarks>
	/// <para><b>No combat gate, on purpose.</b> A same-scene teleporter is a legitimate way to
	/// leave a fight (decided 2026-09-08); fast travel and cross-scene transfers are not, and
	/// they gate combat themselves.</para>
	/// <para>Every refusal here answers the owner with a toast. The interaction handler in front
	/// of this action already refuses dead, teleporting and unloaded characters silently at
	/// Debug level; what this action can refuse on its own is a broken teleporter or a character
	/// mid-transfer, and neither is something the player can see.</para>
	/// </remarks>
	[Serializable]
	public class TeleportAction : BaseAction
	{
		/// <summary>
		/// Moves the player to the teleporter's target position/rotation or triggers a keyed teleport.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			ITeleporter teleporter = data.Interactable as ITeleporter;
			if (teleporter == null) return;

			if (player.IsTeleporting)
			{
				SendToOwner(player, new ToastBroadcast() { Text = "You are already teleporting.", Severity = ToastSeverity.Warning });
				return;
			}

			if (teleporter.Target != null)
			{
				/* Motor is fetched with GetComponent, not required by attribute, so a prefab can
				 * lack it. Every other caller of this primitive guards it; an ECA action throwing
				 * takes the rest of the trigger list down with it. */
				if (player.Motor == null)
				{
					SendToOwner(player, new ToastBroadcast() { Text = "The teleporter cannot move you right now.", Severity = ToastSeverity.Error });
					return;
				}

				player.Motor.SetPositionAndRotationAndVelocity(
					teleporter.Target.position,
					teleporter.Target.rotation,
					Vector3.zero);
			}
			else
			{
				// The character system answers this one, including the refusals.
				player.Teleport(TeleporterKey.Normalize(data.Interactable.GameObject.name));
			}

			if (teleporter.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(teleporter.AchievementTemplate, 1);
			}
		}
	}
}
