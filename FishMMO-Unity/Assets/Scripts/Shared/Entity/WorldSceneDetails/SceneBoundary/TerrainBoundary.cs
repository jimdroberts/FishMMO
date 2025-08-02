using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour for defining terrain boundaries. Draws gizmos for visualization and provides boundary size and offset based on terrain data.
	/// </summary>
	[RequireComponent(typeof(Terrain))]
	public class TerrainBoundary : IBoundary
	{
		/// <summary>
		/// Additional offset to apply to the terrain boundary. Boundaries are inclusive; if a player is not within it, it will not apply.
		/// </summary>
		[Header("Terrain Boundaries are *inclusive*, if a player is not within it, it will not apply!")]
		public Vector3 BoundaryOffset;

		/// <summary>
		/// The size of the terrain, read from the terrain data. Marked readonly in the inspector.
		/// </summary>
		[ShowReadonly]
		public Vector3 TerrainSize;

		/// <summary>
		/// The offset of the terrain center, read from the terrain data. Marked readonly in the inspector.
		/// </summary>
		[ShowReadonly]
		public Vector3 TerrainOffset;

		/// <summary>
		/// Draws a wireframe cube gizmo in the editor to visualize the terrain boundary.
		/// </summary>
		public void OnDrawGizmos()
		{
			Gizmos.color = Color.grey;
			Gizmos.DrawWireCube(GetBoundaryOffset(), GetBoundarySize());
		}

		/// <summary>
		/// Draws a solid cube gizmo in the editor when the terrain boundary is selected. Updates terrain size and offset from terrain data.
		/// </summary>
		public void OnDrawGizmosSelected()
		{
			Terrain terrain = GetComponent<Terrain>();
			TerrainSize = terrain.terrainData.bounds.size;
			TerrainOffset = terrain.terrainData.bounds.center;

			Gizmos.color = Color.green;
			Gizmos.DrawCube(GetBoundaryOffset(), GetBoundarySize());
		}

		/// <summary>
		/// Gets the offset of the terrain boundary, calculated from the object's position, terrain offset, and vertical boundary offset.
		/// </summary>
		/// <returns>The offset vector for the terrain boundary.</returns>
		public override Vector3 GetBoundaryOffset()
		{
			return transform.position + TerrainOffset + Vector3.up * BoundaryOffset.y / 2f;
		}

		/// <summary>
		/// Gets the size of the terrain boundary, calculated from terrain size and additional boundary offset.
		/// </summary>
		/// <returns>The size vector of the terrain boundary.</returns>
		public override Vector3 GetBoundarySize()
		{
			return TerrainSize + BoundaryOffset;
		}
	}
}
