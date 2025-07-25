using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Play FX Action", menuName = "FishMMO/Triggers/Actions/Play FX")]
	public class PlayFXAction : BaseAction
	{
		[Tooltip("The FX prefab to play.")]
		public GameObject FXPrefab;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CollisionEventData collisionEventData))
			{
				Vector3 spawnPosition;
				if (collisionEventData.Collision.contacts.Length > 0)
				{
					spawnPosition = collisionEventData.Collision.contacts[0].point;
				}
				else if (collisionEventData.Collision.transform != null)
				{
					spawnPosition = collisionEventData.Collision.transform.position;
				}
				else if (initiator != null)
				{
					spawnPosition = initiator.Transform.position;
				}
				else
				{
					spawnPosition = Vector3.zero;
				}

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
	}
}