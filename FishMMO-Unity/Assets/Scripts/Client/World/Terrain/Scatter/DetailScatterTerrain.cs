using System;
using System.Collections.Generic;
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

			public string Progress => layer < layerPrototypes.Count ? $"reading layer {layer + 1}/{layerPrototypes.Count}" : heightRow < st.HeightResolution ? "heights" : "packing";

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
				for (int c = 0; c < channelTypes.Count; c++)
				{
					DetailScatterType type = channelTypes[c];
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

			/// <summary>Drops an unfinished build, freeing anything it created.</summary>
			public void Abandon()
			{
				if (!Done || Result == null)
				{
					st.Dispose();
				}
				Done = true;
				Result = null;
			}

			/// <summary>Works for at most <paramref name="budgetMs"/>; true once finished (<see cref="Result"/> null: nothing to scatter there).</summary>
			public bool Step(double budgetMs)
			{
				if (Done)
				{
					return true;
				}
				Frames++;
				var watch = System.Diagnostics.Stopwatch.StartNew();
				do
				{
					Advance();
				}
				while (!Done && watch.Elapsed.TotalMilliseconds < budgetMs);
				Milliseconds += watch.Elapsed.TotalMilliseconds;
				return Done;
			}

			private float Density(int prototype) => prototypeSettings[prototype].UseDensityScaling ? st.DensityScale : 1f;

			private void Advance()
			{
				TerrainData data = st.Data;
				if (data == null)
				{
					Done = true;
					return;
				}
				if (layer < layerPrototypes.Count)
				{
					int p = layerPrototypes[layer];
					int rows = Mathf.Min(StripRows, res - row);
					int[,] cells = data.GetDetailLayer(0, row, res, rows, p);
					float[] target = expected[layerChannels[layer]];
					int blocks = st.Blocks;
					float density = channelBush[layerChannels[layer]] ? 1f : Density(p);
					double total = 0.0;
					for (int z = 0; z < rows; z++)
					{
						int blockRow = (row + z) / factor * blocks;
						for (int x = 0; x < res; x++)
						{
							int v = cells[z, x];
							if (v <= 0)
							{
								continue;
							}
							// The chunk renderer's own count for the cell (coverage or instance count, density scale).
							float e = TerrainDetailMath.ExpectedInCell(v, coverageMode, coverage[p], cellArea, density);
							if (e > 0f)
							{
								target[blockRow + x / factor] += e;
								total += e;
								any = true;
							}
						}
					}
					prototypeTotals[p] += total;
					row += rows;
					if (row >= res)
					{
						layer++;
						row = 0;
					}
					return;
				}
				if (!any)
				{
					Done = true;
					return;
				}
				if (heightRow == 0 && heightTexelsMissing)
				{
					BeginHeights();
				}
				int hres = data.heightmapResolution;
				if (heightRow < hres)
				{
					int rows = Mathf.Min(StripRows, hres - heightRow);
					AccumulateHeights(data.GetHeights(0, heightRow, hres, rows), heightRow);
					heightRow += rows;
					return;
				}
				Finish();
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

			/// <summary>The channels' settings, the items' bounds, the slices and the height texture.</summary>
			private void Finish()
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
				st.Density0 = MakeSlice(slice0, blocks, $"{Terrain.name} detail scatter 0-3");
				st.Density1 = slice1 != null ? MakeSlice(slice1, blocks, $"{Terrain.name} detail scatter 4-7") : null;
				st.FinishHeightTexture();
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
			foreach (Texture2D t in new[] { Density0, Density1, heightTexture })
			{
				if (t != null)
				{
					UnityEngine.Object.Destroy(t);
				}
			}
			Density0 = Density1 = null;
			heightTexture = null;
		}
	}
}
