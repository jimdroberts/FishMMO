using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a destination point for teleporters in the scene.
	/// Each destination has a stable DestinationID (GUID) that survives renames and moves.
	/// Used to visually indicate and mark teleporter endpoints in the Unity Editor.
	/// </summary>
	public class TeleporterDestination : MonoBehaviour
	{
		/// <summary>
		/// Stable unique identifier for this teleporter destination. Auto-generated on first add.
		/// Hidden from default inspector; displayed as readonly by TeleporterDestinationEditor.
		/// </summary>
		[SerializeField, HideInInspector]
		private string destinationID;

		/// <summary>
		/// Gets the stable unique identifier for this teleporter destination.
		/// </summary>
		public string DestinationID => destinationID;

		/// <summary>
		/// Auto-generates a DestinationID when the component is first added to a GameObject
		/// and registers it in the TeleporterCache.
		/// </summary>
		private void Reset()
		{
			if (string.IsNullOrEmpty(destinationID))
			{
				destinationID = System.Guid.NewGuid().ToString();
			}
#if UNITY_EDITOR
			string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
			TeleporterCache.RegisterDestination(this, sceneName);
#endif
		}

#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the gizmo for this teleporter destination in the Unity Editor.
		/// Defaults to forest green for visibility.
		/// </summary>
		public Color GizmoColor = TinyColor.forestGreen.ToUnityColor();

		/// <summary>
		/// Draws a gizmo in the editor to visually represent the teleporter destination.
		/// If a Collider is attached, draws the collider's gizmo; otherwise, draws a wire cube at the object's position.
		/// </summary>
		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				// Draw the collider's gizmo for accurate visualization of the destination area.
				collider.DrawGizmo(GizmoColor);
			}
			else
			{
				// If no collider is present, draw a default wire cube to indicate the position.
				Gizmos.color = GizmoColor;
				Gizmos.DrawWireCube(transform.position, Vector3.one);
				ColliderExtensions.DrawCenterMarker(transform.position, GizmoColor);
			}
		}
#endif
	}
}