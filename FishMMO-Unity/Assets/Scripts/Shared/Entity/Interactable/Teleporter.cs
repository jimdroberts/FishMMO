using UnityEngine;

namespace FishMMO.Shared
{
	public class Teleporter : Interactable
	{
		public Transform Target;

		public override string Title { get { return "Teleporter"; } }

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			if (Target == null)
			{
				return;
			}

			Gizmos.color = GizmoColor;
			Gizmos.DrawWireCube(Target.position, Vector3.one);
			Gizmos.DrawLine(transform.position, Target.position);
		}
#endif
	}
}