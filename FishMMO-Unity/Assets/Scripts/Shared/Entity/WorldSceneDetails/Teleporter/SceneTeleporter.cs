using UnityEngine;

namespace FishMMO.Shared
{
	public class SceneTeleporter : MonoBehaviour
	{
#if UNITY_SERVER
		void OnTriggerEnter(Collider other)
		{
			if (other == null ||
				other.gameObject == null)
			{
				return;
			}

			Character character = other.gameObject.GetComponent<Character>();
			if (character == null)
			{
				Debug.Log("Character not found!");
				return;
			}

			if (character.IsTeleporting)
			{
				return;
			}

			character.Teleport(gameObject.name);
		}
#endif
	}
}