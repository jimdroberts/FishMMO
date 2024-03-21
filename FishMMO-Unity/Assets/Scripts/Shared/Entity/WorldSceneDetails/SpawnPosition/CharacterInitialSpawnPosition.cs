using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class CharacterInitialSpawnPosition : MonoBehaviour
	{
		public List<RaceTemplate> AllowedRaces = new List<RaceTemplate>();

#if UNITY_EDITOR
		public Color GizmoColor = TinyColor.orange.ToUnityColor();

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