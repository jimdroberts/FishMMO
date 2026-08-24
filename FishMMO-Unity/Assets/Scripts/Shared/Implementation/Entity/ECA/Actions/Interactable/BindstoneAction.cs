using System;
using UnityEngine;
using KinematicCharacterController;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that binds the player's respawn location to their current position and scene.
	/// Requires the interactable to implement <see cref="IBindstone"/>.
	/// Server-only.
	/// </summary>
	/// <remarks>
	/// The position recorded is the <em>player's</em>, not the stone's, so a bind reflects where
	/// the player chose to stand. That only works if the spot is somewhere they can be put back
	/// down safely — a bind point is consumed by the respawn path with no further validation, so a
	/// bad one is permanent until the player binds again, and they may well be respawning into it
	/// precisely because they died. <see cref="IsValidBindPosition"/> is what makes the player's
	/// position safe to trust.
	/// </remarks>
	[Serializable]
	public class BindstoneAction : BaseAction
	{
		/// <summary>
		/// Refuse to bind when something solid is directly overhead within this distance.
		/// </summary>
		/// <remarks>
		/// Catches the case the ground and overlap checks cannot: a player who has clipped beneath
		/// the world stands on the underside of the terrain quite stably and overlaps nothing, and
		/// binding there gives them a permanent respawn inside the map.
		/// <para>
		/// Set to 0 for a bindstone that is legitimately indoors or in a cave, where a ceiling is
		/// expected and this check would refuse every honest bind.
		/// </para>
		/// </remarks>
		[Tooltip("Refuse to bind with solid geometry this close overhead. 0 disables — use 0 for indoor or cave bindstones.")]
		public float RequiredHeadroom = 3.0f;

		/// <summary>
		/// Buffer for the capsule overlap test.
		/// </summary>
		/// <remarks>
		/// Static and shared: the sweep is fully consumed inside a single synchronous
		/// <see cref="IsValidBindPosition"/> call, so no two binds can be mid-test at once.
		/// </remarks>
		private static readonly Collider[] overlapBuffer = new Collider[8];

		/// <summary>
		/// Buffer for the headroom sweep.
		/// </summary>
		private static readonly RaycastHit[] sweepBuffer = new RaycastHit[8];

		/// <summary>
		/// Sets the player's <see cref="IPlayerCharacter.BindPosition"/> and <see cref="IPlayerCharacter.BindScene"/>
		/// to their current motor position and scene name.
		/// </summary>
		/// <param name="initiator">The character binding to the bindstone.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
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

			if (!IsValidBindPosition(player, out string rejection))
			{
				Log.Debug("BindstoneAction", $"CharID={player.ID} cannot bind here: {rejection}");
				return;
			}

			player.BindPosition = player.Motor.Transform.position;
			player.BindScene = player.SceneName;

			if (bindstone?.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(bindstone.AchievementTemplate, 1);
			}
		}

		/// <summary>
		/// Returns true when the player is standing somewhere they can safely be put back down.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Every query runs against the motor's own <see cref="KinematicCharacterMotor.PhysicsScene"/>
		/// and its own capsule dimensions, rather than the global <c>Physics</c> API. Both matter:
		/// scene stacking loads several copies of a scene at once, so a global query would happily
		/// answer using another channel's geometry, and the character's real capsule is what has to
		/// fit — a point test would pass in a gap the player cannot occupy.
		/// </para>
		/// <para>
		/// The layer masks come from the motor too (<c>CollidableLayers</c>, <c>StableGroundLayers</c>),
		/// so "solid" here means exactly what it means to the character controller. A second,
		/// hand-maintained mask on this action would be one more thing to drift.
		/// </para>
		/// </remarks>
		/// <param name="player">The binding player.</param>
		/// <param name="rejection">Receives why the position was refused.</param>
		/// <returns>True when the position is safe to record.</returns>
		private bool IsValidBindPosition(IPlayerCharacter player, out string rejection)
		{
			rejection = null;

			KinematicCharacterMotor motor = player.Motor;
			if (motor == null)
			{
				rejection = "no character motor";
				return false;
			}

			/* Ask the controller, rather than re-deriving grounding from a raycast. IsStableOnGround
			 * already means "touching ground the character may stand on", slope limit included, and
			 * it is the same answer the movement code acts on — so a bind can never be accepted on
			 * a surface the player would immediately slide off. FoundAnyGround is deliberately not
			 * enough: that is true on a 70-degree face the character is sliding down. */
			if (!motor.GroundingStatus.IsStableOnGround)
			{
				rejection = "not standing on stable ground";
				return false;
			}

			Vector3 position = motor.Transform.position;
			Quaternion rotation = motor.Transform.rotation;

			/* Inside geometry. The controller de-collides itself as it moves, so an overlap here
			 * means the character is somewhere it could not have walked — clipped into terrain, or
			 * pushed inside a wall by a knockback. Recording that position would respawn them
			 * inside it, with no movement input able to get them out. */
			if (motor.CharacterCollisionsOverlap(position, rotation, overlapBuffer) > 0)
			{
				rejection = "overlapping world geometry";
				return false;
			}

			if (RequiredHeadroom > 0f)
			{
				/* Under the map. A player who has clipped through the world stands on the
				 * underside of the terrain perfectly stably and overlaps nothing, so neither check
				 * above sees it — but there is solid ground directly above their head, which there
				 * is not when standing on the world the right way up.
				 *
				 * Swept with the character's own capsule rather than raycast from a point, so a
				 * gap the player's shoulders would not fit through does not read as open sky. */
				int hits = motor.CharacterCollisionsSweep(
					position,
					rotation,
					motor.CharacterUp,
					RequiredHeadroom,
					out RaycastHit closestHit,
					sweepBuffer);

				if (hits > 0 && closestHit.collider != null)
				{
					rejection = $"only {closestHit.distance:0.00}m of headroom below '{closestHit.collider.name}'";
					return false;
				}
			}

			return true;
		}
	}
}
