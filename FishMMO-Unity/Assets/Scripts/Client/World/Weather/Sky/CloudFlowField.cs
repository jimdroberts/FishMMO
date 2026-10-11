using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The air's way round the scene's terrain, for the clouds and the fog: where it flows round the
	/// mountains at each height (<see cref="TerrainFlowSolver"/>), and where the rock is.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The terrain does not move, so this is worked out once per scene, over the whole of it — not in
	/// a window round one camera — and nothing of it is redone when the wind turns or the camera
	/// travels. "The whole of it" is the terrain and the backdrop round it to the horizon
	/// (<see cref="TerrainSet"/>): the playable area alone left every mountain past its edge for the clouds
	/// to run through. The heights are read a few thousand a frame, the flow is solved on a worker thread (a
	/// second or so at 384 cells), and both are uploaded when ready; until then the sky reads the air
	/// undisturbed.
	/// </para>
	/// <para>
	/// Two textures. <c>_FishCloudFlowField</c>, a 3D texture: the flow at <see cref="Levels"/> heights
	/// from the lowest ground to the highest, as four offsets a cell (FishFlowAround). <c>_FishCloudRock</c>:
	/// r the rock — the heights, each the highest of its three-by-three, so it stands at or above the
	/// ground and a summit between samples is not lost — and g the massif, the highest rock within a
	/// kilometre, which is what says whether the air beside a slope can climb what it is against or
	/// must go round (FishCloudClearance).
	/// </para>
	/// </remarks>
	public sealed class CloudFlowField
	{
		/// <summary>Cells across the grid.</summary>
		public const int Resolution = 384;
		/// <summary>Heights the flow is solved at, from the lowest ground to the highest.</summary>
		public const int Levels = 8;
		/// <summary>Open air round the terrain, as a share of its size: the grid's edge is the undisturbed wind.</summary>
		private const float MarginShare = 0.1f;
		private const float MinimumMargin = 1500f;
		/// <summary>How far round a slope the massif is looked for, m.</summary>
		private const float MassifRadius = 1000f;
		/// <summary>Heights read a frame: the whole grid in about sixteen frames.</summary>
		private const int SamplesPerFrame = 4096;

		private static readonly int FieldId = Shader.PropertyToID("_FishCloudFlowField");
		private static readonly int RockId = Shader.PropertyToID("_FishCloudRock");
		private static readonly int RectId = Shader.PropertyToID("_FishCloudFlowRect");
		private static readonly int LevelsId = Shader.PropertyToID("_FishCloudFlowLevels");
		private static readonly int RockTopId = Shader.PropertyToID("_FishCloudRockTop");
		private static readonly int TerrainId = Shader.PropertyToID("_FishCloudTerrain");
		private static readonly int TerrainRectId = Shader.PropertyToID("_FishCloudTerrainRect");
		/// <summary>The terrain smoothed as the air feels it, m: some 700 m, as CloudTerrainMap smooths it.</summary>
		private const float AirSmoothMetres = 625f;
		private static readonly int ShadeId = Shader.PropertyToID("_FishShadeFar");
		private static readonly int ShadeRectId = Shader.PropertyToID("_FishShadeFarRect");
		private static readonly int ShadeBaseId = Shader.PropertyToID("_FishShadeFarBase");
		/// <summary>How far toward the light the terrain is looked over for what shades a cell, m.</summary>
		private const float ShadeReach = 8000f;
		/// <summary>How far the light may turn before the far shade is worked out again: half a degree.</summary>
		private const float ShadeTurnCosine = 0.99996f;

		private Texture3D field;

		/// <summary>
		/// Bound as <c>_FishCloudFlowField</c> until the flow is solved. The shaders read the field only when the rect's w
		/// says it is there, but a compute kernel that declares a texture must have one bound to dispatch at all, and the
		/// volumetric fog can now run before the flow is ready (for the waterfalls' mist alone, 2026-10-08): "Property
		/// (_FishCloudFlowField) at kernel index (0) is not set".
		/// </summary>
		private static Texture3D placeholderField;

		private static void BindIfUnset(int id)
		{
			if (Shader.GetGlobalTexture(id) == null)
			{
				Shader.SetGlobalTexture(id, Texture2D.blackTexture);
			}
		}
		private Texture2D rockTexture;
		private float[] ground;
		private int sampled;
		private Vector2 corner;
		private float size;
		private float lowest, highest;
		private bool rockReady, fieldReady;
		private Task<float[][]> solving;
		private float[] solvingLevels;
		private int signature;
		private int generation;

		/// <summary>The highest rock in the scene, m; 0 while there is none (or it is not yet read).</summary>
		public float HighestRock { get; private set; }

		/// <summary>
		/// The lowest ground in the scene, m (0 until it is read): the land the air arrives at its mountains
		/// over, which how high a mountain is to the air is measured from (<c>_FishCloudFlow.z</c>). The whole
		/// scene's, never a window's round the camera — a window's lowest moved every time the window did, and
		/// with it how far every mountain rose, whether the air went over, and how high every cloud was lifted:
		/// the whole sky jumped as the camera walked.
		/// </summary>
		public float LowestGround { get; private set; }

		private TerrainShadowSolver.Grid completeGround;
		private Texture2D terrainTexture;
		private bool terrainReady;

		/// <summary>The highest ground in the scene, barely smoothed, m (the scene terrain map's a).</summary>
		public float HighestGround { get; private set; }
		/// <summary>The highest the ground stands as the pooled air feels it, smoothed some 700 m, m (its r).</summary>
		public float HighestPooled { get; private set; }
		/// <summary>Whether the scene terrain map (<c>_FishCloudTerrain</c> over the whole scene) is published.</summary>
		public bool TerrainReady => terrainReady;
		private Texture2D shadeTexture;
		private Task<float[]> shading;
		private Vector3 shadingLight, shadedLight;
		private bool shadeReady;
		private float shadeBase;
		private float shadeStarted = float.NegativeInfinity;

		/// <summary>Lets the far shade be solved again at once if the light has turned: the clock was set to another moment.</summary>
		public void ShadeSoon() => shadeStarted = float.NegativeInfinity;
		private ushort[] shadeHalves;
		/// <summary>The least time between two of the far shade's solves, s, however fast the light is moving.</summary>
		private const float ShadeInterval = 3f;

		/// <summary>
		/// The scene's heights once read, unsmoothed, as a grid a worker may read (it is never written again:
		/// a new terrain set reads into a new array). Invalid until then.
		/// </summary>
		public TerrainShadowSolver.Grid Ground => completeGround;

		/// <summary>
		/// Builds the field for the scene's terrain when it changes, works out where the terrain shades the
		/// air from <paramref name="toLight"/> over the whole scene (the far grid FishTerrainSunlit reads),
		/// and publishes them.
		/// </summary>
		public void Update(Vector3 toLight)
		{
			TerrainSet terrains = TerrainSet.Current;
			if (terrains.Signature != signature)
			{
				signature = terrains.Signature;
				Begin(terrains);
			}
			if (ground != null && sampled < ground.Length)
			{
				Sample(terrains);
				if (sampled >= ground.Length)
				{
					Solve();
				}
			}
			if (solving != null && solving.IsCompleted)
			{
				Task<float[][]> done = solving;
				solving = null;
				if (done.Status == TaskStatus.RanToCompletion)
				{
					UploadField(done.Result);
				}
				else if (done.Exception != null)
				{
					Debug.LogException(done.Exception.InnerException ?? done.Exception);
				}
			}
			Shade(toLight);
			Publish();
		}

		/// <summary>
		/// The scene's terrain shadow from the light, on a worker, again whenever the light has turned half a
		/// degree: kilometres of evening shadow for the fog lying in the valleys (<see cref="TerrainShadowSolver"/>).
		/// </summary>
		private void Shade(Vector3 toLight)
		{
			if (shading != null && shading.IsCompleted)
			{
				Task<float[]> done = shading;
				shading = null;
				if (done.Status == TaskStatus.RanToCompletion && done.Result != null)
				{
					UploadShade(done.Result);
					shadedLight = shadingLight;
				}
				else if (done.Exception != null)
				{
					Debug.LogException(done.Exception.InnerException ?? done.Exception);
				}
			}
			if (!completeGround.Valid || shading != null || toLight.sqrMagnitude < 1e-6f)
			{
				return;
			}
			Vector3 light = toLight.normalized;
			if (shadeReady && (Vector3.Dot(light, shadedLight) >= ShadeTurnCosine || Time.unscaledTime - shadeStarted < ShadeInterval))
			{
				return;
			}
			shadeStarted = Time.unscaledTime;
			TerrainShadowSolver.Grid scene = completeGround;
			shadingLight = light;
			int mine = generation;
			shading = Task.Run(() => mine == generation ? TerrainShadowSolver.ShadowTops(scene, default, light.x, light.y, light.z, ShadeReach) : null);
		}

		private void UploadShade(float[] tops)
		{
			int n = Resolution;
			if (shadeTexture == null)
			{
				shadeTexture = new Texture2D(n, n, TextureFormat.RHalf, false, true)
				{
					name = "Terrain Shade",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
			}
			// Above the scene's lowest ground, and held within a half's comfortable range.
			shadeBase = lowest;
			shadeHalves ??= new ushort[n * n];
			for (int i = 0; i < n * n; i++)
			{
				shadeHalves[i] = Mathf.FloatToHalf(Mathf.Clamp(tops[i] - shadeBase, -1000f, 4000f));
			}
			shadeTexture.SetPixelData(shadeHalves, 0);
			shadeTexture.Apply(false, false);
			shadeReady = true;
		}

		private void Begin(TerrainSet terrains)
		{
			generation++;
			solving = null;
			rockReady = false;
			fieldReady = false;
			HighestRock = 0f;
			LowestGround = 0f;
			ground = null;
			completeGround = default;
			terrainReady = false;
			shading = null;
			shadeReady = false;
			if (terrains.Extent.width <= 0f || terrains.Extent.height <= 0f)
			{
				return;
			}
			Vector2 min = terrains.Extent.min, max = terrains.Extent.max;
			float span = Mathf.Max(max.x - min.x, max.y - min.y);
			float margin = Mathf.Max(MinimumMargin, span * MarginShare);
			size = span + 2f * margin;
			Vector2 centre = (min + max) * 0.5f;
			corner = centre - new Vector2(size, size) * 0.5f;
			ground = new float[Resolution * Resolution];
			sampled = 0;
		}

		/// <summary>Reads the next few thousand heights; outside every terrain the ground is sea level.</summary>
		private void Sample(TerrainSet terrains)
		{
			float cell = size / Resolution;
			int end = Mathf.Min(ground.Length, sampled + SamplesPerFrame);
			for (; sampled < end; sampled++)
			{
				int x = sampled % Resolution, z = sampled / Resolution;
				ground[sampled] = terrains.TryHeight(corner.x + (x + 0.5f) * cell, corner.y + (z + 0.5f) * cell, out float height)
					? Mathf.Max(0f, height)
					: 0f;
			}
		}

		/// <summary>Builds the rock map here, and hands the flow to a worker.</summary>
		private void Solve()
		{
			int n = Resolution;
			float cell = size / n;
			var rock = new float[n * n];
			lowest = float.MaxValue;
			highest = 0f;
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					float top = 0f;
					for (int oz = Mathf.Max(0, z - 1); oz <= Mathf.Min(n - 1, z + 1); oz++)
					{
						for (int ox = Mathf.Max(0, x - 1); ox <= Mathf.Min(n - 1, x + 1); ox++)
						{
							top = Mathf.Max(top, ground[oz * n + ox]);
						}
					}
					rock[z * n + x] = top;
					float here = ground[z * n + x];
					highest = Mathf.Max(highest, here);
					// The lowest ground under the terrain itself, not the open margin round it.
					if (here > 0f)
					{
						lowest = Mathf.Min(lowest, here);
					}
				}
			}
			if (lowest == float.MaxValue)
			{
				lowest = 0f;
			}
			LowestGround = lowest;
			completeGround = TerrainShadowSolver.Grid.Of(ground, n, corner.x, corner.y, size);
			UploadTerrain(cell);
			float[] massif = MaxFilter(rock, n, Mathf.Max(1, Mathf.CeilToInt(MassifRadius / cell)));
			UploadRock(rock, massif);

			// The flow is turned by the mountain, not by the gullies on it: one pass of a box blur.
			float[] smoothed = BoxBlur(ground, n);
			var levels = new float[Levels];
			for (int k = 0; k < Levels; k++)
			{
				levels[k] = Mathf.Lerp(lowest, highest, k / (float)(Levels - 1));
			}
			solvingLevels = levels;
			int mine = generation;
			solving = Task.Run(() => mine == generation ? TerrainFlowSolver.Solve(smoothed, n, cell, levels) : null);
		}

		private void UploadRock(float[] rock, float[] massif)
		{
			int n = Resolution;
			if (rockTexture == null)
			{
				rockTexture = new Texture2D(n, n, TextureFormat.RGHalf, false, true)
				{
					name = "Cloud Rock",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
			}
			var halves = new ushort[n * n * 2];
			for (int i = 0; i < n * n; i++)
			{
				halves[i * 2] = Mathf.FloatToHalf(rock[i]);
				halves[i * 2 + 1] = Mathf.FloatToHalf(massif[i]);
			}
			rockTexture.SetPixelData(halves, 0);
			rockTexture.Apply(false, false);
			HighestRock = Mathf.Max(1f, highest + 1f);
			rockReady = highest > 0f;
		}

		private void UploadField(float[][] solved)
		{
			if (solved == null || solved.Length != Levels)
			{
				return;
			}
			int n = Resolution;
			if (field == null)
			{
				field = new Texture3D(n, n, Levels, TextureFormat.RGBAHalf, false)
				{
					name = "Cloud Flow",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
			}
			int plane = n * n * TerrainFlowSolver.Channels;
			var halves = new ushort[plane * Levels];
			for (int k = 0; k < Levels; k++)
			{
				float[] level = solved[k];
				for (int i = 0; i < plane; i++)
				{
					halves[k * plane + i] = Mathf.FloatToHalf(level[i]);
				}
			}
			field.SetPixelData(halves, 0);
			field.Apply(false, false);
			fieldReady = true;
		}

		/// <summary>
		/// The ground under the sky as the clouds and the fog read it (<c>_FishCloudTerrain</c>: r the terrain
		/// smoothed as the air feels it, gb which way that rises, a the ground itself), over the whole scene and
		/// fixed in place — in place of CloudTerrainMap's window round the camera, which moved in steps of eight
		/// of its texels and eased its ground to sea level over its outer tenth: every step lifted or dropped the
		/// clouds four and five kilometres out by hundreds of metres, and the sky jumped as the camera travelled.
		/// </summary>
		private void UploadTerrain(float cell)
		{
			int n = Resolution;
			// The ground itself, barely smoothed (two passes of a three-by-three), and the air's (three of a box
			// some 625 m wide), as CloudTerrainMap makes them from its own samples.
			float[] surface = BoxMean(BoxMean(ground, n, 1), n, 1);
			int radius = Mathf.Max(1, Mathf.RoundToInt(AirSmoothMetres / cell));
			float[] air = BoxMean(BoxMean(BoxMean(surface, n, radius), n, radius), n, radius);
			if (terrainTexture == null)
			{
				terrainTexture = new Texture2D(n, n, TextureFormat.RGBAHalf, false, true)
				{
					name = "Cloud Terrain (scene)",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
			}
			var halves = new ushort[n * n * 4];
			float highestGround = 0f, highestPooled = 0f;
			for (int z = 0; z < n; z++)
			{
				int up = Mathf.Min(n - 1, z + 1), down = Mathf.Max(0, z - 1);
				for (int x = 0; x < n; x++)
				{
					int right = Mathf.Min(n - 1, x + 1), left = Mathf.Max(0, x - 1);
					int i = z * n + x;
					float dx = (air[z * n + right] - air[z * n + left]) / ((right - left) * cell);
					float dz = (air[up * n + x] - air[down * n + x]) / ((up - down) * cell);
					halves[i * 4] = Mathf.FloatToHalf(air[i]);
					halves[i * 4 + 1] = Mathf.FloatToHalf(dx);
					halves[i * 4 + 2] = Mathf.FloatToHalf(dz);
					halves[i * 4 + 3] = Mathf.FloatToHalf(surface[i]);
					highestGround = Mathf.Max(highestGround, surface[i]);
					highestPooled = Mathf.Max(highestPooled, air[i]);
				}
			}
			terrainTexture.SetPixelData(halves, 0);
			terrainTexture.Apply(false, false);
			HighestGround = highestGround;
			HighestPooled = highestPooled;
			terrainReady = true;
		}

		private static float[] BoxMean(float[] source, int n, int radius)
		{
			var across = new float[n * n];
			var result = new float[n * n];
			for (int z = 0; z < n; z++)
			{
				float sum = 0f;
				int count = 0;
				for (int o = 0; o <= Mathf.Min(n - 1, radius); o++)
				{
					sum += source[z * n + o];
					count++;
				}
				for (int x = 0; x < n; x++)
				{
					across[z * n + x] = sum / count;
					int add = x + radius + 1, drop = x - radius;
					if (add < n) { sum += source[z * n + add]; count++; }
					if (drop >= 0) { sum -= source[z * n + drop]; count--; }
				}
			}
			for (int x = 0; x < n; x++)
			{
				float sum = 0f;
				int count = 0;
				for (int o = 0; o <= Mathf.Min(n - 1, radius); o++)
				{
					sum += across[o * n + x];
					count++;
				}
				for (int z = 0; z < n; z++)
				{
					result[z * n + x] = sum / count;
					int add = z + radius + 1, drop = z - radius;
					if (add < n) { sum += across[add * n + x]; count++; }
					if (drop >= 0) { sum -= across[drop * n + x]; count--; }
				}
			}
			return result;
		}

		private void Publish()
		{
			// After CloudTerrainMap has published its window this frame: the scene's map, fixed in place, wins.
			if (terrainReady)
			{
				Shader.SetGlobalTexture(TerrainId, terrainTexture);
				Shader.SetGlobalVector(TerrainRectId, new Vector4(corner.x, corner.y, size, 1f));
			}
			if (rockReady)
			{
				Shader.SetGlobalTexture(RockId, rockTexture);
			}
			if (fieldReady)
			{
				Shader.SetGlobalTexture(FieldId, field);
			}
			else
			{
				if (placeholderField == null)
				{
					placeholderField = new Texture3D(1, 1, 1, field != null ? field.format : TextureFormat.RGBAHalf, false)
					{
						name = "Cloud flow field placeholder",
						hideFlags = HideFlags.DontSave,
					};
					placeholderField.SetPixel(0, 0, 0, Color.clear);
					placeholderField.Apply(false, true);
				}
				Shader.SetGlobalTexture(FieldId, placeholderField);
			}
			// w: 1 the rock is there, 2 the flow is too. The shader reads neither below what it needs.
			float state = fieldReady ? 2f : (rockReady ? 1f : 0f);
			Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, size, state));
			Shader.SetGlobalVector(LevelsId, solvingLevels != null
				? new Vector4(solvingLevels[0], solvingLevels[Levels - 1], Levels, 0f)
				: Vector4.zero);
			Shader.SetGlobalFloat(RockTopId, rockReady ? HighestRock : 0f);
			if (shadeReady)
			{
				Shader.SetGlobalTexture(ShadeId, shadeTexture);
			}
			// The volumetric fog's compute declares these too and must have something bound to dispatch (it reads them
			// only when the rect's flags say they are there). Only where nothing at all is bound yet: CloudTerrainMap
			// publishes its own window under the terrain's name, and that must not be overwritten.
			BindIfUnset(TerrainId);
			BindIfUnset(RockId);
			BindIfUnset(ShadeId);
			Shader.SetGlobalVector(ShadeRectId, new Vector4(corner.x, corner.y, size, shadeReady ? 1f : 0f));
			Shader.SetGlobalFloat(ShadeBaseId, shadeBase);
		}

		/// <summary>The highest value within <paramref name="radius"/> cells, separably.</summary>
		private static float[] MaxFilter(float[] source, int n, int radius)
		{
			var across = new float[n * n];
			var result = new float[n * n];
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					float top = 0f;
					for (int o = Mathf.Max(0, x - radius); o <= Mathf.Min(n - 1, x + radius); o++)
					{
						top = Mathf.Max(top, source[z * n + o]);
					}
					across[z * n + x] = top;
				}
			}
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					float top = 0f;
					for (int o = Mathf.Max(0, z - radius); o <= Mathf.Min(n - 1, z + radius); o++)
					{
						top = Mathf.Max(top, across[o * n + x]);
					}
					result[z * n + x] = top;
				}
			}
			return result;
		}

		private static float[] BoxBlur(float[] source, int n)
		{
			var result = new float[n * n];
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					float sum = 0f;
					int count = 0;
					for (int oz = Mathf.Max(0, z - 1); oz <= Mathf.Min(n - 1, z + 1); oz++)
					{
						for (int ox = Mathf.Max(0, x - 1); ox <= Mathf.Min(n - 1, x + 1); ox++)
						{
							sum += source[oz * n + ox];
							count++;
						}
					}
					result[z * n + x] = sum / count;
				}
			}
			return result;
		}

		public void Dispose()
		{
			generation++;
			solving = null;
			if (field != null)
			{
				if (Application.isPlaying) Object.Destroy(field); else Object.DestroyImmediate(field);
				field = null;
			}
			if (rockTexture != null)
			{
				if (Application.isPlaying) Object.Destroy(rockTexture); else Object.DestroyImmediate(rockTexture);
				rockTexture = null;
			}
			if (shadeTexture != null)
			{
				if (Application.isPlaying) Object.Destroy(shadeTexture); else Object.DestroyImmediate(shadeTexture);
				shadeTexture = null;
			}
			if (terrainTexture != null)
			{
				if (Application.isPlaying) Object.Destroy(terrainTexture); else Object.DestroyImmediate(terrainTexture);
				terrainTexture = null;
			}
			terrainReady = false;
			shading = null;
			shadeReady = false;
			Shader.SetGlobalVector(ShadeRectId, Vector4.zero);
			rockReady = false;
			fieldReady = false;
			Shader.SetGlobalVector(RectId, Vector4.zero);
			Shader.SetGlobalFloat(RockTopId, 0f);
		}
	}
}
