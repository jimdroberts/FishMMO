using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that plays a visual effect (FX) at a determined position, typically at the point of collision or interaction.
	/// </summary>
	[CreateAssetMenu(fileName = "New Play FX Action", menuName = "FishMMO/Triggers/Actions/Play FX")]
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
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
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
					Instantiate(FXPrefab, spawnPosition, Quaternion.identity);
				}
			}
			else
			{
				Log.Warning("PlayFXAction", "Expected CollisionEventData.");
			}
		}

		/// <summary>
		/// Returns a formatted description of the play FX action for UI display.
		/// </summary>
		/// <returns>A string describing the FX played and its location.</returns>
		public override string GetFormattedDescription()
		{
			return $"Plays FX: <color=#FFD700>{FXPrefab?.name ?? "FX"}</color> at the collision location.";
		}
	}
}