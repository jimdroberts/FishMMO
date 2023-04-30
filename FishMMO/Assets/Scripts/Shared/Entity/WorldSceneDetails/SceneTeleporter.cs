using UnityEngine;

public class SceneTeleporter : MonoBehaviour
{
	private void OnCollisionEnter(Collision collision)
	{
		if (collision != null &&
			collision.gameObject != null)
		{
			Character character = collision.gameObject.GetComponent<Character>();
			if (character != null)
			{
				character.Owner.Broadcast(new CharacterSceneChangeRequestBroadcast()
				{
					fromTeleporter = character.sceneName,
					teleporterName = gameObject.name,
				});
			}
		}
	}
}