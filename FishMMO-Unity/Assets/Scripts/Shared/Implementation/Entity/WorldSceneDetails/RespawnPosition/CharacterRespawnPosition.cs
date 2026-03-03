using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour for marking a character's respawn position in the scene. Draws a gizmo for visualization in the editor.
	/// </summary>
	public class CharacterRespawnPosition : MonoBehaviour
	{
#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the respawn position gizmo in the editor.
		/// </summary>
		public Color GizmoColor = TinyColor.plum.ToUnityColor();

		/// <summary>
		/// Draws a gizmo at the respawn position for visualization in the Unity editor.
		/// </summary>
		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
			else
			{
				Gizmos.color = GizmoColor;
				Gizmos.DrawWireCube(transform.position, Vector3.one);
			}
		}
#endif
	}
}