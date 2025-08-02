using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour for scene teleporters. Handles teleportation logic on the server and draws gizmos for visualization in the editor.
	/// </summary>
	public class SceneTeleporter : MonoBehaviour
	{
#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the teleporter gizmo in the editor.
		/// </summary>
		public Color GizmoColor = Color.magenta;

		/// <summary>
		/// Draws a gizmo at the teleporter position for visualization in the Unity editor.
		/// </summary>
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
		/// <summary>
		/// Called when another collider enters the teleporter's trigger. Teleports the player character if valid and not already teleporting.
		/// </summary>
		/// <param name="other">The collider that entered the trigger.</param>
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
				Log.Debug("SceneTeleporter", "Character not found!");
				return;
			}

			if (character.IsTeleporting)
			{
				Log.Debug("SceneTeleporter", "Character is already teleporting!");
				return;
			}

			// Teleport the character to the destination associated with this teleporter's name.
			character.Teleport(gameObject.name);
		}
#endif
	}
}