﻿using UnityEngine;

namespace FishMMO.Shared
{
	public class CharacterRespawnPosition : MonoBehaviour
	{
#if UNITY_EDITOR
		public Color GizmoColor = TinyColor.plum.ToUnityColor();

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