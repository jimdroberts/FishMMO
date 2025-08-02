using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour for marking a character's initial spawn position in the scene. Allows restricting spawn to specific races and draws a gizmo for visualization.
	/// </summary>
	public class CharacterInitialSpawnPosition : MonoBehaviour
	{
		/// <summary>
		/// List of races allowed to spawn at this position.
		/// </summary>
		public List<RaceTemplate> AllowedRaces = new List<RaceTemplate>();

#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the initial spawn position gizmo in the editor.
		/// </summary>
		public Color GizmoColor = TinyColor.orange.ToUnityColor();

		/// <summary>
		/// Draws a gizmo at the initial spawn position for visualization in the Unity editor.
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