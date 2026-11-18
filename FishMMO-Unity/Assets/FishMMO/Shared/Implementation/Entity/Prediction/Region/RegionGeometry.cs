using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Point-containment helpers for region colliders. Axis-aligned <c>Collider.bounds</c> is
	/// wrong for rotated boxes, so boxes are tested in their own local space; other convex
	/// colliders use <see cref="Collider.ClosestPoint"/>; anything else falls back to bounds.
	/// </summary>
	public static class RegionGeometry
	{
		/// <summary>
		/// Pure test: is <paramref name="localPoint"/> (already in the box's local space) inside a
		/// box with the given local <paramref name="center"/> and <paramref name="size"/>?
		/// </summary>
		public static bool BoxContainsLocalPoint(Vector3 localPoint, Vector3 center, Vector3 size)
		{
			Vector3 half = size * 0.5f;
			Vector3 d = localPoint - center;
			return Mathf.Abs(d.x) <= Mathf.Abs(half.x)
				&& Mathf.Abs(d.y) <= Mathf.Abs(half.y)
				&& Mathf.Abs(d.z) <= Mathf.Abs(half.z);
		}

		/// <summary>
		/// Returns true when <paramref name="worldPoint"/> lies inside <paramref name="collider"/>.
		/// Null colliders never contain anything.
		/// </summary>
		public static bool ContainsPoint(Collider collider, Vector3 worldPoint)
		{
			if (collider == null)
			{
				return false;
			}
			if (collider is BoxCollider box)
			{
				Vector3 local = box.transform.InverseTransformPoint(worldPoint);
				return BoxContainsLocalPoint(local, box.center, box.size);
			}
			if (collider is SphereCollider || collider is CapsuleCollider ||
				(collider is MeshCollider mesh && mesh.convex))
			{
				Vector3 closest = collider.ClosestPoint(worldPoint);
				return (closest - worldPoint).sqrMagnitude <= 1e-6f;
			}
			return collider.bounds.Contains(worldPoint);
		}
	}
}
