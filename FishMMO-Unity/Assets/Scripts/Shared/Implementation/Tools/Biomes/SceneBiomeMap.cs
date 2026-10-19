using UnityEngine;
using UnityEngine.AddressableAssets;

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
		[Tooltip("The ground's altitude at each cell's centre, in the body's own metres above its sea level, as half floats in the same order as the biome IDs: the height each cell's biome was chosen at. Empty for a map not cut from a globe.")]
		[HideInInspector] public ushort[] GroundAltitudes = new ushort[0];
		[Tooltip("Scene metres per metre of the body's altitude, as the scene was cut. 0 for a map not cut from a globe.")]
		public float VerticalScale;
		[Tooltip("The solved flow down the scene's rivers, a sub-asset of this map: loaded by clients when they build the water, never by the server.")]
		public AssetReferenceT<SceneRiverFlow> RiverFlow;
		[Tooltip("The sea surface's coastal look (BiomeSeaCoast, 2026-10-18): texels along X. 0 when no biome on the map gives the sea a look of its own.")]
		public int CoastLookWidth;
		[Tooltip("The sea surface's coastal look: texels along Z.")]
		public int CoastLookHeight;
		[Tooltip("The world rectangle the coastal look covers: xy its south-west corner, zw its size, metres (the map and the reach past it).")]
		public Vector4 CoastLookRect;
		[Tooltip("The coastal look's texels, RGBA8 row-major from the south-west (BiomeSeaCoast): written with the map by the cut and every repaint.")]
		[HideInInspector] public byte[] CoastLook = new byte[0];

		/// <summary>True when the map carries a coastal look for the sea's surface (<see cref="BiomeSeaCoast"/>); false draws the sea as it always was.</summary>
		public bool HasCoastLook => CoastLookBake.Any;

		/// <summary>The stored coastal look as a bake (empty for none).</summary>
		public BiomeSeaCoast.Bake CoastLookBake => new BiomeSeaCoast.Bake { Width = CoastLookWidth, Height = CoastLookHeight, Rect = CoastLookRect, Texels = CoastLook };

		/// <summary>Stores a coastal look (<see cref="BiomeSeaCoast.For"/>); an empty one clears it.</summary>
		public void SetCoastLook(BiomeSeaCoast.Bake bake)
		{
			if (!bake.Any)
			{
				CoastLookWidth = CoastLookHeight = 0;
				CoastLookRect = Vector4.zero;
				CoastLook = new byte[0];
				return;
			}
			CoastLookWidth = bake.Width;
			CoastLookHeight = bake.Height;
			CoastLookRect = bake.Rect;
			CoastLook = bake.Texels;
		}

		/// <summary>Bakes and stores the coastal look of the grid as it stands, each ID's sea look from <paramref name="lookOf"/> (null for none).</summary>
		public void BakeCoastLook(System.Func<int, BiomeSeaLook?> lookOf)
		{
			SetCoastLook(BiomeSeaCoast.For(Width, Height, BiomeIDs, WorldOrigin, WorldSize, lookOf));
		}

		/// <summary>True when the cut baked the ground's altitude into this map (<see cref="TryGroundAltitude"/>).</summary>
		public bool HasGroundAltitudes => VerticalScale > 0f && GroundAltitudes != null && GroundAltitudes.Length == Width * Height && Width * Height > 0;

		/// <summary>
		/// The ground's altitude under a world position, in the body's own metres above its sea level, as the cut measured it:
		/// bilinear between cell centres, the edge held past the grid. False when none was baked.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Fixed at the cut, so every peer reads the same ground.</b> Temperature falls with altitude, and the cut chose
		/// each cell's biome from the altitude stored here, so the climate a running scene reads from it is the one its
		/// biomes were painted with. Asking the loaded terrain instead left the answer to whatever tile happened to be under
		/// the position, and to the character's own height wherever none was.
		/// </para>
		/// <para>Allocation-free and safe off the main thread: it touches no engine object.</para>
		/// </remarks>
		public bool TryGroundAltitude(float worldX, float worldZ, out float altitudeMetres)
		{
			altitudeMetres = 0f;
			if (!HasGroundAltitudes || WorldSize.x <= 0f || WorldSize.y <= 0f)
			{
				return false;
			}
			float gx = Mathf.Clamp((worldX - WorldOrigin.x) / WorldSize.x * Width - 0.5f, 0f, Width - 1);
			float gz = Mathf.Clamp((worldZ - WorldOrigin.y) / WorldSize.y * Height - 0.5f, 0f, Height - 1);
			int x0 = Mathf.Min((int)gx, Mathf.Max(0, Width - 2)), z0 = Mathf.Min((int)gz, Mathf.Max(0, Height - 2));
			int x1 = Mathf.Min(x0 + 1, Width - 1), z1 = Mathf.Min(z0 + 1, Height - 1);
			float fx = gx - x0, fz = gz - z0;
			float south = Mathf.Lerp(AltitudeOfCell(x0, z0), AltitudeOfCell(x1, z0), fx);
			float north = Mathf.Lerp(AltitudeOfCell(x0, z1), AltitudeOfCell(x1, z1), fx);
			altitudeMetres = Mathf.Lerp(south, north, fz);
			return true;
		}

		private float AltitudeOfCell(int x, int z) => PlanetClimateTableData.FromHalf(GroundAltitudes[z * Width + x]);

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

		/// <summary>Replaces the whole grid, with no ground altitudes: a map painted or imported rather than cut.</summary>
		public void Set(int width, int height, int[] biomeIDs, Vector2 worldOrigin, Vector2 worldSize)
		{
			Set(width, height, biomeIDs, worldOrigin, worldSize, null, 0f);
		}

		/// <summary>Replaces the whole grid and the ground under it (<see cref="TryGroundAltitude"/>).</summary>
		/// <param name="groundAltitudes">The body's metres above its sea level at each cell's centre, one per cell; null for none.</param>
		/// <param name="verticalScale">Scene metres per metre of the body's altitude the scene was cut at; 0 with no altitudes.</param>
		public void Set(int width, int height, int[] biomeIDs, Vector2 worldOrigin, Vector2 worldSize, float[] groundAltitudes, float verticalScale)
		{
			Width = Mathf.Max(1, width);
			Height = Mathf.Max(1, height);
			BiomeIDs = biomeIDs != null && biomeIDs.Length == Width * Height ? biomeIDs : new int[Width * Height];
			WorldOrigin = worldOrigin;
			WorldSize = worldSize;
			// A new grid's coast is baked by whoever wrote it (SceneBiomeField.WriteTo bakes it next); never the old grid's.
			SetCoastLook(BiomeSeaCoast.Bake.None);
			if (groundAltitudes != null && groundAltitudes.Length == Width * Height && verticalScale > 0f)
			{
				GroundAltitudes = new ushort[groundAltitudes.Length];
				for (int i = 0; i < groundAltitudes.Length; i++)
				{
					GroundAltitudes[i] = PlanetClimateTableData.ToHalf(groundAltitudes[i]);
				}
				VerticalScale = verticalScale;
			}
			else
			{
				GroundAltitudes = new ushort[0];
				VerticalScale = 0f;
			}
		}
	}
}
