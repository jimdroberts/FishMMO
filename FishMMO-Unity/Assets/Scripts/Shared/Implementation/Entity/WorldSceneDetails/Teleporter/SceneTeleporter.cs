using UnityEngine;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour for scene teleporters. Handles teleportation logic on the server and draws gizmos for visualization in the editor.
	/// </summary>
	public class SceneTeleporter : MonoBehaviour
	{
		/// <summary>
		/// The stable destination ID this teleporter connects to. Selected via editor dropdown from the TeleporterCache.
		/// </summary>
		[SerializeField, HideInInspector]
		public string DestinationID;

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

		/// <summary>
		/// Called when another collider enters the teleporter's trigger. Teleports the player
		/// character if valid and not already teleporting.
		/// </summary>
		/// <remarks>
		/// Server-only by a runtime check, not <c>#if UNITY_SERVER</c>. That define is absent in
		/// the editor, where the scene server is developed and run, so the compile-time guard made
		/// every scene teleporter a dead volume during development while working in a server
		/// build. The check is the one <c>BaseAction.IsServer</c> uses: the character's network
		/// object is initialised as a server object on this peer.
		/// </remarks>
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
				return;
			}

			if (character.NetworkObject == null ||
				!character.NetworkObject.IsServerInitialized)
			{
				return;
			}

			if (character.IsTeleporting)
			{
				Log.Debug("SceneTeleporter", "Character is already teleporting!");
				return;
			}

			// Teleport the character to the destination baked under this teleporter's key.
			character.Teleport(TeleporterKey.Normalize(gameObject.name));
		}
	}
}