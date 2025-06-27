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

		public static bool TryGetDimensions(this Collider collider, out float height, out float radius)
		{
			height = 0f;
			radius = 0f;

			if (collider == null)
			{
				Log.Error("Collider is null.");
				return false;
			}

			switch (collider)
			{
				case SphereCollider sphereCollider:
					radius = sphereCollider.radius;
					height = radius * 2f; // Height is the diameter of the sphere
					break;
				case CapsuleCollider capsuleCollider:
					radius = capsuleCollider.radius;
					height = capsuleCollider.height;
					break;
				case BoxCollider boxCollider:
					radius = Mathf.Max(boxCollider.size.x, boxCollider.size.z) / 2f;
					height = boxCollider.size.y;
					break;
				case MeshCollider meshCollider:
					radius = Mathf.Max(meshCollider.sharedMesh.bounds.size.x, meshCollider.sharedMesh.bounds.size.z) / 2f;
					height = meshCollider.sharedMesh.bounds.size.y;
					break;
				default: return false;
			}
			return true;
		}
	}
}