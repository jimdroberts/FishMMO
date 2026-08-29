using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that plays a visual effect (FX) at a determined position, typically at the point of collision or interaction.
	/// </summary>
	[Serializable]
	public class PlayFXAction : BaseAction
	{
		/// <summary>
		/// The FX prefab to play when this action is executed.
		/// </summary>
		[Tooltip("The FX prefab to play.")]
		public GameObject FXPrefab;

		/// <summary>
		/// Plays the FX prefab at the collision or interaction location.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing collision or interaction information.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="CollisionEventData"/> from the event data. The FX is spawned at the first contact point if available, otherwise at the collision transform's position, the initiator's position, or Vector3.zero as fallback.
		/// VFX instantiation is suppressed during prediction replay ticks to prevent visual spam.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Clients only. OnHit now dispatches on every peer, so without this the dedicated
			 * server instantiates a particle prefab nobody can see — and before that widening, this
			 * action ran ONLY there, which is why authored impact effects were invisible to every
			 * player. Purely presentational, so it takes the client gate and never an authority one. */
			if (!IsClientPeer(initiator, eventData))
			{
				return;
			}

			// Suppress VFX during prediction replay to prevent visual spam
			if (IsReplayTick(eventData)) return;

			// Try to get the collision event data. If not present, log a warning and exit.
			if (eventData.TryGet(out CollisionEventData collisionEventData))
			{
				Vector3 spawnPosition;
				// Prefer the first contact point if available.
				if (collisionEventData.Collision.contacts.Length > 0)
				{
					spawnPosition = collisionEventData.Collision.contacts[0].point;
				}
				// Otherwise, use the collision object's position if available.
				else if (collisionEventData.Collision.transform != null)
				{
					spawnPosition = collisionEventData.Collision.transform.position;
				}
				// Otherwise, use the initiator's position if available.
				else if (initiator != null)
				{
					spawnPosition = initiator.Transform.position;
				}
				// Fallback to the world origin if all else fails.
				else
				{
					spawnPosition = Vector3.zero;
				}

				// Instantiate the FX prefab at the determined position if it is set.
				if (FXPrefab != null)
				{
					UnityEngine.Object.Instantiate(FXPrefab, spawnPosition, Quaternion.identity);
				}
			}
			else
			{
				Log.Warning("PlayFXAction", "Expected CollisionEventData.");
			}
		}
	}
}
