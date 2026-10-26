using System.Collections.Generic;
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
		[Tooltip("The ground's heat (BiomeHeatMask, 2026-10-18, I2q): texels along X. 0 when the scene has no hot ground.")]
		public int HeatWidth;
		[Tooltip("The ground's heat: texels along Z.")]
		public int HeatHeight;
		[Tooltip("The world rectangle the heat covers: xy its south-west corner, zw its size, metres (only what the geothermal sites warm).")]
		public Vector4 HeatRect;
		[Tooltip("The heat's texels, 0 cold … 255 boiling, row-major from the south-west: baked by the cut and every repaint from the scene's geothermal sites (GeothermalVents.SceneSites) and their runoff.")]
		[HideInInspector] public byte[] Heat = new byte[0];
		[Tooltip("True once the cut (or a repaint) has chosen the scene's Great Spout and stored it here (2026-10-25, I2q): the largest geyser it dug, none when it dug no geyser. False on a map cut before, which finds it by search (GeothermalVents.TryGreatSpout).")]
		public bool GreatSpoutBaked;
		[Tooltip("True when the scene has a Great Spout (read only when GreatSpoutBaked).")]
		public bool HasGreatSpout;
		[Tooltip("The Great Spout's place (world xz), seed and size: everything its eruptions' schedule reads (GeothermalVents.Moment), so a Spout clock needs no search.")]
		public Vector2 GreatSpoutPosition;
		public uint GreatSpoutSeed;
		public float GreatSpoutSize;
		[Tooltip("The scene's stored vent lines (2026-10-25, I2q-b, decision 53: IceStripes.Of): the segments of every landform line (a tiger stripe, a rift, a double ridge) and the pads of a biome whose vents do not stand on the grid, written by the cut and every repaint. Empty: every vent on its grid.")]
		[HideInInspector] public List<VentLine> VentLines = new List<VentLine>();
		[Tooltip("True when the scene has a Great Plume (2026-10-25, I2q-b, decision 53): the largest jet on its stored lines, the cut's choice, which never falls under 0.4 of its strength with the plume tide.")]
		public bool HasGreatPlume;
		[Tooltip("The Great Plume's seed: the jet whose seed it is (GeothermalVents.IsGreatPlume).")]
		public uint GreatPlumeSeed;
		[Tooltip("The scene's stored chutes (2026-10-25, I2u falling stone, decisions 79/95: RockChutes.FromLandforms): every scree chute's and sciara's line, head to runout, its points in Chute Points. Written by the cut and every repaint. Empty: no stone falls.")]
		[HideInInspector] public List<RockChute> Chutes = new List<RockChute>();
		[Tooltip("The stored chutes' points: xyz on the ground (world m), w metres along its chute.")]
		[HideInInspector] public List<Vector4> ChutePoints = new List<Vector4>();
		[Tooltip("The scene's stored talus cones (I2u): apex and toe of each, where a breathing talus breathes.")]
		[HideInInspector] public List<TalusCone> TalusCones = new List<TalusCone>();

		/// <summary>Stores the scene's chutes, their points and its talus cones (<see cref="RockChutes.FromLandforms"/>); nulls clear them.</summary>
		public void SetRockChutes(IReadOnlyList<RockChute> chutes, IReadOnlyList<Vector4> points, IReadOnlyList<TalusCone> cones)
		{
			Chutes = chutes != null && points != null ? new List<RockChute>(chutes) : new List<RockChute>();
			ChutePoints = chutes != null && points != null ? new List<Vector4>(points) : new List<Vector4>();
			TalusCones = cones != null ? new List<TalusCone>(cones) : new List<TalusCone>();
		}

		[Tooltip("The scene's stored landform lines for its glows (2026-10-25, I6i-b, decisions 76/90: SceneGlowPlaces.FromLandforms): every stretch of every landform line (a rille's or channel's centreline, a ridge's crest) of every biome, written by the cut and every repaint. A TRAIL glow creeps along the nearest, a KNEADING glow reads its bearing. Empty: a fixed bearing and a smooth field, as before.")]
		[HideInInspector] public List<VentLine> GlowLines = new List<VentLine>();

		/// <summary>Stores the scene's landform lines for its glows (<see cref="SceneGlowPlaces.FromLandforms"/>); null or empty clears them.</summary>
		public void SetGlowLines(IReadOnlyList<VentLine> lines)
		{
			GlowLines = lines != null ? new List<VentLine>(lines) : new List<VentLine>();
		}

		[Tooltip("The scene's stored lava lines (2026-10-25, I2r, decision 65 (c): BiomeLavaChannels.FromLandforms): every stretch of every molten landform line (a lava channel, an ʻaʻā tongue's channel, a breakout, an ember rill), its half-width the lava's, written by the cut and every repaint. The client draws a lava ribbon along each. Empty: none.")]
		[HideInInspector] public List<VentLine> LavaLines = new List<VentLine>();

		/// <summary>Stores the scene's lava lines (<see cref="BiomeLavaChannels.FromLandforms"/>); null or empty clears them.</summary>
		public void SetLavaLines(IReadOnlyList<VentLine> lines)
		{
			LavaLines = lines != null ? new List<VentLine>(lines) : new List<VentLine>();
		}

		/// <summary>Stores the scene's vent lines (<see cref="IceStripes.FromLandforms"/>); null or empty clears them.</summary>
		public void SetVentLines(IReadOnlyList<VentLine> lines)
		{
			VentLines = lines != null ? new List<VentLine>(lines) : new List<VentLine>();
		}

		/// <summary>Stores the scene's Great Plume as the cut chose it (<paramref name="has"/> false: none).</summary>
		public void SetGreatPlume(bool has, uint seed)
		{
			HasGreatPlume = has;
			GreatPlumeSeed = has ? seed : 0u;
		}

		/// <summary>
		/// Stores the scene's Great Spout as the cut chose it (<paramref name="has"/> false: the scene has none); a new grid
		/// (<see cref="Set"/>) clears it until the writer stores it again.
		/// </summary>
		public void SetGreatSpout(bool has, Vector2 position, uint seed, float size)
		{
			GreatSpoutBaked = true;
			HasGreatSpout = has;
			GreatSpoutPosition = has ? position : Vector2.zero;
			GreatSpoutSeed = has ? seed : 0u;
			GreatSpoutSize = has ? size : 0f;
		}

		/// <summary>The stored heat mask (empty for none).</summary>
		public BiomeHeatMask HeatMask => new BiomeHeatMask { Width = HeatWidth, Height = HeatHeight, Area = HeatRect, Texels = Heat };

		/// <summary>True when the map carries a heat mask (the scene has hot ground).</summary>
		public bool HasHeat => HeatMask.Any;

		/// <summary>Stores a heat mask (<see cref="BiomeHeatMask.Bake"/>); an empty one clears it.</summary>
		public void SetHeat(BiomeHeatMask mask)
		{
			if (!mask.Any)
			{
				HeatWidth = HeatHeight = 0;
				HeatRect = Vector4.zero;
				Heat = new byte[0];
				return;
			}
			HeatWidth = mask.Width;
			HeatHeight = mask.Height;
			HeatRect = mask.Area;
			Heat = mask.Texels;
		}

		/// <summary>The ground's heat at a world point (xz), 0 cold … 1 boiling; 0 off the mask and on a map with none. Allocation-free.</summary>
		public float HeatAt(float worldX, float worldZ)
		{
			return HeatWidth > 0 ? HeatMask.At(worldX, worldZ) : 0f;
		}

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
			// Likewise its heat (the cut and the repaint bake it after writing the grid, 2026-10-18, I2q).
			SetHeat(BiomeHeatMask.None);
			// And its Great Spout (stored after the heat, 2026-10-25): until then the map searches, as one cut before did.
			GreatSpoutBaked = HasGreatSpout = false;
			GreatSpoutPosition = Vector2.zero;
			GreatSpoutSeed = 0u;
			GreatSpoutSize = 0f;
			// And its vent lines and Great Plume (2026-10-25, I2q-b): written after the grid by the cut and the repaint.
			SetVentLines(null);
			SetGreatPlume(false, 0u);
			// And its glows' landform lines (2026-10-25, I6i-b): written after the grid by the cut and the repaint.
			SetGlowLines(null);
			// And its chutes and talus cones (2026-10-25, I2u): likewise written after the grid.
			SetRockChutes(null, null, null);
			// And its lava lines (2026-10-25, I2r): likewise written after the grid.
			SetLavaLines(null);
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
