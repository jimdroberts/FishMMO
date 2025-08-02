using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour for defining a scene boundary. Draws gizmos for visualization and provides boundary size and offset.
	/// </summary>
	public class SceneBoundary : IBoundary
	{
		/// <summary>
		/// The size of the scene boundary. Boundaries are inclusive; if a player is not within it, it will not apply.
		/// </summary>
		[Tooltip("Scene Boundaries are *inclusive*, if a player is not within it, it will not apply!")]
		public Vector3 BoundarySize;

		/// <summary>
		/// Draws a wireframe cube gizmo in the editor to visualize the boundary.
		/// </summary>
		public void OnDrawGizmos()
		{
			Gizmos.color = Color.grey;
			Gizmos.DrawWireCube(transform.position, BoundarySize);
		}

		/// <summary>
		/// Draws a solid cube gizmo in the editor when the boundary is selected.
		/// </summary>
		public void OnDrawGizmosSelected()
		{
			Gizmos.color = Color.green;
			Gizmos.DrawCube(transform.position, BoundarySize);
		}

		/// <summary>
		/// Gets the offset of the boundary, which is the object's position.
		/// </summary>
		/// <returns>The position of the boundary object.</returns>
		public override Vector3 GetBoundaryOffset()
		{
			return transform.position;
		}

		/// <summary>
		/// Gets the size of the boundary.
		/// </summary>
		/// <returns>The size vector of the boundary.</returns>
		public override Vector3 GetBoundarySize()
		{
			return BoundarySize;
		}
	}
}
