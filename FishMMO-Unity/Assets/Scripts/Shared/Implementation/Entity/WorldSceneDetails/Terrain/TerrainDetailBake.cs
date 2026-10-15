using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's terrain detail maps, baked by the cut (TerrainDetailBaker, client editor): per terrain, what the client's
	/// blade grass (GrassTerrain) and GPU detail scatter (DetailScatterTerrain) used to build from the terrain data on every
	/// Play — the summed, smoothed and packed density maps, the heights, the water line and holes, the surface layers, the
	/// work items' bounds — plus the grass's colour copies of the terrain texture sets. The client takes a terrain from here
	/// in a frame instead of reading it back over ~1,400 frames, and falls back to building it whenever an entry does not
	/// match what it would build (<see cref="Version"/>, the prototypes and their prefabs, the resolutions, the scatter's
	/// density scale) or is marked <see cref="Tile.Stale"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Client data.</b> Only <see cref="SceneTerrainBake"/> references it, on a <see cref="ClientOnlyObject"/>, so server
	/// builds drop it. Binary: the maps are megabytes, which the project's text serialization would write as hex.
	/// </para>
	/// <para>
	/// <b>What is not baked.</b> Anything the runtime decides: the grass and scatter types (their indices, the profile's
	/// tunings, the heights the grass cover is weighed by), which are resolved at load and checked against the prefabs
	/// recorded here.
	/// </para>
	/// </remarks>
	[PreferBinarySerialization]
	public sealed class TerrainDetailBake : ScriptableObject
	{
		/// <summary>
		/// The layout and the build's arithmetic: bumped whenever either builder changes what it computes, so every bake made
		/// before reads as unusable (the runtime builds those terrains as before) until the scene is cut or rebaked.
		/// </summary>
		public const int Version = 1;

		/// <summary>The <see cref="Version"/> this was baked at.</summary>
		public int BakedVersion;

		public List<Tile> Tiles = new List<Tile>();
		public List<Albedo> Albedos = new List<Albedo>();
		/// <summary>The ground colour and normal maps under the scene's terrains (GroundColourMap), as the client builds them at Play.</summary>
		public GroundColour Ground = new GroundColour();

		/// <summary>One terrain: its heights (shared by both maps' height textures) and its grass and scatter.</summary>
		[Serializable]
		public sealed class Tile
		{
			/// <summary>The terrain data it was baked from (the key).</summary>
			public TerrainData Data;
			/// <summary>Set in the editor when the terrain's heights or textures were edited after the bake: the runtime builds it.</summary>
			public bool Stale;
			/// <summary>The heightmap's resolution, and its samples as 0..1 of the terrain's height in 16 bits, row z first.</summary>
			public int HeightResolution;
			public ushort[] Heights = new ushort[0];
			public Grass Grass = new Grass();
			public Scatter Scatter = new Scatter();
		}

		/// <summary>The blade grass's maps of one terrain (GrassTerrain).</summary>
		[Serializable]
		public sealed class Grass
		{
			/// <summary>False when nothing was baked for it (no blade prototype, or it was not built).</summary>
			public bool Baked;
			/// <summary>False when its blade prototypes paint nothing anywhere: the terrain has no blades.</summary>
			public bool Present;
			/// <summary>The prototypes read, the channel each adds into, and each channel's prefab: what the runtime's selection must match.</summary>
			public int[] LayerPrototypes = new int[0];
			public int[] LayerChannels = new int[0];
			public GameObject[] ChannelPrefabs = new GameObject[0];
			/// <summary>The density maps: <see cref="DensityMaps"/> RGBA8 squares of <see cref="DensityResolution"/>, one after the other.</summary>
			public int DensityResolution;
			public int DensityMaps;
			public byte[] Density = new byte[0];
			/// <summary>Per density texel: 1 where any blade grows; the ground's share under blades (0..255).</summary>
			public byte[] Occupied = new byte[0];
			public byte[] Sward = new byte[0];
			/// <summary>The min/max ground height grid (world metres), <see cref="GridX"/> × <see cref="GridZ"/> cells.</summary>
			public int GridX, GridZ;
			public float[] MinHeights = new float[0];
			public float[] MaxHeights = new float[0];
			/// <summary>The water line and holes (RG8 on the heights' grid); empty when no water reaches it and it has no hole.</summary>
			public byte[] Freeboard = new byte[0];
			/// <summary>The packed surface layers (RGBA8, <see cref="SurfaceResolution"/> square); empty when the terrain has no layers.</summary>
			public int SurfaceResolution;
			public byte[] Surface = new byte[0];
		}

		/// <summary>The GPU detail scatter's maps of one terrain (DetailScatterTerrain).</summary>
		[Serializable]
		public sealed class Scatter
		{
			public bool Baked;
			public bool Present;
			/// <summary>The detail density scale the counts were baked at: a player whose quality setting differs builds the terrain.</summary>
			public float DensityScale = 1f;
			public int[] LayerPrototypes = new int[0];
			public int[] LayerChannels = new int[0];
			public GameObject[] ChannelPrefabs = new GameObject[0];
			/// <summary>Per channel, its placement: the prototypes' sizes, noise and ground alignment, and its seed.</summary>
			public ScatterChannel[] Channels = new ScatterChannel[0];
			/// <summary>Blocks per side, and the half-float slices: channels 0..3, then 4..7 (empty with four or fewer).</summary>
			public int Blocks;
			public ushort[] Slice0 = new ushort[0];
			public ushort[] Slice1 = new ushort[0];
			/// <summary>Per work item and channel, the exact upper bound on its instances; per item, its ground height range.</summary>
			public int[] ItemBounds = new int[0];
			public float[] ItemMin = new float[0];
			public float[] ItemMax = new float[0];
			public int ClampedBlocks;
			public int ClampedChannel = -1;
			/// <summary>The painted holes (R8 on the heights' grid, 0 a hole); empty when it has none.</summary>
			public byte[] Holes = new byte[0];
		}

		[Serializable]
		public struct ScatterChannel
		{
			public float MinWidth, MaxWidth, MinHeight, MaxHeight, NoiseSpread, AlignToGround;
			public int Seed;
		}

		/// <summary>
		/// A terrain texture set's albedo copy for the grass's colour (GrassAlbedoTexels): every layer's mip pyramid, as the
		/// runtime read it back off the GPU. The per-layer header (tiling and tint) is the set's own, written at load.
		/// </summary>
		[Serializable]
		public sealed class Albedo
		{
			public Texture2DArray Source;
			public int Layers, Width, Mips, LayerTexels;
			public uint[] Pyramids = new uint[0];
		}

		/// <summary>
		/// The ground colour map (GroundColourMap) over all of the scene's terrains: per texel the splat-weighted mean colour of
		/// the layers as the terrain draws them, and the surface normal; with what it was made from, so the client uses it
		/// only when the same terrains stand in the same places drawn with the same texture sets.
		/// </summary>
		[Serializable]
		public sealed class GroundColour
		{
			public bool Baked;
			/// <summary>Set in the editor when a terrain's heights or textures were edited after the bake.</summary>
			public bool Stale;
			/// <summary>The map's rect (world xz), metres per texel and size.</summary>
			public Rect Bounds;
			public float Texel;
			public int Width, Height;
			/// <summary>RGBA8 texels, row by row: the colour (sRGB, alpha the coverage) and the normal (linear, rgb n·0.5+0.5).</summary>
			public byte[] Colour = new byte[0];
			public byte[] Normals = new byte[0];
			/// <summary>
			/// RGBA8 texels: what the ground there is made of for the planet's looks (r living, g lichen, b mineral, by the
			/// splat weights; a coverage). The colour above is the terrain's own; the client wears the world's looks over it by
			/// these at load (GroundColourMap.WearLooks), so the bake depends on no world and holds for the looks on or off.
			/// Empty in a bake made before it: the client then works the map out itself.
			/// </summary>
			public byte[] Shares = new byte[0];
			/// <summary>The terrains it covers, where each stood, and the texture set each was drawn with (its albedo array and a key of its tints; null and 0 for none).</summary>
			public TerrainData[] Terrains = new TerrainData[0];
			public Vector3[] Positions = new Vector3[0];
			public Texture2DArray[] Albedos = new Texture2DArray[0];
			public int[] SetKeys = new int[0];
		}

		/// <summary>This bake's entry for a terrain's data, or null.</summary>
		public Tile TileOf(TerrainData data)
		{
			if (data == null)
			{
				return null;
			}
			foreach (Tile tile in Tiles)
			{
				if (tile != null && tile.Data == data)
				{
					return tile;
				}
			}
			return null;
		}
	}
}
