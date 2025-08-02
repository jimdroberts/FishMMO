using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a destination point for teleporters in the scene.
	/// Used to visually indicate and mark teleporter endpoints in the Unity Editor.
	/// </summary>
	public class TeleporterDestination : MonoBehaviour
	{
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
			}
		}
#endif
	}
}