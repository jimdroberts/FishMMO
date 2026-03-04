using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing details for a scene boundary, including origin, size, and point containment logic.
	/// </summary>
	[Serializable]
	public class SceneBoundaryDetails
	{
		/// <summary>
		/// The origin (center) of the boundary in world space.
		/// </summary>
		public Vector3 BoundaryOrigin;

		/// <summary>
		/// The size (width, height, depth) of the boundary.
		/// </summary>
		public Vector3 BoundarySize;

		/// <summary>
		/// Checks if a given point is contained within the boundary.
		/// </summary>
		/// <param name="point">The point to check.</param>
		/// <returns>True if the point is inside the boundary, false otherwise.</returns>
		public bool ContainsPoint(Vector3 point)
		{
			// Checks X, Z, Y order, since X, Z out of bounds is probably more common than Y.
			return
				(BoundaryOrigin.x + BoundarySize.x / 2) >= point.x &&
				(BoundaryOrigin.x - BoundarySize.x / 2) <= point.x &&

				(BoundaryOrigin.z + BoundarySize.z / 2) >= point.z &&
				(BoundaryOrigin.z - BoundarySize.z / 2) <= point.z &&

				(BoundaryOrigin.y + BoundarySize.y / 2) >= point.y &&
				(BoundaryOrigin.y - BoundarySize.y / 2) <= point.y;
		}
	}
}