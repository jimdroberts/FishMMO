using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The ground round the camera finely enough for mist that lies a few metres deep on it, with what the
	/// ground there does to the air over it — and where the terrain shades that air from the light.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The sky's other pictures of the ground are a hundred metres to the texel (<see cref="CloudTerrainMap"/>)
	/// or tens (<see cref="CloudFlowField"/>): right for clouds and a fog's level top, useless for a patch
	/// of mist two metres deep, which would float or sink by more than its own depth. This is
	/// <see cref="Resolution"/> cells over <see cref="SizeMeters"/> round the camera — as far as the mist is
	/// drawn (FishMist.hlsl) with room to move — read a few thousand heights a frame into a second copy
	/// while the first is in use, and swapped when complete, whenever the camera has moved far enough.
	/// </para>
	/// <para>
	/// <c>_FishMistGround</c>: r the ground above the window's lowest (<c>_FishMistGroundBase</c>), m — a half
	/// float holds that to a fraction of a metre and filters everywhere a float texture may not; over open
	/// water the ground is the water's surface. g the hollow: how far the ground lies below its
	/// surroundings averaged over some thirty metres (m, positive in a dip). b how near open water is, 0..1
	/// (1 on it, falling off over a few tens of metres from its edge). a how closed the tree canopy is,
	/// 0..1, from the terrain's own tree instances. What each does to the air is GroundMist.LocalClosing.
	/// </para>
	/// <para>
	/// <c>_FishShadeNear</c>: the height below which the air over each cell is in the terrain's shadow
	/// from the light that leads the sky (<see cref="TerrainShadowSolver"/>), above <c>_FishShadeNearBase</c> —
	/// the near grid every fog and the mist are shaded by (FishTerrainSunlit, FishFogLayer.hlsl),
	/// worked out on a worker over this window's ground and the whole scene's (for the mountains kilometres
	/// off), again whenever the light has moved a quarter of a degree or the window has moved.
	/// </para>
	/// </remarks>
	public sealed class MistGroundMap
	{
		public const int Resolution = 160;
		public const float SizeMeters = 800f;
		/// <summary>How far the camera may move from the window's middle before the next is read, m.</summary>
		private const float Recentre = 100f;
		private const int SamplesPerFrame = 2048;
		/// <summary>The least time between two of the shade's solves, s, however fast the light is moving.</summary>
		private const float ShadowInterval = 0.5f;
		/// <summary>The hollow is measured against the ground averaged over this many cells either way.</summary>
		private const int HollowRadius = 3;
		/// <summary>How far from its edge open water still moistens the air, m (the e-folding).</summary>
		private const float WaterReach = 25f;
		/// <summary>A tree's crown, m: its radius at a width scale of one.</summary>
		private const float CrownRadius = 3f;
		/// <summary>How far toward the light the terrain is looked over for what shades a cell, m.</summary>
		private const float ShadowReach = 6000f;
		/// <summary>How far the light may turn before the shadow is worked out again: a quarter of a degree.</summary>
		private const float ShadowTurnCosine = 0.99999f;

		private static readonly int TextureId = Shader.PropertyToID("_FishMistGround");
		private static readonly int RectId = Shader.PropertyToID("_FishMistGroundRect");
		private static readonly int BaseId = Shader.PropertyToID("_FishMistGroundBase");
		private static readonly int ShadowId = Shader.PropertyToID("_FishShadeNear");
		private static readonly int ShadowRectId = Shader.PropertyToID("_FishShadeNearRect");
		private static readonly int ShadowBaseId = Shader.PropertyToID("_FishShadeNearBase");

		private Texture2D texture;
		private Texture2D shadowTexture;
		private float[] reading;
		private float[] current;
		private int read = -1;
		private Vector2 readingCorner;
		private Vector2 corner;
		private bool ready;
		private float groundBase;
		private int terrainSignature;
		private float shadingStarted = float.NegativeInfinity;
		private ushort[] groundHalves;
		private ushort[] shadowHalves;

		// Each terrain's trees, once: x, z (world) and crown radius, three to a tree.
		private readonly Dictionary<int, float[]> trees = new Dictionary<int, float[]>();

		private Task<float[]> shading;
		private Vector3 shadingLight;
		private Vector2 shadingCorner;
		private Vector3 shadedLight;
		private Vector2 shadedCorner;
		private bool shadowReady;
		private float shadowBase;
		private int generation;

		/// <summary>Keeps the window round the camera, its ground and its shade from <paramref name="toLight"/>, and publishes them.</summary>
		public void Update(Vector3 camera, Vector3 toLight, CloudFlowField scene)
		{
			TerrainSet terrains = TerrainSet.Current;
			float cell = SizeMeters / Resolution;
			Vector2 here = new Vector2(camera.x, camera.z);
			Vector2 middle = corner + new Vector2(SizeMeters, SizeMeters) * 0.5f;
			bool changed = terrains.Signature != terrainSignature;
			if (changed)
			{
				trees.Clear();
				generation++;
			}
			bool stale = !ready || changed || (here - middle).sqrMagnitude > Recentre * Recentre;
			if (stale && read < 0)
			{
				readingCorner = new Vector2(Mathf.Round(here.x / cell) * cell, Mathf.Round(here.y / cell) * cell) - new Vector2(SizeMeters, SizeMeters) * 0.5f;
				reading ??= new float[Resolution * Resolution];
				read = 0;
				terrainSignature = terrains.Signature;
			}
			if (read >= 0)
			{
				Read(terrains, cell);
			}
			Shade(toLight, scene);

			if (ready)
			{
				Shader.SetGlobalTexture(TextureId, texture);
			}
			Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, SizeMeters, ready ? 1f : 0f));
			Shader.SetGlobalFloat(BaseId, groundBase);
			if (shadowReady)
			{
				Shader.SetGlobalTexture(ShadowId, shadowTexture);
			}
			Shader.SetGlobalVector(ShadowRectId, new Vector4(shadedCorner.x, shadedCorner.y, SizeMeters, shadowReady ? 1f : 0f));
			Shader.SetGlobalFloat(ShadowBaseId, shadowBase);
		}

		private void Read(TerrainSet terrains, float cell)
		{
			if (readingWet == null || readingWet.Length != reading.Length)
			{
				readingWet = new bool[reading.Length];
			}
			int end = Mathf.Min(reading.Length, read + SamplesPerFrame);
			for (; read < end; read++)
			{
				int x = read % Resolution, z = read / Resolution;
				float wx = readingCorner.x + (x + 0.5f) * cell, wz = readingCorner.y + (z + 0.5f) * cell;
				if (!terrains.TryHeight(wx, wz, out float height))
				{
					height = 0f;
				}
				// The sea, a lake or a river: whichever stands over this ground.
				bool wet = SurfaceWater.TryGetSurfaceAt(wx, wz, out float level) && level >= height;
				readingWet[read] = wet;
				reading[read] = wet ? level : height;
			}
			if (read >= reading.Length)
			{
				Upload(terrains, cell, readingWet);
				corner = readingCorner;
				ready = true;
				read = -1;
				// The finished heights, kept apart from the next read for the shadow's worker.
				current = (float[])reading.Clone();
			}
		}

		/// <summary>Per cell of the read in progress, whether water stands over it.</summary>
		private bool[] readingWet;

		private void Upload(TerrainSet terrains, float cell, bool[] wetCells)
		{
			int n = Resolution;
			if (texture == null)
			{
				texture = new Texture2D(n, n, TextureFormat.RGBAHalf, false, true)
				{
					name = "Mist Ground",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
			}
			float lowest = float.MaxValue;
			for (int i = 0; i < reading.Length; i++)
			{
				lowest = Mathf.Min(lowest, reading[i]);
			}
			groundBase = lowest;
			float[] mean = BoxMean(reading, n, HollowRadius);
			float[] wet = WaterNearness(wetCells, n, cell);
			float[] canopy = Canopy(terrains.Terrains, n, cell, readingCorner);
			groundHalves ??= new ushort[n * n * 4];
			for (int i = 0; i < n * n; i++)
			{
				groundHalves[i * 4] = Mathf.FloatToHalf(reading[i] - lowest);
				groundHalves[i * 4 + 1] = Mathf.FloatToHalf(mean[i] - reading[i]);
				groundHalves[i * 4 + 2] = Mathf.FloatToHalf(wet[i]);
				groundHalves[i * 4 + 3] = Mathf.FloatToHalf(canopy[i]);
			}
			texture.SetPixelData(groundHalves, 0);
			texture.Apply(false, false);
		}

		/// <summary>
		/// How near open water each cell is, 0..1: 1 on it, e^(−d / <see cref="WaterReach"/>) at d metres
		/// from its edge (a chamfer distance over the grid). Water is wherever the sea, a lake or a river stands.
		/// </summary>
		private static float[] WaterNearness(bool[] wetCells, int n, float cell)
		{
			var near = new float[n * n];
			const float far = 1e9f;
			var distance = new float[n * n];
			bool any = false;
			for (int i = 0; i < n * n; i++)
			{
				bool wet = wetCells[i];
				distance[i] = wet ? 0f : far;
				any |= wet;
			}
			if (!any)
			{
				return near;
			}
			float straight = cell, diagonal = cell * 1.41421356f;
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					int i = z * n + x;
					float d = distance[i];
					if (x > 0) d = Mathf.Min(d, distance[i - 1] + straight);
					if (z > 0) d = Mathf.Min(d, distance[i - n] + straight);
					if (x > 0 && z > 0) d = Mathf.Min(d, distance[i - n - 1] + diagonal);
					if (x < n - 1 && z > 0) d = Mathf.Min(d, distance[i - n + 1] + diagonal);
					distance[i] = d;
				}
			}
			for (int z = n - 1; z >= 0; z--)
			{
				for (int x = n - 1; x >= 0; x--)
				{
					int i = z * n + x;
					float d = distance[i];
					if (x < n - 1) d = Mathf.Min(d, distance[i + 1] + straight);
					if (z < n - 1) d = Mathf.Min(d, distance[i + n] + straight);
					if (x < n - 1 && z < n - 1) d = Mathf.Min(d, distance[i + n + 1] + diagonal);
					if (x > 0 && z < n - 1) d = Mathf.Min(d, distance[i + n - 1] + diagonal);
					distance[i] = d;
				}
			}
			for (int i = 0; i < n * n; i++)
			{
				near[i] = Mathf.Exp(-distance[i] / WaterReach);
			}
			return near;
		}

		/// <summary>
		/// How closed the canopy over each cell is, 0..1: the crowns of the terrain's trees laid on the grid,
		/// their area over the cell's, spread over a couple of cells (a crown is wider than its trunk's cell).
		/// </summary>
		private float[] Canopy(Terrain[] terrains, int n, float cell, Vector2 at)
		{
			var cover = new float[n * n];
			float cellArea = cell * cell;
			foreach (Terrain t in terrains)
			{
				if (t == null || t.terrainData == null)
				{
					continue;
				}
				// Only the terrains under the window: the others' trees cannot land on it.
				Vector3 origin = t.GetPosition();
				Vector3 extent = t.terrainData.size;
				if (origin.x > at.x + n * cell || origin.z > at.y + n * cell || origin.x + extent.x < at.x || origin.z + extent.z < at.y)
				{
					continue;
				}
				float[] crowns = TreesOf(t);
				for (int k = 0; k < crowns.Length; k += 3)
				{
					int x = Mathf.FloorToInt((crowns[k] - at.x) / cell);
					int z = Mathf.FloorToInt((crowns[k + 1] - at.y) / cell);
					if (x < 0 || z < 0 || x >= n || z >= n)
					{
						continue;
					}
					float r = crowns[k + 2];
					cover[z * n + x] += Mathf.PI * r * r / cellArea;
				}
			}
			float[] spread = BoxMean(cover, n, 1);
			for (int i = 0; i < spread.Length; i++)
			{
				spread[i] = Mathf.Clamp01(spread[i]);
			}
			return spread;
		}

		/// <summary>A terrain's trees as world x, z and crown radius, read once (TerrainData.treeInstances copies on every read).</summary>
		private float[] TreesOf(Terrain terrain)
		{
			int id = terrain.GetInstanceID();
			if (trees.TryGetValue(id, out float[] crowns))
			{
				return crowns;
			}
			TerrainData data = terrain.terrainData;
			TreeInstance[] instances = data.treeInstances;
			Vector3 origin = terrain.GetPosition();
			Vector3 size = data.size;
			crowns = new float[instances.Length * 3];
			for (int i = 0; i < instances.Length; i++)
			{
				TreeInstance tree = instances[i];
				crowns[i * 3] = origin.x + tree.position.x * size.x;
				crowns[i * 3 + 1] = origin.z + tree.position.z * size.z;
				crowns[i * 3 + 2] = CrownRadius * Mathf.Max(0.1f, tree.widthScale);
			}
			trees[id] = crowns;
			return crowns;
		}

		/// <summary>Starts the shadow's worker when the light or the window has moved, and uploads its answer.</summary>
		private void Shade(Vector3 toLight, CloudFlowField scene)
		{
			if (shading != null && shading.IsCompleted)
			{
				Task<float[]> done = shading;
				shading = null;
				if (done.Status == TaskStatus.RanToCompletion && done.Result != null)
				{
					UploadShadow(done.Result);
					shadedLight = shadingLight;
					shadedCorner = shadingCorner;
				}
				else if (done.Exception != null)
				{
					Debug.LogException(done.Exception.InnerException ?? done.Exception);
				}
			}
			if (!ready || current == null || shading != null || toLight.sqrMagnitude < 1e-6f)
			{
				return;
			}
			Vector3 light = toLight.normalized;
			bool windowMoved = !shadowReady || shadedCorner != corner;
			bool turned = Vector3.Dot(light, shadedLight) < ShadowTurnCosine && Time.unscaledTime - shadingStarted >= ShadowInterval;
			if (!windowMoved && !turned)
			{
				return;
			}
			shadingStarted = Time.unscaledTime;
			var fine = TerrainShadowSolver.Grid.Of(current, Resolution, corner.x, corner.y, SizeMeters);
			TerrainShadowSolver.Grid coarse = scene != null ? scene.Ground : default;
			shadingLight = light;
			shadingCorner = corner;
			int mine = generation;
			shading = Task.Run(() => mine == generation ? TerrainShadowSolver.ShadowTops(fine, coarse, light.x, light.y, light.z, ShadowReach) : null);
		}

		private void UploadShadow(float[] tops)
		{
			int n = Resolution;
			if (shadowTexture == null)
			{
				shadowTexture = new Texture2D(n, n, TextureFormat.RHalf, false, true)
				{
					name = "Mist Shadow",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
			}
			// Measured up from the same base as the ground, and held within a half's comfortable range:
			// a kilometre under the window's lowest ground is "nothing shades it", two above "all shade".
			shadowBase = groundBase;
			shadowHalves ??= new ushort[n * n];
			for (int i = 0; i < n * n; i++)
			{
				shadowHalves[i] = Mathf.FloatToHalf(Mathf.Clamp(tops[i] - shadowBase, -1000f, 2000f));
			}
			shadowTexture.SetPixelData(shadowHalves, 0);
			shadowTexture.Apply(false, false);
			shadowReady = true;
		}

		private static float[] BoxMean(float[] source, int n, int radius)
		{
			var across = new float[n * n];
			var result = new float[n * n];
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					float sum = 0f;
					int count = 0;
					for (int o = Mathf.Max(0, x - radius); o <= Mathf.Min(n - 1, x + radius); o++)
					{
						sum += source[z * n + o];
						count++;
					}
					across[z * n + x] = sum / count;
				}
			}
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					float sum = 0f;
					int count = 0;
					for (int o = Mathf.Max(0, z - radius); o <= Mathf.Min(n - 1, z + radius); o++)
					{
						sum += across[o * n + x];
						count++;
					}
					result[z * n + x] = sum / count;
				}
			}
			return result;
		}

		public void Dispose()
		{
			generation++;
			shading = null;
			if (texture != null)
			{
				if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
				texture = null;
			}
			if (shadowTexture != null)
			{
				if (Application.isPlaying) Object.Destroy(shadowTexture); else Object.DestroyImmediate(shadowTexture);
				shadowTexture = null;
			}
			ready = false;
			shadowReady = false;
			read = -1;
			trees.Clear();
			Shader.SetGlobalVector(RectId, Vector4.zero);
			Shader.SetGlobalVector(ShadowRectId, Vector4.zero);
		}
	}
}
