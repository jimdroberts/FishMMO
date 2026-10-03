using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// One kind of grass as blades: from a detail prototype's prefab (its mesh's height and vertex colours,
	/// its FishMMO/Vegetation material's tints) and the profile's <see cref="GrassTypeTuning"/>. Shared by
	/// every terrain using the prefab; at most <see cref="GrassBladeRenderer.MaxTypes"/> exist.
	/// </summary>
	public sealed class GrassType
	{
		public GameObject Prefab;
		public int Index;
		public string Name;
		/// <summary>Typical blade height, metres (the mesh's height × the prototype's mean height scale × the tuning's).</summary>
		public float Height;
		/// <summary>Linear colours at the root and the tip (the mesh's sRGB vertex colours, linearised, × _BaseColor).</summary>
		public Color Root, Tip;
		/// <summary>Linear healthy and dry tints (the material's), the spread between neighbours, the patch size.</summary>
		public Color Healthy, Dry;
		public float TintSpread, PatchMetres, SnowBury;
		public GrassTypeTuning Tuning;
		/// <summary>True when the colours came from the mesh's vertex colours (false: the fallback greens).</summary>
		public bool ColoursFromMesh;

		private static readonly Color FallbackRoot = new Color(0.035f, 0.07f, 0.012f);
		private static readonly Color FallbackTip = new Color(0.12f, 0.26f, 0.035f);

		public static GrassType Build(DetailPrototype prototype, GrassTypeTuning tuning)
		{
			GameObject prefab = prototype.prototype;
			MeshFilter filter = prefab != null ? prefab.GetComponent<MeshFilter>() : null;
			MeshRenderer renderer = prefab != null ? prefab.GetComponent<MeshRenderer>() : null;
			Mesh mesh = filter != null ? filter.sharedMesh : null;
			Material material = renderer != null ? renderer.sharedMaterial : null;
			tuning = tuning ?? new GrassTypeTuning();

			float meshHeight = mesh != null ? mesh.bounds.max.y : 0.4f;
			if (meshHeight < 0.05f)
			{
				meshHeight = 0.4f;
			}
			float scale = 0.5f * (Mathf.Max(0.1f, prototype.minHeight) + Mathf.Max(0.1f, prototype.maxHeight));
			var type = new GrassType
			{
				Prefab = prefab,
				Name = prefab != null ? prefab.name : "grass",
				Height = Mathf.Clamp(meshHeight * scale * tuning.HeightScale, 0.05f, GrassMath.MaxPackedHeight / 1.6f),
				Root = FallbackRoot,
				Tip = FallbackTip,
				Healthy = new Color(0.9f, 0.95f, 0.9f),
				Dry = new Color(0.75f, 0.7f, 0.55f),
				TintSpread = 0.35f,
				PatchMetres = 12f,
				SnowBury = 0.8f,
				Tuning = tuning,
			};
			if (mesh != null && mesh.isReadable)
			{
				type.ColoursFromMesh = ReadColours(mesh, out type.Root, out type.Tip);
			}
			if (material != null)
			{
				Color baseColour = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").linear : Color.white;
				type.Root *= baseColour;
				type.Tip *= baseColour;
				if (material.HasProperty("_HealthyColor")) type.Healthy = material.GetColor("_HealthyColor").linear;
				if (material.HasProperty("_DryColor")) type.Dry = material.GetColor("_DryColor").linear;
				if (material.HasProperty("_TintSpread")) type.TintSpread = material.GetFloat("_TintSpread");
				if (material.HasProperty("_TintPatchMetres")) type.PatchMetres = material.GetFloat("_TintPatchMetres");
				if (material.HasProperty("_SnowBury")) type.SnowBury = material.GetFloat("_SnowBury");
			}
			return type;
		}

		/// <summary>The mean vertex colour of the bottom and the top fifth of the mesh (the generator darkens the root).</summary>
		private static bool ReadColours(Mesh mesh, out Color root, out Color tip)
		{
			root = FallbackRoot;
			tip = FallbackTip;
			var colours = new List<Color32>();
			var vertices = new List<Vector3>();
			mesh.GetColors(colours);
			mesh.GetVertices(vertices);
			if (colours.Count != vertices.Count || colours.Count == 0)
			{
				return false;
			}
			Bounds b = mesh.bounds;
			float low = b.min.y + 0.2f * b.size.y, high = b.max.y - 0.2f * b.size.y;
			Vector4 r = Vector4.zero, t = Vector4.zero;
			int nr = 0, nt = 0;
			for (int i = 0; i < vertices.Count; i++)
			{
				Color c = colours[i];
				if (vertices[i].y <= low)
				{
					r += (Vector4)c;
					nr++;
				}
				else if (vertices[i].y >= high)
				{
					t += (Vector4)c;
					nt++;
				}
			}
			if (nr == 0 || nt == 0)
			{
				return false;
			}
			// The generator writes its hex sRGB picks into the vertex colours; FishMMO/Vegetation multiplied them in
			// raw (as if linear), which is part of why the clumps read pale. Blades take them as the colours they are.
			root = ((Color)(r / nr)).linear;
			tip = ((Color)(t / nt)).linear;
			return true;
		}
	}

	/// <summary>
	/// One terrain as the blade grass reads it: its grass prototypes' detail layers as density maps (RGBA8,
	/// four channels a texture, each layer divided by the terrain's top detail value), its own heightmap
	/// texture for the roots, and a coarse min/max height grid for the CPU's tile bounds. Built once when
	/// the terrain is taken (CPU), released with it.
	/// </summary>
	public sealed class GrassTerrain : IDisposable
	{

		public const int MaxChannels = 8;

		/// <summary>The min/max grid's cell, metres.</summary>
		public const float BoundsCellMetres = 8f;

		public Terrain Terrain;
		public TerrainData Data;
		public Vector3 Origin;
		public Vector3 Size;
		/// <summary>
		/// Per density channel, the grass type's index (−1 none). A channel is a TYPE, the sum of every prototype
		/// of it: the scatter makes one prototype per biome rule, so a terrain carries several of one prefab
		/// (15 Detail_Grass* prototypes of 4 prefabs on Baoakraal), and a channel per prototype left all past
		/// the eighth as meshes, and a terrain whose first eight grew nowhere as meshes entirely.
		/// </summary>
		public readonly int[] ChannelTypes = new int[MaxChannels];
		public int Channels;
		/// <summary>The prototypes read into the channels (drawn as blades).</summary>
		public int Layers;
		public Texture2D Density0, Density1;
		/// <summary>The prototypes drawn as blades (by prototype index), what the detail renderer skips.</summary>
		public bool[] Skip;
		public int DensityResolution;
		public int HeightResolution;
		public float MaxGrassHeight;
		public bool HeightChecked;
		/// <summary>The terrain array set the ground is drawn with (TerrainArrayBinder), for the blades' colour; null until bound.</summary>
		public FishMMO.Shared.TerrainArraySet Arrays;
		/// <summary>
		/// The splat layers under each alphamap texel, packed (<see cref="GrassMath.PackSurfaceLayers"/>: two strongest
		/// layer ids and the first's share), the grass's own copy read by the compute instead of the terrain's 1..8
		/// control maps. Null where the terrain has no layers.
		/// </summary>
		public Texture2D SurfaceLayers;

		private float[] minHeights, maxHeights;
		private int gridX, gridZ;

		/* The grass's own copy of the terrain's heights (TerrainData.GetHeights, normalised 0..1 of size.y), filled
		 * from the strips the build reads anyway. TerrainData.heightmapTexture is NOT the same on every graphics
		 * API: its values were right on OpenGL (0.01 m off) and ~1.8 km off on Vulkan, which stood the blades in
		 * columns up into the sky. R16 where the device samples it, else RFloat (WebGPU has no core r16unorm). */
		private Texture2D heightTexture;
		private ushort[] heightTexels16;
		private float[] heightTexels32;

		public Texture Heightmap => heightTexture;

		/// <summary>Metres per unit of <see cref="Heightmap"/> (it holds 0..1 of the terrain's height).</summary>
		public float HeightScale => Size.y;

		/// <summary>
		/// Starts building a terrain's density maps and height grid a strip at a time (<see cref="Builder"/>);
		/// null when there is nothing to build. Reading every grass layer and every height sample at once cost
		/// 25–240 ms per terrain, a frame each, as a scene's terrains were taken.
		/// </summary>
		/// <param name="prototypes">The terrain's blade-grass prototype indices.</param>
		/// <param name="types">Each prototype's grass type (parallel to <paramref name="prototypes"/>).</param>
		public static Builder Begin(Terrain terrain, List<int> prototypes, List<GrassType> types)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			return prototypes.Count == 0 || data == null ? null : new Builder(terrain, data, prototypes, types);
		}

		/// <summary>
		/// A terrain's grass under construction: <see cref="Step"/> reads <see cref="StripRows"/> rows of one grass
		/// layer (or of the heightmap) at a time until the frame's budget is spent, then finishes with the textures.
		/// The terrain's mesh grass keeps drawing until it is <see cref="Done"/>.
		/// </summary>
		public sealed class Builder
		{
			public const int StripRows = 64;

			/// <summary>
			/// Metres the detail layers are smoothed over. The scatter writes most grass as points (one cell
			/// per spawned clump, the rest zero): right for the mesh clumps, but read as a density field it grew
			/// blades only in those cells — scattered islands of a few blades. Smoothed, it says how much grass
			/// an area has, and the blades fill it.
			/// </summary>
			public const float SmoothMetres = 2f;

			/// <summary>
			/// Each channel is scaled so this quantile of its non-zero smoothed texels reads as full: a layer's
			/// own typical dense ground is full grass whatever its scatter density, and its sparse ground stays sparse.
			/// </summary>
			public const float FullQuantile = 0.95f;

			/// <summary>The density maps' texel size, metres (the detail layer summed into blocks this size).</summary>
			public const float DensityTexelMetres = 2f;

			/// <summary>Progress, for the log: which stage and how far.</summary>
			public string Progress => layer < layerPrototypes.Count ? $"reading layer {layer + 1}/{layerPrototypes.Count}" : smoothChannel < count ? $"smoothing channel {smoothChannel + 1}/{count}" : textures ? (heightRow < gt.HeightResolution ? "heights" : "surface layers") : "packing";

			public readonly Terrain Terrain;
			public GrassTerrain Result { get; private set; }
			public bool Done { get; private set; }
			public double Milliseconds { get; private set; }
			public int Frames { get; private set; }

			private readonly GrassTerrain gt;
			/// <summary>The prototypes read, and the channel (type) each one adds into.</summary>
			private readonly List<int> layerPrototypes = new List<int>(), layerChannels = new List<int>();
			private readonly int count, res;
			/// <summary>
			/// The density maps' resolution: the detail layer's summed into <see cref="factor"/>² blocks (about
			/// 2 m texels). Smoothed over 2 m anyway, full detail resolution only cost time — 1056² × 8 layers × 4
			/// blur passes per terrain kept the terrain underfoot on the mesh grass for many seconds — and memory.
			/// </summary>
			private readonly int dres, factor;
			private readonly float top;
			/// <summary>Each channel's density while building, 0..1, at full precision: smoothing a point field in bytes rounded it to nothing.</summary>
			private readonly float[][] density;
			private int layer, row, heightRow, surfaceRow;
			private Color32[] surfacePixels;
			private float[] surfaceWeights;
			private bool any, textures;
			/// <summary>Smoothing: per channel, passes 0..3 = (horizontal, vertical) × 2 iterations of a box (≈ a Gaussian); then normalising.</summary>
			private int smoothChannel, smoothPass, smoothLine, normaliseChannel;
			private float[] line;

			internal Builder(Terrain terrain, TerrainData data, List<int> prototypes, List<GrassType> types)
			{
				Terrain = terrain;
				// One channel per type, in order of first appearance; a type past the eighth stays meshes.
				var channelTypes = new List<GrassType>();
				for (int i = 0; i < prototypes.Count; i++)
				{
					int c = channelTypes.IndexOf(types[i]);
					if (c < 0)
					{
						if (channelTypes.Count >= MaxChannels)
						{
							continue;
						}
						c = channelTypes.Count;
						channelTypes.Add(types[i]);
					}
					layerPrototypes.Add(prototypes[i]);
					layerChannels.Add(c);
				}
				count = channelTypes.Count;
				res = Mathf.Max(1, data.detailResolution);
				top = Mathf.Max(1, data.maxDetailScatterPerRes);
				factor = Mathf.Max(1, Mathf.RoundToInt(DensityTexelMetres / Mathf.Max(0.05f, data.size.x / res)));
				dres = Mathf.Max(1, (res + factor - 1) / factor);
				gt = new GrassTerrain
				{
					Terrain = terrain,
					Data = data,
					Origin = terrain.GetPosition(),
					Size = data.size,
					Channels = count,
					Layers = layerPrototypes.Count,
					DensityResolution = dres,
					HeightResolution = data.heightmapResolution,
					Skip = new bool[data.detailPrototypes.Length],
				};
				for (int c = 0; c < MaxChannels; c++)
				{
					gt.ChannelTypes[c] = c < count ? channelTypes[c].Index : -1;
				}
				for (int c = 0; c < count; c++)
				{
					gt.MaxGrassHeight = Mathf.Max(gt.MaxGrassHeight, channelTypes[c].Height);
				}
				foreach (int p in layerPrototypes)
				{
					gt.Skip[p] = true;
				}
				density = new float[count][];
				for (int c = 0; c < count; c++)
				{
					density[c] = new float[dres * dres];
				}
			}

			/// <summary>One strip of the current box-blur pass of the current channel (running sums, floats).</summary>
			private void SmoothStrip()
			{
				float[] target = density[smoothChannel];
				int radius = Mathf.Max(1, Mathf.RoundToInt(SmoothMetres / Mathf.Max(0.05f, gt.Size.x / dres)));
				bool horizontal = (smoothPass & 1) == 0;
				if (line == null || line.Length < dres)
				{
					line = new float[dres];
				}
				int lines = Mathf.Min(StripRows, dres - smoothLine);
				for (int l = smoothLine; l < smoothLine + lines; l++)
				{
					for (int i = 0; i < dres; i++)
					{
						line[i] = target[horizontal ? l * dres + i : i * dres + l];
					}
					float sum = 0f;
					int n = 0;
					for (int i = 0; i < Mathf.Min(radius, dres); i++)
					{
						sum += line[i];
						n++;
					}
					for (int i = 0; i < dres; i++)
					{
						int add = i + radius, drop = i - radius - 1;
						if (add < dres)
						{
							sum += line[add];
							n++;
						}
						if (drop >= 0)
						{
							sum -= line[drop];
							n--;
						}
						target[horizontal ? l * dres + i : i * dres + l] = Mathf.Max(0f, sum / n);
					}
				}
				smoothLine += lines;
				if (smoothLine >= dres)
				{
					smoothLine = 0;
					if (++smoothPass >= 4)
					{
						smoothPass = 0;
						smoothChannel++;
					}
				}
			}

			/// <summary>Scales a channel so its <see cref="FullQuantile"/> of non-zero texels reads 1.</summary>
			private static void Normalise(float[] values)
			{
				float max = 0f;
				int nonZero = 0;
				foreach (float v in values)
				{
					if (v > 1e-5f)
					{
						nonZero++;
						if (v > max) max = v;
					}
				}
				if (nonZero == 0 || max <= 0f)
				{
					return;
				}
				const int Bins = 1024;
				var histogram = new int[Bins];
				foreach (float v in values)
				{
					if (v > 1e-5f)
					{
						histogram[Mathf.Min(Bins - 1, (int)(v / max * Bins))]++;
					}
				}
				int want = Mathf.CeilToInt(nonZero * FullQuantile), seen = 0;
				float level = max;
				for (int b = 0; b < Bins; b++)
				{
					seen += histogram[b];
					if (seen >= want)
					{
						level = (b + 1) / (float)Bins * max;
						break;
					}
				}
				float scale = 1f / Mathf.Max(1e-5f, level);
				for (int i = 0; i < values.Length; i++)
				{
					values[i] = Mathf.Min(1f, values[i] * scale);
				}
			}

			/// <summary>Four channels from <paramref name="first"/> packed into a texture's bytes.</summary>
			private Color32[] Pack(int first)
			{
				var pixels = new Color32[dres * dres];
				for (int i = 0; i < pixels.Length; i++)
				{
					byte B(int c) => first + c < count ? (byte)Mathf.Clamp(Mathf.RoundToInt(density[first + c][i] * 255f), 0, 255) : (byte)0;
					pixels[i] = new Color32(B(0), B(1), B(2), B(3));
				}
				return pixels;
			}

			/// <summary>Drops an unfinished build, freeing anything it created.</summary>
			public void Abandon()
			{
				if (!Done || Result == null)
				{
					gt.Dispose();
				}
				Done = true;
				Result = null;
			}

			/// <summary>Works for at most <paramref name="budgetMs"/>; true once finished (<see cref="Result"/> null: no grass there).</summary>
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

			private void Advance()
			{
				TerrainData data = gt.Data;
				if (data == null)
				{
					Done = true;
					return;
				}
				if (layer < layerPrototypes.Count)
				{
					int rows = Mathf.Min(StripRows, res - row);
					int[,] cells = data.GetDetailLayer(0, row, res, rows, layerPrototypes[layer]);
					// Prototypes of one type add up; the channel is normalised afterwards and clamped to 1.
					float[] target = density[layerChannels[layer]];
					float blockWeight = 1f / (factor * factor);
					for (int z = 0; z < rows; z++)
					{
						for (int x = 0; x < res; x++)
						{
							int v = cells[z, x];
							if (v > 0)
							{
								any = true;
								target[((row + z) / factor) * dres + x / factor] += Mathf.Min(1f, v / top) * blockWeight;
							}
						}
					}
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
				if (smoothChannel < count)
				{
					SmoothStrip();
					return;
				}
				if (normaliseChannel < count)
				{
					Normalise(density[normaliseChannel++]);
					return;
				}
				if (!textures)
				{
					textures = true;
					Color32[] pixels0 = Pack(0), pixels1 = count > 4 ? Pack(4) : null;
					gt.Density0 = MakeTexture(pixels0, dres, $"{Terrain.name} grass density 0-3");
					gt.Density1 = pixels1 != null ? MakeTexture(pixels1, dres, $"{Terrain.name} grass density 4-7") : null;
					gt.BeginHeightGrid();
					return;
				}
				int hres = data.heightmapResolution;
				if (heightRow < hres)
				{
					int rows = Mathf.Min(StripRows, hres - heightRow);
					gt.AccumulateHeights(data.GetHeights(0, heightRow, hres, rows), heightRow);
					heightRow += rows;
					return;
				}
				int ares = data.alphamapResolution, alayers = data.alphamapLayers;
				if (alayers > 0 && surfaceRow < ares)
				{
					if (surfacePixels == null)
					{
						surfacePixels = new Color32[ares * ares];
						surfaceWeights = new float[alayers];
					}
					int rows = Mathf.Min(StripRows, ares - surfaceRow);
					float[,,] maps = data.GetAlphamaps(0, surfaceRow, ares, rows);
					for (int r = 0; r < rows; r++)
					{
						for (int x = 0; x < ares; x++)
						{
							for (int l = 0; l < alayers; l++)
							{
								surfaceWeights[l] = maps[r, x, l];
							}
							uint packed = GrassMath.PackSurfaceLayers(surfaceWeights, alayers);
							surfacePixels[(surfaceRow + r) * ares + x] = new Color32((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
						}
					}
					surfaceRow += rows;
					return;
				}
				if (surfacePixels != null)
				{
					gt.SurfaceLayers = MakeTexture(surfacePixels, ares, $"{Terrain.name} grass surface layers");
					surfacePixels = null;
					surfaceWeights = null;
				}
				gt.FinishHeightTexture();
				Result = gt;
				Done = true;
			}
		}

		private static Texture2D MakeTexture(Color32[] pixels, int res, string name)
		{
			var texture = new Texture2D(res, res, TextureFormat.RGBA32, false, true)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			return texture;
		}

		private void BeginHeightGrid()
		{
			int res = Data.heightmapResolution;
			if (SystemInfo.SupportsTextureFormat(TextureFormat.R16))
			{
				heightTexels16 = new ushort[res * res];
			}
			else
			{
				heightTexels32 = new float[res * res];
			}
			gridX = Mathf.Max(1, Mathf.CeilToInt(Size.x / BoundsCellMetres));
			gridZ = Mathf.Max(1, Mathf.CeilToInt(Size.z / BoundsCellMetres));
			minHeights = new float[gridX * gridZ];
			maxHeights = new float[gridX * gridZ];
			for (int i = 0; i < minHeights.Length; i++)
			{
				minHeights[i] = float.MaxValue;
				maxHeights[i] = float.MinValue;
			}
		}

		/// <summary>Folds a strip of height samples (rows <paramref name="zBase"/> on) into the min/max grid.</summary>
		private void AccumulateHeights(float[,] heights, int zBase)
		{
			int res = Data.heightmapResolution;
			float step = (res - 1) / Mathf.Max(1e-3f, Size.x) * BoundsCellMetres;
			int rows = heights.GetLength(0);
			for (int r = 0; r < rows; r++)
			{
				int z = zBase + r;
				for (int x = 0; x < res; x++)
				{
					float v = heights[r, x];
					if (heightTexels16 != null)
					{
						heightTexels16[z * res + x] = (ushort)Mathf.Clamp(Mathf.RoundToInt(v * 65535f), 0, 65535);
					}
					else
					{
						heightTexels32[z * res + x] = v;
					}
					float h = Origin.y + v * Size.y;
					// A sample on a cell border bounds both cells.
					int gx0 = Mathf.Clamp(Mathf.FloorToInt((x - 0.5f) / step), 0, gridX - 1), gx1 = Mathf.Clamp(Mathf.FloorToInt((x + 0.5f) / step), 0, gridX - 1);
					int gz0 = Mathf.Clamp(Mathf.FloorToInt((z - 0.5f) / step), 0, gridZ - 1), gz1 = Mathf.Clamp(Mathf.FloorToInt((z + 0.5f) / step), 0, gridZ - 1);
					for (int gz = gz0; gz <= gz1; gz++)
					{
						for (int gx = gx0; gx <= gx1; gx++)
						{
							int i = gz * gridX + gx;
							if (h < minHeights[i]) minHeights[i] = h;
							if (h > maxHeights[i]) maxHeights[i] = h;
						}
					}
				}
			}
		}

		/// <summary>Uploads the heights gathered by <see cref="AccumulateHeights"/> (row z = texture row, as the terrain's own).</summary>
		private void FinishHeightTexture()
		{
			int res = Data.heightmapResolution;
			bool r16 = heightTexels16 != null;
			heightTexture = new Texture2D(res, res, r16 ? TextureFormat.R16 : TextureFormat.RFloat, false, true)
			{
				name = $"{(Terrain != null ? Terrain.name : "terrain")} grass heights",
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

		/// <summary>The ground's height range under a world xz rectangle (clamped to the terrain).</summary>
		public Vector2 HeightRange(float x0, float z0, float x1, float z1)
		{
			int gx0 = Mathf.Clamp(Mathf.FloorToInt((x0 - Origin.x) / BoundsCellMetres), 0, gridX - 1);
			int gx1 = Mathf.Clamp(Mathf.FloorToInt((x1 - Origin.x) / BoundsCellMetres), 0, gridX - 1);
			int gz0 = Mathf.Clamp(Mathf.FloorToInt((z0 - Origin.z) / BoundsCellMetres), 0, gridZ - 1);
			int gz1 = Mathf.Clamp(Mathf.FloorToInt((z1 - Origin.z) / BoundsCellMetres), 0, gridZ - 1);
			float lo = float.MaxValue, hi = float.MinValue;
			for (int gz = gz0; gz <= gz1; gz++)
			{
				for (int gx = gx0; gx <= gx1; gx++)
				{
					int i = gz * gridX + gx;
					lo = Mathf.Min(lo, minHeights[i]);
					hi = Mathf.Max(hi, maxHeights[i]);
				}
			}
			return lo <= hi ? new Vector2(lo, hi) : new Vector2(Origin.y, Origin.y + Size.y);
		}

		public void Dispose()
		{
			if (Density0 != null)
			{
				UnityEngine.Object.Destroy(Density0);
			}
			if (Density1 != null)
			{
				UnityEngine.Object.Destroy(Density1);
			}
			Density0 = Density1 = null;
			if (heightTexture != null)
			{
				UnityEngine.Object.Destroy(heightTexture);
			}
			heightTexture = null;
			if (SurfaceLayers != null)
			{
				UnityEngine.Object.Destroy(SurfaceLayers);
			}
			SurfaceLayers = null;
		}
	}
}
