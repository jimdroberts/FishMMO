using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Which biome lies where in a scene, baked when the world was generated: a grid of
	/// <see cref="BiomeTemplate"/> IDs over a rectangle of world space. Imported from the
	/// biome-map cells WorldEditor exports, or painted by any other tool; both the server and
	/// the client read it, so it holds IDs rather than a texture.
	/// </summary>
	[CreateAssetMenu(fileName = "New Scene Biome Map", menuName = "FishMMO/Biomes/Scene Biome Map", order = 3)]
	public class SceneBiomeMap : CachedScriptableObject<SceneBiomeMap>, ICachedObject
	{
		[Tooltip("Grid columns (world X).")]
		[Min(1)] public int Width = 1;
		[Tooltip("Grid rows (world Z).")]
		[Min(1)] public int Height = 1;
		[Tooltip("World-space X/Z of the grid's south-west corner.")]
		public Vector2 WorldOrigin;
		[Tooltip("World-space extent of the grid along X and Z.")]
		public Vector2 WorldSize = new Vector2(1024f, 1024f);
		[Tooltip("BiomeTemplate cached-object IDs, row-major from the south-west corner; 0 = no biome.")]
		[HideInInspector] public int[] BiomeIDs = new int[1];

		/// <summary>True when the position's X/Z lies inside the grid's rectangle.</summary>
		public bool Contains(Vector3 worldPosition)
		{
			return worldPosition.x >= WorldOrigin.x && worldPosition.x <= WorldOrigin.x + WorldSize.x
				&& worldPosition.z >= WorldOrigin.y && worldPosition.z <= WorldOrigin.y + WorldSize.y;
		}

		/// <summary>0 at the south edge, 1 at the north edge; the equator is 0.5.</summary>
		public float Latitude01(Vector3 worldPosition)
		{
			return WorldSize.y <= 0f ? 0.5f : Mathf.Clamp01((worldPosition.z - WorldOrigin.y) / WorldSize.y);
		}

		/// <summary>The biome ID under a world position, or 0 outside the grid or where none was baked.</summary>
		public int IDAt(Vector3 worldPosition)
		{
			if (BiomeIDs == null || BiomeIDs.Length != Width * Height || !Contains(worldPosition))
			{
				return 0;
			}
			int x = Mathf.Clamp(Mathf.FloorToInt((worldPosition.x - WorldOrigin.x) / WorldSize.x * Width), 0, Width - 1);
			int z = Mathf.Clamp(Mathf.FloorToInt((worldPosition.z - WorldOrigin.y) / WorldSize.y * Height), 0, Height - 1);
			return BiomeIDs[z * Width + x];
		}

		/// <summary>The biome under a world position, or null.</summary>
		public BiomeTemplate Sample(Vector3 worldPosition)
		{
			int id = IDAt(worldPosition);
			return id != 0 && BiomeRegistry.TryGetByID(id, out BiomeTemplate biome) ? biome : null;
		}

		/// <summary>Replaces the whole grid.</summary>
		public void Set(int width, int height, int[] biomeIDs, Vector2 worldOrigin, Vector2 worldSize)
		{
			Width = Mathf.Max(1, width);
			Height = Mathf.Max(1, height);
			BiomeIDs = biomeIDs != null && biomeIDs.Length == Width * Height ? biomeIDs : new int[Width * Height];
			WorldOrigin = worldOrigin;
			WorldSize = worldSize;
		}
	}
}
