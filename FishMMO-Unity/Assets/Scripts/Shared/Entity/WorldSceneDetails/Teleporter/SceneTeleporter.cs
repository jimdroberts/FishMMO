using UnityEngine;

namespace FishMMO.Shared
{
	public class SceneTeleporter : MonoBehaviour
	{
#if UNITY_EDITOR
		public Color GizmoColor = Color.magenta;

		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
		}
#endif

#if UNITY_SERVER
		void OnTriggerEnter(Collider other)
		{
			if (other == null ||
				other.gameObject == null)
			{
				return;
			}

			IPlayerCharacter character = other.gameObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				Log.Debug("Character not found!");
				return;
			}

			if (character.IsTeleporting)
			{
				Log.Debug("Character is already teleporting!");
				return;
			}

			character.Teleport(gameObject.name);
		}
#endif
	}
}