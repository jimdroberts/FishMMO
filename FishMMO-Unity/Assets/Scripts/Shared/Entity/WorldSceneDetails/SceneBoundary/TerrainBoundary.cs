using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(Terrain))]
	public class TerrainBoundary : IBoundary
	{
		[Header("Terrain Boundaries are *inclusive*, if a player is not within it, it will not apply!")]
		public Vector3 BoundaryOffset;
		[ShowReadonly]
		public Vector3 TerrainSize;
		[ShowReadonly]
		public Vector3 TerrainOffset;

		public void OnDrawGizmos()
		{
			Gizmos.color = Color.grey;
			Gizmos.DrawWireCube(GetBoundaryOffset(), GetBoundarySize());
		}

		public void OnDrawGizmosSelected()
		{
			Terrain terrain = GetComponent<Terrain>();
			TerrainSize = terrain.terrainData.bounds.size;
			TerrainOffset = terrain.terrainData.bounds.center;

			Gizmos.color = Color.green;
			Gizmos.DrawCube(GetBoundaryOffset(), GetBoundarySize());
		}

		public override Vector3 GetBoundaryOffset()
		{
			return transform.position + TerrainOffset + Vector3.up * BoundaryOffset.y / 2f;
		}

		public override Vector3 GetBoundarySize()
		{
			return TerrainSize + BoundaryOffset;
		}
	}
}
