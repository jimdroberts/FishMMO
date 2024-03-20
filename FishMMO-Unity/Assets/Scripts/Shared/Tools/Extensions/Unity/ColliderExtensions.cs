using UnityEngine;

namespace FishMMO.Shared
{
	public static class ColliderExtensions
	{
		public static void DrawGizmo(this Collider collider, Color color)
		{
			Gizmos.color = color;

			Transform transform = collider.transform;

			BoxCollider box = collider as BoxCollider;
			if (box != null)
			{
				Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(box.center), transform.rotation, transform.lossyScale);
				Gizmos.DrawWireCube(Vector3.zero, box.size);
			}
			else
			{
				SphereCollider sphere = collider as SphereCollider;
				if (sphere != null)
				{
					Gizmos.DrawWireSphere(transform.position, sphere.radius);
				}
				else
				{
					CapsuleCollider capsule = collider as CapsuleCollider;
					if (capsule != null)
					{
						Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(capsule.center), transform.rotation, transform.lossyScale);
						Gizmos.DrawWireCube(Vector3.zero, capsule.bounds.size);
					}
				}
			}
		}
	}
}