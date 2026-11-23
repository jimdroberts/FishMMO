using System;
using UnityEngine;
using KinematicCharacterController;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using FishNet.Transporting;

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
	/// precisely because they died. <see cref="StandingSpot.IsValid"/> is what makes the player's
	/// position safe to trust.
	/// </remarks>
	[Serializable]
	[UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "FishMMO.Shared", sourceAssembly: "FishMMO.Shared")]
	public class BindstoneAction : BaseAction
	{
		/// <summary>
		/// Total vertical clearance, in metres from the ground, a bind position must have.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Floored at the character's own capsule height plus
		/// <see cref="MINIMUM_CLEARANCE_MARGIN"/>, whatever is configured here — a player needs room
		/// to stand and room to move, and a bind point is respawned into with no further validation.
		/// Raising this asks for more; lowering it cannot go below the floor, so there is no value
		/// that disables the check.
		/// </para>
		/// <para>
		/// Derived from the live capsule rather than a constant, because races differ in height and
		/// a fixed number would be too strict for one and too loose for another.
		/// </para>
		/// </remarks>
		[Tooltip("Total vertical clearance a bind spot needs, in metres from the ground. Always at least the character's height + 1m.")]
		public float RequiredClearance = 3.0f;

		/// <summary>The floor on <see cref="RequiredClearance"/> above the capsule; see <see cref="StandingSpot.MinimumClearanceMargin"/>.</summary>
		public const float MINIMUM_CLEARANCE_MARGIN = StandingSpot.MinimumClearanceMargin;

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
				SendFeedback(player, "You cannot set your home point inside an instance.");
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

			if (!StandingSpot.IsValid(player, RequiredClearance, out string rejection))
			{
				Log.Debug("BindstoneAction", $"CharID={player.ID} cannot bind here: {rejection}");
				// The detailed rejection is a diagnostic (it can name colliders); the player
				// gets the actionable part: this spot won't do, stand somewhere clearer.
				SendFeedback(player, "You cannot set your home point here — find a clearer spot to stand.");
				return;
			}

			player.BindPosition = player.Motor.Transform.position;
			player.BindScene = player.SceneName;

			/* The whole visible effect of a bind is two fields changing server-side, so without
			 * this line a successful bind is indistinguishable from a refused one from where the
			 * player stands — which reads as "the bindstone doesn't work" (reported live).
			 * Same System-channel pattern the dungeon finder uses. */
			SendFeedback(player, "Your home point has been set.");

			if (bindstone?.AchievementTemplate != null)
			{
				Accomplishments.Record(player, bindstone.AchievementTemplate);
			}
		}

		/// <summary>
		/// Tells the binding player what happened, on the System chat channel. Server-side, to
		/// the owner only; a character with no owner (nothing renders its feedback) is a no-op.
		/// </summary>
		private static void SendFeedback(IPlayerCharacter player, string text)
		{
			if (player?.Owner == null || !player.Owner.IsActive)
			{
				return;
			}
			player.Owner.Broadcast(new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = text,
			}, true, Channel.Reliable);
		}

	}
}
