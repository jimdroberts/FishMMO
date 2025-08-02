using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for Unity Colliders, including gizmo drawing and dimension extraction.
	/// </summary>
	public static class ColliderExtensions
	{
		/// <summary>
		/// Draws a gizmo for the collider in the editor using the specified color.
		/// Supports BoxCollider, SphereCollider, and CapsuleCollider.
		/// </summary>
		/// <param name="collider">The collider to draw.</param>
		/// <param name="color">The color to use for the gizmo.</param>
		public static void DrawGizmo(this Collider collider, Color color)
		{
			Gizmos.color = color;

			Transform transform = collider.transform;

			BoxCollider box = collider as BoxCollider;
			if (box != null)
			{
				// Draw a wireframe box at the collider's center, respecting transform and scale.
				Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(box.center), transform.rotation, transform.lossyScale);
				Gizmos.DrawWireCube(Vector3.zero, box.size);
			}
			else
			{
				SphereCollider sphere = collider as SphereCollider;
				if (sphere != null)
				{
					// Draw a wireframe sphere at the collider's position.
					Gizmos.DrawWireSphere(transform.position, sphere.radius);
				}
				else
				{
					CapsuleCollider capsule = collider as CapsuleCollider;
					if (capsule != null)
					{
						// Draw a wireframe box using the capsule's bounds (approximation).
						Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(capsule.center), transform.rotation, transform.lossyScale);
						Gizmos.DrawWireCube(Vector3.zero, capsule.bounds.size);
					}
				}
			}
		}

		/// <summary>
		/// Attempts to extract the height and radius from a collider.
		/// Supports SphereCollider, CapsuleCollider, BoxCollider, and MeshCollider.
		/// </summary>
		/// <param name="collider">The collider to extract dimensions from.</param>
		/// <param name="height">The extracted height value.</param>
		/// <param name="radius">The extracted radius value.</param>
		/// <returns>True if dimensions were successfully extracted; otherwise, false.</returns>
		public static bool TryGetDimensions(this Collider collider, out float height, out float radius)
		{
			height = 0f;
			radius = 0f;

			if (collider == null)
			{
				Log.Error("ColliderExtensions", "Collider is null.");
				return false;
			}

			switch (collider)
			{
				case SphereCollider sphereCollider:
					// For spheres, radius is the collider's radius, height is diameter.
					radius = sphereCollider.radius;
					height = radius * 2f;
					break;
				case CapsuleCollider capsuleCollider:
					// For capsules, use collider's height and radius.
					radius = capsuleCollider.radius;
					height = capsuleCollider.height;
					break;
				case BoxCollider boxCollider:
					// For boxes, radius is half the largest horizontal side, height is vertical size.
					radius = Mathf.Max(boxCollider.size.x, boxCollider.size.z) / 2f;
					height = boxCollider.size.y;
					break;
				case MeshCollider meshCollider:
					// For mesh colliders, use mesh bounds for radius and height.
					radius = Mathf.Max(meshCollider.sharedMesh.bounds.size.x, meshCollider.sharedMesh.bounds.size.z) / 2f;
					height = meshCollider.sharedMesh.bounds.size.y;
					break;
				default: return false;
			}
			return true;
		}
	}
}