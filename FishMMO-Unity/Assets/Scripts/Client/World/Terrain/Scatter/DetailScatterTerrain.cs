using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// One mesh detail prefab as the GPU detail scatter draws it, shared by every terrain that uses it: the root's mesh
	/// and material (drawn through the material's procedural-instancing twin), its renderer settings, its bounding
	/// sphere and foot, and its salt (<see cref="DetailScatterMath.Salt"/>).
	/// </summary>
	public sealed class DetailScatterType
	{
		public GameObject Prefab;
		public int Index;
		public string Name;
		public uint Salt;
		public Mesh Mesh;
		/// <summary>
		/// The meshes it draws by distance: <see cref="Mesh"/> first, then the prefab's <c>LOD1</c> and <c>LOD2</c> children
		/// (bare MeshFilters, one sub-mesh each) when it has them. One for every type but a bush.
		/// </summary>
		public Mesh[] Levels;
		/// <summary>
		/// The meshes its instances cycle, one each by its hash (2026-10-26, <see cref="DetailScatterMath.Variant"/>): <see cref="Mesh"/>
		/// first, then the prefab's <c>Variant1</c>, <c>Variant2</c> children (bare MeshFilters, one sub-mesh each,
		/// <see cref="DetailScatterSettings.VariantChildPrefix"/>) when it has them. One for a bush or any type with levels: a
		/// type's variants take the slots its levels would.
		/// </summary>
		public Mesh[] Variants;
		/// <summary>
		/// A bush (<see cref="DetailScatterSettings.BushPrefix"/>): cover, the same for every player. Never thinned, never
		/// scaled by a density setting, drawn to a reach set by its own height rather than by the detail distance.
		/// </summary>
		public bool Bush;
		/// <summary>The top of its tallest level at scale 1, metres (a bush's reach and level steps go by its height).</summary>
		public float MeshTop;
		public Material Material;
		/// <summary>Layer, receive shadows, reflection probes, rendering layer mask; shadow casting and camera are set per draw.</summary>
		public RenderParams Template;
		public bool CastsShadows;
		/// <summary>The bounds of every level together, at scale 1.</summary>
		public Bounds MeshBounds;
		/// <summary>How far its foot reaches from its root at scale 1, m (what hangs off a slope: the chunk renderer's rule).</summary>
		public float FootRadius;

		/// <summary>Reads a detail prototype, or returns null with the reason the scatter cannot draw it (it is then not drawn: an error, GPU only).</summary>
		public static DetailScatterType Build(DetailPrototype prototype, int layer, out string reason)
		{
			reason = null;
			GameObject prefab = prototype?.prototype;
			if (prototype == null || !prototype.usePrototypeMesh || prefab == null)
			{
				reason = "not a mesh detail prototype";
				return null;
			}
			MeshFilter filter = prefab.GetComponent<MeshFilter>();
			MeshRenderer renderer = prefab.GetComponent<MeshRenderer>();
			Material material = renderer != null ? renderer.sharedMaterial : null;
			if (filter == null || filter.sharedMesh == null || material == null)
			{
				reason = $"'{prefab.name}' has no root MeshFilter mesh and MeshRenderer material";
				return null;
			}
			if (filter.sharedMesh.subMeshCount != 1)
			{
				reason = $"'{prefab.name}' has {filter.sharedMesh.subMeshCount} sub-meshes; a detail draws one";
				return null;
			}
			if (TerrainGpuRenderer.IndirectShaderFor(material) == null)
			{
				reason = $"'{prefab.name}' material '{material.name}' ({material.shader?.name}) has no procedural-instancing twin";
				return null;
			}
			var levels = new List<Mesh> { filter.sharedMesh };
			Bounds bounds = filter.sharedMesh.bounds;
			// The reduced levels, in order, while they are there; one with more than one sub-mesh ends the list.
			for (int lod = 1; lod < DetailScatterSettings.MaxLevels; lod++)
			{
				Transform child = prefab.transform.Find("LOD" + lod);
				Mesh mesh = child != null && child.TryGetComponent(out MeshFilter levelFilter) ? levelFilter.sharedMesh : null;
				if (mesh == null || mesh.subMeshCount != 1)
				{
					break;
				}
				levels.Add(mesh);
				bounds.Encapsulate(mesh.bounds);
			}
			bool bush = DetailScatterSettings.IsBush(prefab.name);
			// The meshes it cycles per instance, in order, while they are there (a type of one level only: never a bush).
			var variants = new List<Mesh> { filter.sharedMesh };
			if (!bush && levels.Count == 1)
			{
				for (int v = 1; v < DetailScatterSettings.MaxVariants; v++)
				{
					Transform child = prefab.transform.Find(DetailScatterSettings.VariantChildPrefix + v);
					Mesh mesh = child != null && !child.TryGetComponent(out Renderer _) && child.TryGetComponent(out MeshFilter variantFilter) ? variantFilter.sharedMesh : null;
					if (mesh == null || mesh.subMeshCount != 1)
					{
						break;
					}
					variants.Add(mesh);
					bounds.Encapsulate(mesh.bounds);
				}
			}
			return new DetailScatterType
			{
				Prefab = prefab,
				Name = prefab.name,
				Salt = DetailScatterMath.Salt(prefab.name),
				Mesh = filter.sharedMesh,
				Levels = levels.ToArray(),
				Variants = variants.ToArray(),
				Bush = bush,
				MeshTop = Mathf.Max(0.05f, bounds.max.y),
				Material = material,
				CastsShadows = renderer.shadowCastingMode != ShadowCastingMode.Off,
				MeshBounds = bounds,
				FootRadius = DetailPrototypeSettings.FootRadiusOf(prototype),
				Template = new RenderParams(material)
				{
					layer = layer >= 0 ? layer : prefab.layer,
					receiveShadows = renderer.receiveShadows,
					reflectionProbeUsage = renderer.reflectionProbeUsage,
					renderingLayerMask = renderer.renderingLayerMask,
					lightProbeUsage = LightProbeUsage.Off,
					motionVectorMode = MotionVectorGenerationMode.Camera,
				},
			};
		}
	}

	/// <summary>
	/// The per-prototype placement settings, read once from a detail prototype (the CPU chunk renderer's, kept when it was
	/// removed on 2026-10-27: the scatter averages them per channel and slope-sinks by the foot's reach).
	/// </summary>
	public struct DetailPrototypeSettings
	{
		/// <summary>How far the plant's foot reaches from its root, m at scale 1: the widest of its lowest sixth (FootRadiusOf).</summary>
		public float FootRadius;
		public int Seed;
		public float Jitter;
		public float NoiseSpread;
		public float MinWidth, MaxWidth, MinHeight, MaxHeight;
		public float AlignToGround;
		public bool UseDensityScaling;

		public static DetailPrototypeSettings From(DetailPrototype p)
		{
			return new DetailPrototypeSettings
			{
				Seed = p.noiseSeed,
				Jitter = p.positionJitter,
				NoiseSpread = Mathf.Max(1e-4f, p.noiseSpread),
				MinWidth = p.minWidth,
				MaxWidth = Mathf.Max(p.minWidth, p.maxWidth),
				MinHeight = p.minHeight,
				MaxHeight = Mathf.Max(p.minHeight, p.maxHeight),
				AlignToGround = Mathf.Clamp01(p.alignToGround),
				UseDensityScaling = p.useDensityScaling,
				FootRadius = FootRadiusOf(p),
			};
		}

		/// <summary>Each mesh's foot reach, worked out once (a terrain lists the same prefab once per biome rule; the main thread's).</summary>
		private static readonly Dictionary<Mesh, float> footRadii = new Dictionary<Mesh, float>();

		/// <summary>
		/// How far a prototype's foot reaches from its root, m: the furthest any vertex in the lowest sixth of
		/// its height stands from the root across the ground — what hangs off a slope. From the vertices where
		/// the mesh can be read, half its bounds' width where it cannot; 0 for a texture detail.
		/// </summary>
		public static float FootRadiusOf(DetailPrototype p)
		{
			if (p == null || !p.usePrototypeMesh || p.prototype == null)
			{
				return 0f;
			}
			MeshFilter filter = p.prototype.GetComponentInChildren<MeshFilter>();
			Mesh mesh = filter != null ? filter.sharedMesh : null;
			if (mesh == null)
			{
				return 0f;
			}
			if (footRadii.TryGetValue(mesh, out float cached))
			{
				return cached;
			}
			float reach;
			if (!mesh.isReadable)
			{
				reach = 0.5f * Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
			}
			else
			{
				Vector3[] vertices = mesh.vertices;
				float lowest = float.MaxValue, highest = float.MinValue;
				foreach (Vector3 v in vertices)
				{
					lowest = Mathf.Min(lowest, v.y);
					highest = Mathf.Max(highest, v.y);
				}
				float foot = lowest + (highest - lowest) * 0.16f;
				reach = 0f;
				foreach (Vector3 v in vertices)
				{
					if (v.y <= foot)
					{
						reach = Mathf.Max(reach, Mathf.Sqrt(v.x * v.x + v.z * v.z));
					}
				}
			}
			footRadii[mesh] = reach;
			return reach;
		}
	}

	/// <summary>
	/// One terrain as the GPU detail scatter reads it: its scattered prototypes' detail layers summed into 2 m blocks of
	/// exact expected counts (<see cref="DetailScatterMath"/>), one channel per TYPE painted on it (every prototype of a
	/// prefab adds into it, as the blade grass does), kept SPARSE (2026-10-27): only the painted pairs of block and channel,
	/// each with the block's painted cells (<see cref="DetailScatterDensity"/>); each channel's placement settings (the
	/// prototypes' own, averaged by how much each contributes); the terrain's own height texture; and, per work item (8 × 8
	/// blocks), an exact upper bound on each channel's instances and the ground's height range, so the CPU sizes the slots
	/// and culls the items without reading the GPU. Built once when the terrain is taken, released with it.
	/// </summary>
	/// <remarks>
	/// Sparse because every mesh kind is scattered now (the CPU chunk renderer is gone): Moron 2's terrains paint up to 63
	/// mesh kinds but a 2 m block holds 2.4 of them on average (11 at most), so the 6.1 M painted pairs of its 16 terrains
	/// take 24 MB, where the two dense half-float slices of eight channels a terrain took 72 MB.
	/// </remarks>
	public sealed class DetailScatterTerrain : IDisposable
	{
		/// <summary>One channel's placement: the prototypes' sizes, noise, seed and ground alignment.</summary>
		public struct Channel
		{
			public int Type;
			public float MinWidth, MaxWidth, MinHeight, MaxHeight;
			public float NoiseSpread;
			public int Seed;
			public float AlignToGround;
			/// <summary>
			/// The channel's scatter modes (2026-10-26, SCATTER-MODES): the two words of <see cref="FishMMO.Shared.Biomes.BiomeScatterModeWord"/>
			/// (facing, lean, power-law size), from the terrain's <see cref="FishMMO.Shared.TerrainScatterModes"/> record; 0 and 0 (every
			/// mode off, the scatter exactly as before) when it has none.
			/// </summary>
			public uint ModeWord, ModeAngles;
		}

		public Terrain Terrain;
		public TerrainData Data;
		public Vector3 Origin;
		public Vector3 Size;
		public int Channels;
		/// <summary>Per channel (<see cref="Channels"/> of them).</summary>
		public Channel[] ChannelSettings = Array.Empty<Channel>();
		/// <summary>The prototypes read into the channels.</summary>
		public int Layers;
		/// <summary>The prototypes the scatter draws (by prototype index).</summary>
		public bool[] Skip;
		/// <summary>The density scale the counts were built at (a quality change rebuilds the terrain).</summary>
		public float DensityScale;
		public int HeightResolution;
		/// <summary>Blocks per side, block size (m) along x and z, and the world index of block 0 on each axis.</summary>
		public int Blocks;
		public float BlockX, BlockZ;
		public int WorldBlockX, WorldBlockZ;
		/// <summary>Detail cells per block side (DetailScatterMath.BlockFactor): what an entry's painted-cell mask means.</summary>
		public int Factor;
		/// <summary>Work items per side.</summary>
		public int ItemsSide;
		/// <summary>The sparse density (<see cref="DetailScatterDensity"/>): per item its first entry (and the total), its blocks' starts, the entries.</summary>
		public int[] ItemEntryStart = Array.Empty<int>();
		public ushort[] BlockStarts = Array.Empty<ushort>();
		public uint[] Entries = Array.Empty<uint>();
		/// <summary>Per item, its painted channels' exact upper bounds (<see cref="DetailScatterDensity.ItemBounds"/>): pairs [start[i], start[i + 1]).</summary>
		public int[] ItemBoundStart = Array.Empty<int>();
		public ushort[] ItemBoundChannel = Array.Empty<ushort>();
		public int[] ItemBoundValue = Array.Empty<int>();
		private float[] itemMin, itemMax;
		/// <summary>The tallest an instance can stand above its root, and the farthest it can reach sideways, m (item bounds).</summary>
		public float MaxInstanceHeight, MaxInstanceReach;
		/// <summary>True when any channel's type casts shadows.</summary>
		public bool CastsShadows;
		/// <summary>Blocks whose expected count was over <see cref="DetailScatterMath.MaxPerBlock"/> and was capped, and the first such channel (−1 none).</summary>
		public int ClampedBlocks;
		public int ClampedChannel = -1;

		/* The scatter's own copy of the terrain's heights, as the blade grass keeps one (GrassTerrain): Unity's
		 * heightmapTexture is not the same on every graphics API. */
		private Texture2D heightTexture;
		private ushort[] heightTexels16;
		private float[] heightTexels32;

		public Texture Heightmap => heightTexture;

		/// <summary>
		/// The terrain's painted holes (<see cref="TerrainHoleMask"/>) on the heights' grid: R8, texel (x, z) = heightmap
		/// cell (x, z), 0 a hole, 1 solid; null when it has none (the atlas gives it a solid slice). The 2 m blocks reach
		/// past a hole's edge, and the detail layers under a cave mouth keep what was painted before it was cut, so the
		/// generate kernel tests each instance's own cell.
		/// </summary>
		public Texture2D Holes;

		/// <summary>Metres per unit of <see cref="Heightmap"/> (it holds 0..1 of the terrain's height).</summary>
		public float HeightScale => Size.y;

		/// <summary>True when the terrain scatters anything (it has entries).</summary>
		public bool Any => Entries.Length > 0;

		/// <summary>The ground's height range under a work item (lo, hi), world y.</summary>
		public Vector2 ItemHeightRange(int itemX, int itemZ)
		{
			int i = itemZ * ItemsSide + itemX;
			return itemMin[i] <= itemMax[i] ? new Vector2(itemMin[i], itemMax[i]) : new Vector2(Origin.y, Origin.y + Size.y);
		}

		/// <summary>Starts building a terrain's blocks a strip at a time; null when there is nothing to build.</summary>
		/// <param name="prototypes">The terrain's scattered prototype indices.</param>
		/// <param name="types">Each prototype's type (parallel to <paramref name="prototypes"/>).</param>
		public static Builder Begin(Terrain terrain, List<int> prototypes, List<DetailScatterType> types, float densityScale)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			return prototypes.Count == 0 || data == null ? null : new Builder(terrain, data, prototypes, types, densityScale);
		}

		/// <summary>
		/// A terrain's scatter under construction: <see cref="Step"/> reads a strip of whole item rows of every painted layer
		/// (<see cref="DetailScatterMath.StripRows"/>), one layer at a time until the frame's budget is spent, and a worker sums
		/// each strip into blocks and writes its items' entries once all its layers are in; then the heights, the holes and the
		/// channels' settings. Nothing of the terrain's scattered kinds draws until it is <see cref="Done"/>.
		/// </summary>
		public sealed class Builder
		{
			public readonly Terrain Terrain;
			public DetailScatterTerrain Result { get; private set; }
			public bool Done { get; private set; }
			public double Milliseconds { get; private set; }
			public int Frames { get; private set; }

			public string Progress => row < res ? $"reading rows {row}/{res}" : heightRow < st.HeightResolution ? "heights" : holes == null || !holes.Complete ? "holes" : "packing";

			/// <summary>True when the build gave up on a worker's exception (logged): the terrain is taken again from the start.</summary>
			public bool Failed { get; private set; }

			/// <summary>Strips read ahead of the worker at most (a strip of one layer each): past it the main thread waits a frame (bounded memory).</summary>
			public const int MaxPendingStrips = 8;

			/// <summary>
			/// The worker's queue (<see cref="TerrainBuildQueue"/>): each strip's sums, its items' entries and the height texels, in
			/// the order read, so the counts and heights are bit for bit the same wherever they are worked out.
			/// </summary>
			private readonly TerrainBuildQueue queue = new TerrainBuildQueue();
			/// <summary>The channels' settings and the items' bounds (a worker's), once everything is read.</summary>
			private Task<Slices> packing;

			private readonly DetailScatterTerrain st;
			private readonly List<int> layerPrototypes = new List<int>(), layerChannels = new List<int>();
			/// <summary>Per prototype index, its place in <see cref="layerPrototypes"/> (−1 not read).</summary>
			private readonly int[] layerOf;
			private readonly DetailPrototypeSettings[] prototypeSettings;
			/// <summary>Per channel, whether its type is a bush (no density scale: cover is the same for everyone).</summary>
			private readonly bool[] channelBush;
			private readonly float[] coverage;
			private readonly double[] prototypeTotals;
			private readonly int res, factor, stripRows, stripBlocks;
			private readonly bool coverageMode;
			private readonly float cellArea;
			/// <summary>The strip being summed (the worker's): per channel each block's expected count and painted cells, null until painted.</summary>
			private readonly float[][] window;
			private readonly byte[][] windowCells;
			private readonly DetailScatterDensity density;
			/// <summary>The strip's first row, and the layers of it still to read (painted ones only: GetSupportedLayers).</summary>
			private int row, stripLayer = -1;
			private readonly List<int> stripLayers = new List<int>();
			private int heightRow;
			/// <summary>The terrain's painted holes, read a strip at a time after the heights; null until then.</summary>
			private TerrainHoleMask holes;
			/// <summary>Each channel's prefab (its type's): what a bake records, and what one must match to be taken.</summary>
			private readonly GameObject[] channelPrefabs;

			/// <summary>
			/// Set before the first <see cref="Step"/> (the editor's bake, TerrainDetailBaker): the maps are kept as they are made,
			/// in <see cref="Captured"/> and <see cref="CapturedHeights"/>, once <see cref="Done"/>.
			/// </summary>
			public bool Capture;
			public FishMMO.Shared.TerrainDetailBake.Scatter Captured { get; private set; }
			/// <summary>The heights as the build stored them (16 bits, 0..1 of the terrain's height), for the bake.</summary>
			public ushort[] CapturedHeights { get; private set; }

			/// <summary>Types past <see cref="DetailScatterMath.MaxChannels"/> on this terrain (not drawn: an error, logged by the system).</summary>
			public readonly List<string> Overflow = new List<string>();

			internal Builder(Terrain terrain, TerrainData data, List<int> prototypes, List<DetailScatterType> types, float densityScale)
			{
				Terrain = terrain;
				var channelTypes = new List<DetailScatterType>();
				DetailPrototype[] all = data.detailPrototypes;
				layerOf = new int[all.Length];
				for (int i = 0; i < layerOf.Length; i++)
				{
					layerOf[i] = -1;
				}
				for (int i = 0; i < prototypes.Count; i++)
				{
					int c = channelTypes.IndexOf(types[i]);
					if (c < 0)
					{
						if (channelTypes.Count >= DetailScatterMath.MaxChannels)
						{
							if (!Overflow.Contains(types[i].Name))
							{
								Overflow.Add(types[i].Name);
							}
							continue;
						}
						c = channelTypes.Count;
						channelTypes.Add(types[i]);
					}
					layerOf[prototypes[i]] = layerPrototypes.Count;
					layerPrototypes.Add(prototypes[i]);
					layerChannels.Add(c);
				}
				res = Mathf.Max(1, data.detailResolution);
				float cellX = data.size.x / res, cellZ = data.size.z / res;
				cellArea = cellX * cellZ;
				factor = DetailScatterMath.BlockFactor(cellX);
				stripRows = DetailScatterMath.StripRows(factor);
				stripBlocks = stripRows / factor;
				coverageMode = data.detailScatterMode == DetailScatterMode.CoverageMode;
				prototypeSettings = new DetailPrototypeSettings[all.Length];
				coverage = new float[all.Length];
				prototypeTotals = new double[all.Length];
				foreach (int p in layerPrototypes)
				{
					prototypeSettings[p] = DetailPrototypeSettings.From(all[p]);
					if (coverageMode)
					{
						float c = data.ComputeDetailCoverage(p);
						coverage[p] = float.IsNaN(c) || c < 0f ? 0f : c;
					}
				}
				Vector3 origin = terrain.GetPosition();
				int blocks = DetailScatterMath.Blocks(res, factor);
				st = new DetailScatterTerrain
				{
					Terrain = terrain,
					Data = data,
					Origin = origin,
					Size = data.size,
					Channels = channelTypes.Count,
					ChannelSettings = new Channel[channelTypes.Count],
					Layers = layerPrototypes.Count,
					Skip = new bool[all.Length],
					DensityScale = densityScale,
					HeightResolution = data.heightmapResolution,
					Blocks = blocks,
					BlockX = cellX * factor,
					BlockZ = cellZ * factor,
					WorldBlockX = DetailScatterMath.WorldBlockBase(origin.x, cellX * factor),
					WorldBlockZ = DetailScatterMath.WorldBlockBase(origin.z, cellZ * factor),
					Factor = factor,
					ItemsSide = DetailScatterMath.Items(blocks),
				};
				channelBush = new bool[channelTypes.Count];
				channelPrefabs = new GameObject[channelTypes.Count];
				for (int c = 0; c < channelTypes.Count; c++)
				{
					DetailScatterType type = channelTypes[c];
					channelPrefabs[c] = type.Prefab;
					st.ChannelSettings[c].Type = type.Index;
					st.CastsShadows |= type.CastsShadows;
					channelBush[c] = type.Bush;
				}
				foreach (int p in layerPrototypes)
				{
					st.Skip[p] = true;
				}
				bool leans = ReadModes(terrain, all);
				window = new float[st.Channels][];
				windowCells = new byte[st.Channels][];
				density = new DetailScatterDensity(blocks, st.Channels, factor);
				// The tallest and widest an instance can be: its mesh at the largest scale any of its prototypes allows.
				for (int i = 0; i < layerPrototypes.Count; i++)
				{
					DetailPrototypeSettings s = prototypeSettings[layerPrototypes[i]];
					Bounds b = channelTypes[layerChannels[i]].MeshBounds;
					float w = Mathf.Max(s.MaxWidth, 0.01f), h = Mathf.Max(s.MaxHeight, 0.01f);
					st.MaxInstanceHeight = Mathf.Max(st.MaxInstanceHeight, b.max.y * h);
					st.MaxInstanceReach = Mathf.Max(st.MaxInstanceReach, Mathf.Max(b.extents.x + Mathf.Abs(b.center.x), b.extents.z + Mathf.Abs(b.center.z)) * w);
				}
				if (leans)
				{
					// A leaning instance can lay its height sideways: the items' bounds reach that far.
					st.MaxInstanceReach = Mathf.Max(st.MaxInstanceReach, st.MaxInstanceReach + st.MaxInstanceHeight);
				}
			}

			/// <summary>
			/// Each channel's mode words from the terrain's <see cref="FishMMO.Shared.TerrainScatterModes"/> record (SCATTER-MODES): the first of
			/// the channel's prototypes the record names (by prefab name and noise seed, as the cut wrote it). Nothing is set with no record.
			/// True when any channel leans its instances.
			/// </summary>
			private bool ReadModes(Terrain terrain, DetailPrototype[] all)
			{
				FishMMO.Shared.TerrainScatterModes record = terrain != null ? terrain.GetComponent<FishMMO.Shared.TerrainScatterModes>() : null;
				if (record == null)
				{
					return false;
				}
				bool leans = false;
				for (int i = 0; i < layerPrototypes.Count; i++)
				{
					int p = layerPrototypes[i], c = layerChannels[i];
					if (st.ChannelSettings[c].ModeWord != 0u || p < 0 || p >= all.Length || all[p] == null || all[p].prototype == null)
					{
						continue;
					}
					if (record.TryDetail(all[p].prototype.name, all[p].noiseSeed, out uint word, out uint angles))
					{
						st.ChannelSettings[c].ModeWord = word;
						st.ChannelSettings[c].ModeAngles = angles;
						leans |= FishMMO.Shared.Biomes.BiomeScatterModeWord.Leans(word);
					}
				}
				return leans;
			}

			/// <summary>Drops an unfinished build, freeing anything it created; its queued work is skipped.</summary>
			public void Abandon()
			{
				queue.Cancel();
				if (!Done || Result == null)
				{
					st.Dispose();
				}
				Done = true;
				Result = null;
			}

			/// <summary>A bake entry with this build's prototypes, channels and their prefabs, at its density scale; <paramref name="present"/> whether it scatters anything.</summary>
			private FishMMO.Shared.TerrainDetailBake.Scatter Layout(bool present)
			{
				return new FishMMO.Shared.TerrainDetailBake.Scatter
				{
					Baked = true,
					Present = present,
					DensityScale = st.DensityScale,
					LayerPrototypes = layerPrototypes.ToArray(),
					LayerChannels = layerChannels.ToArray(),
					ChannelPrefabs = (GameObject[])channelPrefabs.Clone(),
				};
			}

			/// <summary>The finished density, items' ranges, channel settings and heights into the bake (before the textures take them).</summary>
			private void CaptureMaps(Slices packed)
			{
				Captured = Layout(true);
				Captured.Blocks = st.Blocks;
				Captured.Factor = st.Factor;
				Captured.ItemEntryStart = (int[])st.ItemEntryStart.Clone();
				Captured.BlockStarts = (ushort[])st.BlockStarts.Clone();
				Captured.Entries = (uint[])st.Entries.Clone();
				Captured.Holes = packed.Holes ?? new byte[0];
				Captured.ItemMin = (float[])st.itemMin.Clone();
				Captured.ItemMax = (float[])st.itemMax.Clone();
				Captured.ClampedBlocks = st.ClampedBlocks;
				Captured.ClampedChannel = st.ClampedChannel;
				Captured.Channels = new FishMMO.Shared.TerrainDetailBake.ScatterChannel[st.Channels];
				for (int c = 0; c < st.Channels; c++)
				{
					Channel ch = st.ChannelSettings[c];
					Captured.Channels[c] = new FishMMO.Shared.TerrainDetailBake.ScatterChannel
					{
						MinWidth = ch.MinWidth, MaxWidth = ch.MaxWidth, MinHeight = ch.MinHeight, MaxHeight = ch.MaxHeight,
						NoiseSpread = ch.NoiseSpread, AlignToGround = ch.AlignToGround, Seed = ch.Seed,
					};
				}
				if (st.heightTexels16 != null)
				{
					CapturedHeights = (ushort[])st.heightTexels16.Clone();
				}
				else if (st.heightTexels32 != null)
				{
					CapturedHeights = new ushort[st.heightTexels32.Length];
					for (int i = 0; i < CapturedHeights.Length; i++)
					{
						CapturedHeights[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt(st.heightTexels32[i] * 65535f), 0, 65535);
					}
				}
			}

			/// <summary>Whether a bake was made from the same prototypes, read into the same channels, of the same prefabs.</summary>
			private bool SameLayout(FishMMO.Shared.TerrainDetailBake.Scatter b)
			{
				if (b.LayerPrototypes == null || b.LayerChannels == null || b.ChannelPrefabs == null || b.LayerPrototypes.Length != layerPrototypes.Count
					|| b.LayerChannels.Length != layerChannels.Count || b.ChannelPrefabs.Length != channelPrefabs.Length)
				{
					return false;
				}
				for (int i = 0; i < layerPrototypes.Count; i++)
				{
					if (b.LayerPrototypes[i] != layerPrototypes[i] || b.LayerChannels[i] != layerChannels[i])
					{
						return false;
					}
				}
				for (int c = 0; c < channelPrefabs.Length; c++)
				{
					if (b.ChannelPrefabs[c] != channelPrefabs[c])
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>
			/// Takes the terrain from its baked maps instead of reading it (<see cref="FishMMO.Shared.TerrainDetailBake"/>): true,
			/// and <see cref="Done"/>, when the bake was made from what this build would read (the same prototypes in the same
			/// channels of the same prefabs, the same density scale and resolutions); false, and the builder untouched, when not.
			/// The channels' types (their indices) are this builder's own.
			/// </summary>
			public bool TakeBaked(FishMMO.Shared.TerrainDetailBake.Tile tile)
			{
				FishMMO.Shared.TerrainDetailBake.Scatter b = tile?.Scatter;
				TerrainData data = st.Data;
				if (Done || b == null || !b.Baked || data == null || b.DensityScale != st.DensityScale || !SameLayout(b))
				{
					return false;
				}
				if (!b.Present)
				{
					st.Dispose();
					Done = true;
					Result = null;
					return true;
				}
				int blocks = st.Blocks, hres = st.HeightResolution, items = st.ItemsSide * st.ItemsSide;
				if (b.Blocks != blocks || b.Factor != st.Factor || b.ItemEntryStart == null || b.ItemEntryStart.Length != items + 1
					|| b.BlockStarts == null || b.BlockStarts.Length != items * (DetailScatterMath.BlocksPerItem + 1)
					|| b.Entries == null || b.Entries.Length != b.ItemEntryStart[items]
					|| b.ItemMin == null || b.ItemMin.Length != items || b.ItemMax == null || b.ItemMax.Length != items
					|| b.Channels == null || b.Channels.Length != st.Channels
					|| tile.HeightResolution != hres || tile.Heights == null || tile.Heights.Length != hres * hres)
				{
					return false;
				}
				for (int c = 0; c < st.Channels; c++)
				{
					FishMMO.Shared.TerrainDetailBake.ScatterChannel baked = b.Channels[c];
					ref Channel ch = ref st.ChannelSettings[c];
					ch.MinWidth = baked.MinWidth;
					ch.MaxWidth = baked.MaxWidth;
					ch.MinHeight = baked.MinHeight;
					ch.MaxHeight = baked.MaxHeight;
					ch.NoiseSpread = baked.NoiseSpread;
					ch.AlignToGround = baked.AlignToGround;
					ch.Seed = baked.Seed;
				}
				st.ItemEntryStart = b.ItemEntryStart;
				st.BlockStarts = b.BlockStarts;
				st.Entries = b.Entries;
				st.FinishBounds();
				st.itemMin = b.ItemMin;
				st.itemMax = b.ItemMax;
				st.ClampedBlocks = b.ClampedBlocks;
				st.ClampedChannel = b.ClampedChannel;
				if (SystemInfo.SupportsTextureFormat(TextureFormat.R16))
				{
					st.heightTexels16 = tile.Heights;
				}
				else
				{
					st.heightTexels32 = new float[tile.Heights.Length];
					for (int i = 0; i < tile.Heights.Length; i++)
					{
						st.heightTexels32[i] = tile.Heights[i] / 65535f;
					}
				}
				st.FinishHeightTexture();
				if (b.Holes != null && b.Holes.Length == hres * hres)
				{
					st.Holes = MakeHoles(b.Holes, hres, Terrain.name);
				}
				Result = st;
				Done = true;
				return true;
			}

			/// <summary>Profiler marker of the main thread's share of a step (the reads, the textures).</summary>
			private static readonly Unity.Profiling.ProfilerMarker StepMarker = new Unity.Profiling.ProfilerMarker("DetailScatterTerrain.Builder.Step");

			/// <summary>
			/// Works for at most <paramref name="budgetMs"/> of the main thread's time, or until it waits on the worker; true
			/// once finished (<see cref="Result"/> null: nothing to scatter there, or <see cref="Failed"/>).
			/// </summary>
			public bool Step(double budgetMs)
			{
				if (Done)
				{
					return true;
				}
				using (StepMarker.Auto())
				{
					Frames++;
					var watch = System.Diagnostics.Stopwatch.StartNew();
					while (!Done && Advance() && watch.Elapsed.TotalMilliseconds < budgetMs)
					{
					}
					Milliseconds += watch.Elapsed.TotalMilliseconds;
				}
				return Done;
			}

			private float Density(int prototype) => prototypeSettings[prototype].UseDensityScaling ? st.DensityScale : 1f;

			/// <summary>
			/// One strip of a detail layer (rows <paramref name="rowBase"/>..) summed into a channel's blocks of the strip's window
			/// (block rows from <paramref name="blockRowBase"/>): each cell's expected count as the chunk renderer worked it out
			/// (coverage or instance count, density scale), in order, and each painted cell's bit in its block's mask
			/// (<paramref name="cells"/>, null to keep none). Returns the strip's total (summed in doubles, in order).
			/// </summary>
			public static double AddStrip(int[,] map, int rows, int rowBase, int res, int factor, int blocks, int blockRowBase, bool coverageMode, float coverage,
				float cellArea, float density, float[] target, byte[] cells)
			{
				double total = 0.0;
				for (int z = 0; z < rows; z++)
				{
					int gz = rowBase + z;
					int blockRow = (gz / factor - blockRowBase) * blocks;
					int cz = gz % factor;
					for (int x = 0; x < res; x++)
					{
						int v = map[z, x];
						if (v <= 0)
						{
							continue;
						}
						float e = TerrainDetailMath.ExpectedInCell(v, coverageMode, coverage, cellArea, density);
						if (e > 0f)
						{
							int at = blockRow + x / factor;
							target[at] += e;
							total += e;
							if (cells != null)
							{
								cells[at] |= (byte)DetailScatterMath.CellBit(x % factor, cz, factor);
							}
						}
					}
				}
				return total;
			}

			/// <summary>One step of the main thread's share; false when it is waiting on the worker.</summary>
			private bool Advance()
			{
				TerrainData data = st.Data;
				if (data == null)
				{
					Done = true;
					return true;
				}
				Exception fault = queue.Fault ?? packing?.Exception;
				if (fault != null)
				{
					Debug.LogException(fault);
					Debug.LogWarning($"[Detail scatter] Building '{(Terrain != null ? Terrain.name : "terrain")}' failed on a worker; it is taken again.");
					Abandon();
					Failed = true;
					return true;
				}
				if (row < res)
				{
					if (queue.Pending >= MaxPendingStrips)
					{
						return false;
					}
					int rows = Mathf.Min(stripRows, res - row);
					if (stripLayer < 0)
					{
						// The strip's painted layers of ours, in the order they were listed (GetSupportedLayers: what any patch holds).
						stripLayers.Clear();
						foreach (int p in data.GetSupportedLayers(0, row, res, rows))
						{
							if (p >= 0 && p < layerOf.Length && layerOf[p] >= 0)
							{
								stripLayers.Add(layerOf[p]);
							}
						}
						stripLayers.Sort();
						stripLayer = 0;
					}
					if (stripLayer < stripLayers.Count)
					{
						int layer = stripLayers[stripLayer++];
						int p = layerPrototypes[layer];
						int c = layerChannels[layer];
						int[,] cells = data.GetDetailLayer(0, row, res, rows, p);
						int blocks = st.Blocks, rowBase = row, blockRowBase = row / factor;
						float density = channelBush[c] ? 1f : Density(p);
						float cover = coverage[p];
						bool masks = DetailScatterMath.MaskCells(factor) > 0;
						queue.Enqueue(() =>
						{
							window[c] ??= new float[stripBlocks * blocks];
							if (masks)
							{
								windowCells[c] ??= new byte[stripBlocks * blocks];
							}
							prototypeTotals[p] += AddStrip(cells, rows, rowBase, res, factor, blocks, blockRowBase, coverageMode, cover, cellArea, density, window[c], masks ? windowCells[c] : null);
						});
						return true;
					}
					// Every layer of the strip is in: its item rows are written, and the window cleared for the next.
					int emitRow = row / factor, emitRows = (rows + factor - 1) / factor;
					queue.Enqueue(() =>
					{
						density.EmitRows(window, windowCells, emitRow, emitRows);
						for (int c = 0; c < window.Length; c++)
						{
							if (window[c] != null)
							{
								Array.Clear(window[c], 0, window[c].Length);
							}
							if (windowCells[c] != null)
							{
								Array.Clear(windowCells[c], 0, windowCells[c].Length);
							}
						}
					});
					row += rows;
					stripLayer = -1;
					return true;
				}
				if (heightRow == 0 && heightTexelsMissing)
				{
					// Whether anything was painted is known once the strips are summed.
					if (!queue.Idle)
					{
						return false;
					}
					if (density.Entries.Count == 0)
					{
						if (Capture)
						{
							Captured = Layout(false);
						}
						Done = true;
						return true;
					}
					BeginHeights();
					return true;
				}
				int hres = data.heightmapResolution;
				if (heightRow < hres)
				{
					if (queue.Pending >= MaxPendingStrips)
					{
						return false;
					}
					int rows = Mathf.Min(TerrainHoleMask.StripRows, hres - heightRow);
					float[,] strip = data.GetHeights(0, heightRow, hres, rows);
					int zBase = heightRow;
					queue.Enqueue(() => AccumulateHeights(strip, zBase));
					heightRow += rows;
					return true;
				}
				holes ??= new TerrainHoleMask(data);
				if (!holes.Complete)
				{
					holes.ReadStrip(data);
					return true;
				}
				if (packing == null)
				{
					if (!queue.Idle)
					{
						return false;
					}
					TerrainHoleMask cut = holes;
					packing = TerrainBuildQueue.Inline ? Task.FromResult(Pack(cut)) : Task.Run(() => Pack(cut));
					return true;
				}
				if (!packing.IsCompleted)
				{
					return false;
				}
				Finish(packing.Result);
				return true;
			}

			private bool heightTexelsMissing = true;

			private void BeginHeights()
			{
				heightTexelsMissing = false;
				int hres = st.HeightResolution;
				if (SystemInfo.SupportsTextureFormat(TextureFormat.R16))
				{
					st.heightTexels16 = new ushort[hres * hres];
				}
				else
				{
					st.heightTexels32 = new float[hres * hres];
				}
				int items = st.ItemsSide * st.ItemsSide;
				st.itemMin = new float[items];
				st.itemMax = new float[items];
				for (int i = 0; i < items; i++)
				{
					st.itemMin[i] = float.MaxValue;
					st.itemMax[i] = float.MinValue;
				}
			}

			/// <summary>Folds a strip of height samples into the height texels and the items' height ranges.</summary>
			private void AccumulateHeights(float[,] heights, int zBase)
			{
				int hres = st.HeightResolution;
				float metresPerSample = st.Size.x / Mathf.Max(1, hres - 1);
				float itemX = DetailScatterMath.ItemSide * st.BlockX, itemZ = DetailScatterMath.ItemSide * st.BlockZ;
				int side = st.ItemsSide;
				for (int r = 0; r < heights.GetLength(0); r++)
				{
					int z = zBase + r;
					float localZ = z * st.Size.z / Mathf.Max(1, hres - 1);
					for (int x = 0; x < hres; x++)
					{
						float v = heights[r, x];
						if (st.heightTexels16 != null)
						{
							st.heightTexels16[z * hres + x] = (ushort)Mathf.Clamp(Mathf.RoundToInt(v * 65535f), 0, 65535);
						}
						else
						{
							st.heightTexels32[z * hres + x] = v;
						}
						float h = st.Origin.y + v * st.Size.y;
						float localX = x * metresPerSample;
						// A sample on an item's edge bounds both items.
						int ix0 = Mathf.Clamp(Mathf.FloorToInt((localX - metresPerSample) / itemX), 0, side - 1), ix1 = Mathf.Clamp(Mathf.FloorToInt((localX + metresPerSample) / itemX), 0, side - 1);
						int iz0 = Mathf.Clamp(Mathf.FloorToInt((localZ - metresPerSample) / itemZ), 0, side - 1), iz1 = Mathf.Clamp(Mathf.FloorToInt((localZ + metresPerSample) / itemZ), 0, side - 1);
						for (int iz = iz0; iz <= iz1; iz++)
						{
							for (int ix = ix0; ix <= ix1; ix++)
							{
								int i = iz * side + ix;
								if (h < st.itemMin[i]) st.itemMin[i] = h;
								if (h > st.itemMax[i]) st.itemMax[i] = h;
							}
						}
					}
				}
			}

			/// <summary>What <see cref="Pack"/> gives the textures.</summary>
			private sealed class Slices
			{
				public byte[] Holes;
			}

			/// <summary>The mask with its plants' margin (TerrainHoleMask.KeepPlantsClear), returned for the call's convenience.</summary>
			private static TerrainHoleMask PlantsClear(TerrainHoleMask mask)
			{
				mask.KeepPlantsClear();
				return mask;
			}

			/// <summary>
			/// The channels' settings, the density and the items' bounds (and the holes' texels): a worker's, once every strip is
			/// summed and written.
			/// </summary>
			private Slices Pack(TerrainHoleMask cut)
			{
				int channels = st.Channels;
				// Each channel's placement settings: its prototypes' own, weighted by how many instances each contributes.
				var weight = new double[channels];
				var sums = new double[channels, 6];
				var seedWeight = new double[channels];
				for (int i = 0; i < layerPrototypes.Count; i++)
				{
					int p = layerPrototypes[i], c = layerChannels[i];
					double w = prototypeTotals[p];
					if (w <= 0.0)
					{
						continue;
					}
					DetailPrototypeSettings s = prototypeSettings[p];
					weight[c] += w;
					sums[c, 0] += w * s.MinWidth;
					sums[c, 1] += w * s.MaxWidth;
					sums[c, 2] += w * s.MinHeight;
					sums[c, 3] += w * s.MaxHeight;
					sums[c, 4] += w * s.NoiseSpread;
					sums[c, 5] += w * s.AlignToGround;
					// The seed of the prototype that contributes most.
					if (w > seedWeight[c])
					{
						seedWeight[c] = w;
						st.ChannelSettings[c].Seed = s.Seed;
					}
				}
				for (int c = 0; c < channels; c++)
				{
					double w = Math.Max(1e-9, weight[c]);
					ref Channel ch = ref st.ChannelSettings[c];
					ch.MinWidth = (float)(sums[c, 0] / w);
					ch.MaxWidth = Mathf.Max(ch.MinWidth, (float)(sums[c, 1] / w));
					ch.MinHeight = (float)(sums[c, 2] / w);
					ch.MaxHeight = Mathf.Max(ch.MinHeight, (float)(sums[c, 3] / w));
					ch.NoiseSpread = Mathf.Max(1e-4f, (float)(sums[c, 4] / w));
					ch.AlignToGround = Mathf.Clamp01((float)(sums[c, 5] / w));
					if (weight[c] <= 0.0)
					{
						ch.MinWidth = ch.MaxWidth = ch.MinHeight = ch.MaxHeight = 1f;
					}
				}
				st.ItemEntryStart = density.ItemEntryStart;
				st.BlockStarts = density.BlockStarts;
				st.Entries = density.Entries.ToArray();
				st.ClampedBlocks = density.ClampedBlocks;
				st.ClampedChannel = density.ClampedChannel;
				st.FinishBounds();
				return new Slices
				{
					// No stone, litter or plant over an opening, nor so near its edge that it lies out over it (2026-10-27).
					Holes = cut != null && cut.Any ? PlantsClear(cut).ToTexels(st.HeightResolution) : null,
				};
			}

			/// <summary>The height texture and the holes' texture (the main thread's).</summary>
			private void Finish(Slices packed)
			{
				if (Capture)
				{
					CaptureMaps(packed);
				}
				st.FinishHeightTexture();
				if (packed.Holes != null)
				{
					st.Holes = MakeHoles(packed.Holes, st.HeightResolution, Terrain.name);
				}
				holes = null;
				Result = st;
				Done = true;
			}
		}

		/// <summary>The items' sparse bounds from the entries (<see cref="DetailScatterDensity.ItemBounds"/>).</summary>
		private void FinishBounds()
		{
			DetailScatterDensity.ItemBounds(ItemEntryStart, Entries, Channels, out ItemBoundStart, out ItemBoundChannel, out ItemBoundValue);
		}

		private static Texture2D MakeHoles(byte[] texels, int res, string terrainName)
		{
			var holes = new Texture2D(res, res, TextureFormat.R8, false, true)
			{
				name = $"{terrainName} detail scatter holes",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			holes.SetPixelData(texels, 0);
			holes.Apply(false, true);
			return holes;
		}

		private void FinishHeightTexture()
		{
			int res = HeightResolution;
			bool r16 = heightTexels16 != null;
			heightTexture = new Texture2D(res, res, r16 ? TextureFormat.R16 : TextureFormat.RFloat, false, true)
			{
				name = $"{(Terrain != null ? Terrain.name : "terrain")} detail scatter heights",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			if (r16)
			{
				heightTexture.SetPixelData(heightTexels16, 0);
			}
			else
			{
				heightTexture.SetPixelData(heightTexels32, 0);
			}
			heightTexture.Apply(false, true);
			heightTexels16 = null;
			heightTexels32 = null;
		}

		public void Dispose()
		{
			foreach (Texture2D t in new[] { heightTexture, Holes })
			{
				if (t == null)
				{
					continue;
				}
				// In edit mode too: the editor's bake builds terrains outside play.
				if (Application.isPlaying)
				{
					UnityEngine.Object.Destroy(t);
				}
				else
				{
					UnityEngine.Object.DestroyImmediate(t);
				}
			}
			Holes = null;
			heightTexture = null;
		}
	}
}
