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

		/// <summary>Reads a detail prototype, or returns null with the reason the scatter cannot draw it (it then stays on the chunk renderer).</summary>
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
			return new DetailScatterType
			{
				Prefab = prefab,
				Name = prefab.name,
				Salt = DetailScatterMath.Salt(prefab.name),
				Mesh = filter.sharedMesh,
				Levels = levels.ToArray(),
				Bush = bush,
				MeshTop = Mathf.Max(0.05f, bounds.max.y),
				Material = material,
				CastsShadows = renderer.shadowCastingMode != ShadowCastingMode.Off,
				MeshBounds = bounds,
				FootRadius = TerrainDetailField.DetailPrototypeSettings.From(prototype).FootRadius,
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
	/// One terrain as the GPU detail scatter reads it: its scattered prototypes' detail layers summed into 2 m blocks
	/// of exact expected counts (<see cref="DetailScatterMath"/>), one channel per TYPE (every prototype of a prefab
	/// adds into it, as the blade grass does), four channels a half-float slice; each channel's placement settings
	/// (the prototypes' own, averaged by how much each contributes); the terrain's own height texture; and, per work
	/// item (8 × 8 blocks), an exact upper bound on each channel's instances and the ground's height range, so the CPU
	/// sizes the slots and culls the items without reading the GPU. Built once when the terrain is taken, released
	/// with it.
	/// </summary>
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
		}

		public Terrain Terrain;
		public TerrainData Data;
		public Vector3 Origin;
		public Vector3 Size;
		public int Channels;
		public readonly Channel[] ChannelSettings = new Channel[DetailScatterMath.MaxChannels];
		/// <summary>The prototypes read into the channels.</summary>
		public int Layers;
		/// <summary>The prototypes the scatter draws (by prototype index), what the chunk renderer skips.</summary>
		public bool[] Skip;
		/// <summary>The density scale the counts were built at (a quality change rebuilds the terrain).</summary>
		public float DensityScale;
		public int HeightResolution;
		/// <summary>Blocks per side, block size (m) along x and z, and the world index of block 0 on each axis.</summary>
		public int Blocks;
		public float BlockX, BlockZ;
		public int WorldBlockX, WorldBlockZ;
		/// <summary>Work items per side, each item's exact upper bound per channel (item × MaxChannels + channel), and its ground height range.</summary>
		public int ItemsSide;
		public int[] ItemBounds;
		private float[] itemMin, itemMax;
		/// <summary>The tallest an instance can stand above its root, and the farthest it can reach sideways, m (item bounds).</summary>
		public float MaxInstanceHeight, MaxInstanceReach;
		/// <summary>True when any channel's type casts shadows.</summary>
		public bool CastsShadows;
		/// <summary>Blocks whose expected count was over <see cref="DetailScatterMath.MaxPerBlock"/> and was capped, and the first such channel (−1 none).</summary>
		public int ClampedBlocks;
		public int ClampedChannel = -1;
		/// <summary>The density slices: channels 0..3, then 4..7 (null when there are four or fewer channels).</summary>
		public Texture2D Density0, Density1;

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
		/// A terrain's scatter under construction: <see cref="Step"/> reads <see cref="StripRows"/> rows of one detail
		/// layer (or of the heightmap) at a time until the frame's budget is spent, then packs the slices. The chunk
		/// renderer keeps drawing the prototypes until it is <see cref="Done"/>.
		/// </summary>
		public sealed class Builder
		{
			public const int StripRows = 64;

			public readonly Terrain Terrain;
			public DetailScatterTerrain Result { get; private set; }
			public bool Done { get; private set; }
			public double Milliseconds { get; private set; }
			public int Frames { get; private set; }

			public string Progress => layer < layerPrototypes.Count ? $"reading layer {layer + 1}/{layerPrototypes.Count}" : heightRow < st.HeightResolution ? "heights" : holes == null || !holes.Complete ? "holes" : "packing";

			/// <summary>True when the build gave up on a worker's exception (logged): the terrain is taken again from the start.</summary>
			public bool Failed { get; private set; }

			/// <summary>Strips read ahead of the worker at most: past it the main thread waits a frame (bounded memory).</summary>
			public const int MaxPendingStrips = 8;

			/// <summary>
			/// The worker's queue (<see cref="TerrainBuildQueue"/>): each strip's sums and the height texels, in the order read,
			/// as the same managed code ran them here, so the counts and heights are bit for bit what they were.
			/// </summary>
			private readonly TerrainBuildQueue queue = new TerrainBuildQueue();
			/// <summary>The channels' settings, the items' bounds and the slices (a worker's), once everything is read.</summary>
			private Task<Slices> packing;

			private readonly DetailScatterTerrain st;
			private readonly List<int> layerPrototypes = new List<int>(), layerChannels = new List<int>();
			private readonly TerrainDetailField.DetailPrototypeSettings[] prototypeSettings;
			/// <summary>Per channel, whether its type is a bush (no density scale: cover is the same for everyone).</summary>
			private readonly bool[] channelBush;
			private readonly float[] coverage;
			private readonly double[] prototypeTotals;
			private readonly int res, factor;
			private readonly bool coverageMode;
			private readonly float cellArea;
			/// <summary>Each channel's expected instances per block while building, full precision.</summary>
			private readonly float[][] expected;
			private int layer, row, heightRow;
			private bool any;
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

			internal Builder(Terrain terrain, TerrainData data, List<int> prototypes, List<DetailScatterType> types, float densityScale)
			{
				Terrain = terrain;
				var channelTypes = new List<DetailScatterType>();
				for (int i = 0; i < prototypes.Count; i++)
				{
					int c = channelTypes.IndexOf(types[i]);
					if (c < 0)
					{
						if (channelTypes.Count >= DetailScatterMath.MaxChannels)
						{
							continue;
						}
						c = channelTypes.Count;
						channelTypes.Add(types[i]);
					}
					layerPrototypes.Add(prototypes[i]);
					layerChannels.Add(c);
				}
				res = Mathf.Max(1, data.detailResolution);
				float cellX = data.size.x / res, cellZ = data.size.z / res;
				cellArea = cellX * cellZ;
				factor = DetailScatterMath.BlockFactor(cellX);
				coverageMode = data.detailScatterMode == DetailScatterMode.CoverageMode;
				DetailPrototype[] all = data.detailPrototypes;
				prototypeSettings = new TerrainDetailField.DetailPrototypeSettings[all.Length];
				coverage = new float[all.Length];
				prototypeTotals = new double[all.Length];
				foreach (int p in layerPrototypes)
				{
					prototypeSettings[p] = TerrainDetailField.DetailPrototypeSettings.From(all[p]);
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
					Layers = layerPrototypes.Count,
					Skip = new bool[all.Length],
					DensityScale = densityScale,
					HeightResolution = data.heightmapResolution,
					Blocks = blocks,
					BlockX = cellX * factor,
					BlockZ = cellZ * factor,
					WorldBlockX = DetailScatterMath.WorldBlockBase(origin.x, cellX * factor),
					WorldBlockZ = DetailScatterMath.WorldBlockBase(origin.z, cellZ * factor),
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
				expected = new float[st.Channels][];
				for (int c = 0; c < st.Channels; c++)
				{
					expected[c] = new float[blocks * blocks];
				}
				// The tallest and widest an instance can be: its mesh at the largest scale any of its prototypes allows.
				for (int i = 0; i < layerPrototypes.Count; i++)
				{
					TerrainDetailField.DetailPrototypeSettings s = prototypeSettings[layerPrototypes[i]];
					Bounds b = channelTypes[layerChannels[i]].MeshBounds;
					float w = Mathf.Max(s.MaxWidth, 0.01f), h = Mathf.Max(s.MaxHeight, 0.01f);
					st.MaxInstanceHeight = Mathf.Max(st.MaxInstanceHeight, b.max.y * h);
					st.MaxInstanceReach = Mathf.Max(st.MaxInstanceReach, Mathf.Max(b.extents.x + Mathf.Abs(b.center.x), b.extents.z + Mathf.Abs(b.center.z)) * w);
				}
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

			/// <summary>The finished maps, bounds, channel settings and heights into the bake (before the textures take them).</summary>
			private void CaptureMaps(Slices packed)
			{
				Captured = Layout(true);
				Captured.Blocks = st.Blocks;
				Captured.Slice0 = packed.Slice0;
				Captured.Slice1 = packed.Slice1 ?? new ushort[0];
				Captured.Holes = packed.Holes ?? new byte[0];
				Captured.ItemBounds = (int[])st.ItemBounds.Clone();
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
				bool second = st.Channels > DetailScatterMath.ChannelsPerSlice;
				if (b.Blocks != blocks || b.Slice0 == null || b.Slice0.Length != blocks * blocks * 4
					|| (second && (b.Slice1 == null || b.Slice1.Length != blocks * blocks * 4))
					|| b.ItemBounds == null || b.ItemBounds.Length != items * DetailScatterMath.MaxChannels
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
				st.ItemBounds = b.ItemBounds;
				st.itemMin = b.ItemMin;
				st.itemMax = b.ItemMax;
				st.ClampedBlocks = b.ClampedBlocks;
				st.ClampedChannel = b.ClampedChannel;
				st.Density0 = MakeSlice(b.Slice0, blocks, $"{Terrain.name} detail scatter 0-3");
				st.Density1 = second ? MakeSlice(b.Slice1, blocks, $"{Terrain.name} detail scatter 4-7") : null;
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
					st.Holes = new Texture2D(hres, hres, TextureFormat.R8, false, true)
					{
						name = $"{Terrain.name} detail scatter holes",
						filterMode = FilterMode.Point,
						wrapMode = TextureWrapMode.Clamp,
						hideFlags = HideFlags.DontSave,
					};
					st.Holes.SetPixelData(b.Holes, 0);
					st.Holes.Apply(false, true);
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
			/// One strip of a detail layer (rows <paramref name="rowBase"/>..) summed into a channel's blocks: each cell's
			/// expected count as the chunk renderer works it out (coverage or instance count, density scale), in order.
			/// Returns the strip's total (summed in doubles, in order); <paramref name="any"/> true when any cell counted.
			/// </summary>
			public static double AddStrip(int[,] cells, int rows, int rowBase, int res, int factor, int blocks, bool coverageMode, float coverage,
				float cellArea, float density, float[] target, ref bool any)
			{
				double total = 0.0;
				for (int z = 0; z < rows; z++)
				{
					int blockRow = (rowBase + z) / factor * blocks;
					for (int x = 0; x < res; x++)
					{
						int v = cells[z, x];
						if (v <= 0)
						{
							continue;
						}
						float e = TerrainDetailMath.ExpectedInCell(v, coverageMode, coverage, cellArea, density);
						if (e > 0f)
						{
							target[blockRow + x / factor] += e;
							total += e;
							any = true;
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
				if (layer < layerPrototypes.Count)
				{
					if (queue.Pending >= MaxPendingStrips)
					{
						return false;
					}
					int p = layerPrototypes[layer];
					int rows = Mathf.Min(StripRows, res - row);
					int[,] cells = data.GetDetailLayer(0, row, res, rows, p);
					float[] target = expected[layerChannels[layer]];
					int blocks = st.Blocks, rowBase = row;
					float density = channelBush[layerChannels[layer]] ? 1f : Density(p);
					float cover = coverage[p];
					queue.Enqueue(() =>
					{
						bool found = false;
						prototypeTotals[p] += AddStrip(cells, rows, rowBase, res, factor, blocks, coverageMode, cover, cellArea, density, target, ref found);
						if (found)
						{
							any = true;
						}
					});
					row += rows;
					if (row >= res)
					{
						layer++;
						row = 0;
					}
					return true;
				}
				if (heightRow == 0 && heightTexelsMissing)
				{
					// Whether anything was painted is known once the layers are summed.
					if (!queue.Idle)
					{
						return false;
					}
					if (!any)
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
					int rows = Mathf.Min(StripRows, hres - heightRow);
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
					packing = TerrainBuildQueue.Inline ? Task.FromResult(PackSlices(cut)) : Task.Run(() => PackSlices(cut));
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

			/// <summary>What <see cref="PackSlices"/> gives the textures.</summary>
			private sealed class Slices
			{
				public ushort[] Slice0, Slice1;
				public byte[] Holes;
			}

			/// <summary>
			/// The channels' settings, the items' bounds and the slices' halves (and the holes' texels): a worker's, once every
			/// strip is summed, the same code in the same order as on the main thread before. Mathf.FloatToHalf is thread-safe.
			/// </summary>
			private Slices PackSlices(TerrainHoleMask cut)
			{
				int blocks = st.Blocks;
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
					TerrainDetailField.DetailPrototypeSettings s = prototypeSettings[p];
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

				// The counts as the GPU will read them (half floats), the items' exact upper bounds, and the slices.
				int side = st.ItemsSide;
				st.ItemBounds = new int[side * side * DetailScatterMath.MaxChannels];
				ushort[] slice0 = new ushort[blocks * blocks * 4];
				ushort[] slice1 = channels > DetailScatterMath.ChannelsPerSlice ? new ushort[blocks * blocks * 4] : null;
				for (int c = 0; c < channels; c++)
				{
					float[] values = expected[c];
					ushort[] slice = c < DetailScatterMath.ChannelsPerSlice ? slice0 : slice1;
					int lane = c % DetailScatterMath.ChannelsPerSlice;
					for (int bz = 0; bz < blocks; bz++)
					{
						for (int bx = 0; bx < blocks; bx++)
						{
							int i = bz * blocks + bx;
							float stored = DetailScatterMath.Stored(values[i]);
							if (values[i] > DetailScatterMath.MaxPerBlock)
							{
								st.ClampedBlocks++;
								st.ClampedChannel = st.ClampedChannel < 0 ? c : st.ClampedChannel;
							}
							slice[i * 4 + lane] = Mathf.FloatToHalf(stored);
							int bound = DetailScatterMath.Bound(stored);
							if (bound > 0)
							{
								st.ItemBounds[((bz / DetailScatterMath.ItemSide) * side + bx / DetailScatterMath.ItemSide) * DetailScatterMath.MaxChannels + c] += bound;
							}
						}
					}
				}
				return new Slices
				{
					Slice0 = slice0,
					Slice1 = slice1,
					Holes = cut != null && cut.Any ? cut.ToTexels(st.HeightResolution) : null,
				};
			}

			/// <summary>The slices, the height texture and the holes' texture (the main thread's).</summary>
			private void Finish(Slices packed)
			{
				int blocks = st.Blocks;
				if (Capture)
				{
					CaptureMaps(packed);
				}
				st.Density0 = MakeSlice(packed.Slice0, blocks, $"{Terrain.name} detail scatter 0-3");
				st.Density1 = packed.Slice1 != null ? MakeSlice(packed.Slice1, blocks, $"{Terrain.name} detail scatter 4-7") : null;
				st.FinishHeightTexture();
				if (packed.Holes != null)
				{
					st.Holes = new Texture2D(st.HeightResolution, st.HeightResolution, TextureFormat.R8, false, true)
					{
						name = $"{Terrain.name} detail scatter holes",
						filterMode = FilterMode.Point,
						wrapMode = TextureWrapMode.Clamp,
						hideFlags = HideFlags.DontSave,
					};
					st.Holes.SetPixelData(packed.Holes, 0);
					st.Holes.Apply(false, true);
				}
				holes = null;
				Result = st;
				Done = true;
			}
		}

		private static Texture2D MakeSlice(ushort[] halves, int res, string name)
		{
			var texture = new Texture2D(res, res, TextureFormat.RGBAHalf, false, true)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			texture.SetPixelData(halves, 0);
			texture.Apply(false, true);
			return texture;
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
			foreach (Texture2D t in new[] { Density0, Density1, heightTexture, Holes })
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
			Density0 = Density1 = Holes = null;
			heightTexture = null;
		}
	}
}
