using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the scene's volcanic plumes: an eruption column, its umbrella and the ash falling out of
	/// it where there is air; a ballistic fountain over each vent and lava lake where there is none.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The plume drawn is the plume the ash falls from.</b> With air, the plumes are
	/// <see cref="VolcanicVents.Plumes"/> asked exactly as <see cref="WeatherField.Sample"/> asks it —
	/// the same vents, the same eruption cells, the same planet and the same open-air wind — so the
	/// column leans and the umbrella streams the way the weather's fallout runs, and the ash the
	/// precipitation field draws round the viewer is this umbrella's.
	/// </para>
	/// <para>
	/// <b>Without air</b> nothing is buoyant and nothing is weather: each vent (the vented biomes',
	/// <see cref="VolcanicVents.Of"/>) and each lava lake (<see cref="VolcanicVents.LavaLakes"/>, under
	/// the scene's lava surface) is a fountain of grains on drag-free arcs, as tall as v²/2g on this
	/// world's gravity, landing in a ring round it with no drift. Client only: nothing about it is
	/// networked, and every client works out the same vents from the same map.
	/// </para>
	/// <para>
	/// Reuses the precipitation field's machinery rather than a renderer of its own kind: the same
	/// pre-built field of quads placed by the vertex shader, the same weather atlas (the ash row), and
	/// the same light (FishTrilight and the main light, as the curtains and particles are lit).
	/// </para>
	/// </remarks>
	public sealed class VolcanicPlumePresenter : MonoBehaviour, IWeatherPresenter
	{
		/// <summary>Quads in the field; each population draws a share of them.</summary>
		public const int Particles = 2048;
		/// <summary>The most plumes or fountains drawn at once, the nearest.</summary>
		public const int MaxDrawn = 4;
		/// <summary>The atlas row of soft ragged blobs (WeatherTextureBaker: ash and embers).</summary>
		public const int AshRow = 3;
		/// <summary>The shader's name, for a profile with no plume material (an editor preview).</summary>
		public const string ShaderName = "FishMMO/Weather/Volcanic Plume";
		/// <summary>
		/// The lava surface's shader, by name: the lava lives in the water plugin, which the client does
		/// not reference, and its level is all this needs.
		/// </summary>
		public const string LavaShaderName = "FishMMO/Water/Lava";
		/// <summary>What a lava lake's fountain is made of when nothing says otherwise: sulphur-dioxide frost, Io's white.</summary>
		public static readonly Color FrostTint = new Color(0.88f, 0.88f, 0.84f, 1f);
		/// <summary>Ash with no substance of its own.</summary>
		public static readonly Color AshTint = new Color(0.45f, 0.43f, 0.4f, 1f);

		private static readonly int VentId = Shader.PropertyToID("_PlumeVent");
		private static readonly int FormId = Shader.PropertyToID("_PlumeForm");
		private static readonly int RiseId = Shader.PropertyToID("_PlumeRise");
		private static readonly int LeanId = Shader.PropertyToID("_PlumeLean");
		private static readonly int DriftId = Shader.PropertyToID("_PlumeDrift");
		private static readonly int BallisticId = Shader.PropertyToID("_PlumeBallistic");
		private static readonly int ColorId = Shader.PropertyToID("_PlumeColor");
		private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

		private WeatherPresentation presentation;
		private WeatherContext context;
		private bool hasContext;
		private Mesh mesh;
		private Material fallbackMaterial;
		private MaterialPropertyBlock block;
		private float clock;
		private readonly List<VolcanicPlume.Plume> plumes = new List<VolcanicPlume.Plume>();
		private readonly List<(Vector3 vent, float emission, Color tint, float distance)> fountains = new List<(Vector3, float, Color, float)>();

		// The lava lakes of the scene last asked about: found once, from its terrains.
		private int lakesScene = int.MinValue;
		private readonly List<Vector2> lakes = new List<Vector2>();
		private float lakeLevel;

		private void Awake()
		{
			presentation = GetComponent<WeatherPresentation>();
			block = new MaterialPropertyBlock();
			WeatherClient.RegisterPresenter(this);
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		}

		private void OnDestroy()
		{
			WeatherClient.UnregisterPresenter(this);
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			if (mesh != null)
			{
				Destroy(mesh);
			}
			if (fallbackMaterial != null)
			{
				Destroy(fallbackMaterial);
			}
		}

		public void Apply(in WeatherFrame frame, in WeatherContext ctx)
		{
			context = ctx;
			hasContext = true;
		}

		public void Reset()
		{
			hasContext = false;
			lakesScene = int.MinValue;
			lakes.Clear();
		}

		private void Update()
		{
			// On the world's motion, as what falls is: a held clock holds the plumes.
			clock += WorldMotion.Scale(Time.deltaTime);
		}

		private Material ResolveMaterial(WeatherRenderProfile profile)
		{
			if (profile != null && profile.PlumeMaterial != null)
			{
				return profile.PlumeMaterial;
			}
			if (fallbackMaterial == null)
			{
				Shader shader = Shader.Find(ShaderName);
				if (shader != null)
				{
					fallbackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
				}
			}
			return fallbackMaterial;
		}

		private void OnBeginCamera(ScriptableRenderContext renderContext, Camera camera)
		{
			if (!hasContext || camera == null || context.Timeline == null || context.Timeline.SceneMode != WeatherSceneMode.Own)
			{
				return;
			}
			Camera wanted = presentation != null && presentation.TargetCamera != null ? presentation.TargetCamera : Camera.main;
			if (camera != wanted)
			{
				return;
			}
			WeatherRenderProfile profile = presentation != null ? presentation.Profile : WeatherRenderProfile.Active;
			Material material = ResolveMaterial(profile);
			if (material == null)
			{
				return;
			}
			if (mesh == null)
			{
				mesh = BuildMesh(Particles);
			}
			Vector3 viewer = camera.transform.position;
			var viewer2 = new Vector2(viewer.x, viewer.z);
			float reach = camera.farClipPlane * 1.5f + 20000f;

			WeatherSample sample = context.Sample;
			if (VolcanicPlume.CanRise(sample.Planet))
			{
				// The same call the weather made: the same plumes.
				VolcanicVents.Plumes(context.Timeline, context.Settings, (uint)context.Tick, sample.Planet, sample.OpenAir.Wind, viewer2, plumes);
				plumes.Sort((a, b) => (a.Vent - viewer2).sqrMagnitude.CompareTo((b.Vent - viewer2).sqrMagnitude));
				int drawn = 0;
				for (int i = 0; i < plumes.Count && drawn < MaxDrawn; i++)
				{
					if ((plumes[i].Vent - viewer2).magnitude - VolcanicPlume.FalloutLength(plumes[i]) > reach)
					{
						continue;
					}
					DrawPlume(plumes[i], camera, material, profile);
					drawn++;
				}
				return;
			}

			WorldBody body = context.Timeline.BodyOverride != null ? context.Timeline.BodyOverride : SceneTime.BodyOf(context.Settings);
			float gravity = SurfacePhysics.Gravity(body);
			PlanetAir planet = PlanetAir.For(SolarSystemProfile.Active, body);
			if (body == null || !VolcanicPlume.IsFountain(planet, gravity))
			{
				return;
			}
			GatherFountains(viewer2, reach);
			for (int i = 0; i < fountains.Count && i < MaxDrawn; i++)
			{
				DrawFountain(fountains[i].vent, fountains[i].emission, fountains[i].tint, gravity, camera, material, profile);
			}
		}

		// ── With air: column, umbrella and fall ─────────────────────────

		private void DrawPlume(in VolcanicPlume.Plume plume, Camera camera, Material material, WeatherRenderProfile profile)
		{
			float ground = GroundAt(context.Scene, plume.Vent);
			var vent = new Vector3(plume.Vent.x, ground, plume.Vent.y);
			float u = plume.Wind.magnitude;
			Vector2 down = u >= VolcanicPlume.CalmWind ? plume.Wind / u : Vector2.zero;
			float length = VolcanicPlume.FalloutLength(plume);
			// The umbrella is drawn as far downwind as it stays worth seeing: three of its thinning
			// lengths, and never more than the sky draws to.
			float drawn = Mathf.Min(3f * length, 40000f);
			float riseSeconds = plume.Top / Mathf.Max(1f, VolcanicPlume.RiseSpeed(plume.Top, GravityOf(context.Sample.Planet)));
			float driftSeconds = drawn / Mathf.Max(1f, u);
			float fallSeconds = plume.Umbrella / Mathf.Max(0.05f, plume.FallSpeed);
			Color tint = plume.Substance != null ? plume.Substance.Tint : AshTint;
			float glow = plume.Substance != null ? plume.Substance.Emission : 0f;
			float strength = 0.4f + 0.6f * plume.Emission;

			var rise = new Vector4(plume.Top, plume.Umbrella, plume.Radius, riseSeconds);
			var lean = new Vector4(plume.TopOffset.x, plume.TopOffset.y, down.x, down.y);
			var drift = new Vector4(drawn, driftSeconds, VolcanicPlume.SpreadPerMetre, length);
			var colour = new Color(tint.r, tint.g, tint.b, 0.6f + glow);

			// The box round all of it: the vent, the top, and the umbrella out to where it is drawn.
			float wide = plume.Radius + VolcanicPlume.SpreadPerMetre * drawn;
			Vector2 far = plume.Vent + plume.TopOffset + down * drawn;
			var min = new Vector3(Mathf.Min(plume.Vent.x, far.x) - wide, ground - 50f, Mathf.Min(plume.Vent.y, far.y) - wide);
			var max = new Vector3(Mathf.Max(plume.Vent.x, far.x) + wide, ground + plume.Top * 1.3f + 200f, Mathf.Max(plume.Vent.y, far.y) + wide);
			var bounds = new Bounds((min + max) * 0.5f, max - min);

			Draw(material, profile, camera, bounds, vent, 0, 0.35f, 0.5f * strength, rise, lean, drift, Vector4.zero, colour);
			Draw(material, profile, camera, bounds, vent, 1, 0.65f, 0.35f * strength, rise, lean, drift, Vector4.zero, colour);
			Draw(material, profile, camera, bounds, vent, 2, 0.4f, 0.12f * strength, rise, lean, drift, new Vector4(fallSeconds, 0f, 0f, 0f), colour);
		}

		private static float GravityOf(in PlanetAir planet) => planet.Gravity > 0f ? planet.Gravity : SurfacePhysics.EarthGravity;

		// ── Without air: fountains ──────────────────────────────────────

		private void GatherFountains(Vector2 viewer, float reach)
		{
			fountains.Clear();
			foreach (VentSite vent in VolcanicVents.Of(context.Settings))
			{
				float d = (vent.Position - viewer).magnitude;
				if (d <= reach)
				{
					float ground = GroundAt(context.Scene, vent.Position);
					Color tint = vent.Substance != null ? vent.Substance.Tint : FrostTint;
					fountains.Add((new Vector3(vent.Position.x, ground, vent.Position.y), vent.Emission, tint, d));
				}
			}
			FindLakes(context.Scene);
			foreach (Vector2 lake in lakes)
			{
				float d = (lake - viewer).magnitude;
				if (d <= reach)
				{
					// Over the lake's surface; a lake is a vent of no particular strength.
					fountains.Add((new Vector3(lake.x, lakeLevel, lake.y), 0.08f, FrostTint, d));
				}
			}
			fountains.Sort((a, b) => a.distance.CompareTo(b.distance));
		}

		private void DrawFountain(Vector3 vent, float emission, Color tint, float gravity, Camera camera, Material material, WeatherRenderProfile profile)
		{
			float speed = VolcanicPlume.LaunchSpeed(emission);
			float height = VolcanicPlume.FountainHeight(speed, gravity);
			VolcanicPlume.FalloutRing(speed, gravity, out _, out float outer);
			float puff = Mathf.Clamp(height / 30f, 4f, 40f);
			var bounds = new Bounds(vent + Vector3.up * (0.5f * height), new Vector3(2f * outer + 2f * puff, height + 2f * puff, 2f * outer + 2f * puff));
			var ballistic = new Vector4(speed, gravity, VolcanicPlume.FountainConeDegrees * Mathf.Deg2Rad, VolcanicPlume.FountainInnerShare);
			Draw(material, profile, camera, bounds, vent, 3, 1f, 0.14f, new Vector4(puff, 0f, 0f, 0f), Vector4.zero, Vector4.zero, ballistic,
				new Color(tint.r, tint.g, tint.b, 0.4f));
		}

		/// <summary>The lava lakes under the scene's lava surface, found once per scene.</summary>
		private void FindLakes(Scene scene)
		{
			if (!scene.IsValid() || lakesScene == scene.handle)
			{
				return;
			}
			lakesScene = scene.handle;
			lakes.Clear();
			if (!LavaLevel(scene, out lakeLevel))
			{
				return;
			}
			var terrains = new List<Terrain>();
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain != null && terrain.gameObject.scene == scene && terrain.terrainData != null)
				{
					terrains.Add(terrain);
				}
			}
			if (terrains.Count == 0)
			{
				return;
			}
			Rect area = AreaOf(terrains);
			VolcanicVents.LavaLakes(area, lakeLevel, p => HeightOn(terrains, p), lakes);
		}

		/// <summary>The scene's lava surface's level, from the renderer drawing the lava material.</summary>
		private static bool LavaLevel(Scene scene, out float level)
		{
			level = 0f;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
				{
					Material m = renderer.sharedMaterial;
					if (m != null && m.shader != null && m.shader.name == LavaShaderName)
					{
						level = renderer.transform.position.y;
						return true;
					}
				}
			}
			return false;
		}

		private static Rect AreaOf(List<Terrain> terrains)
		{
			Vector3 min = terrains[0].GetPosition(), max = min + terrains[0].terrainData.size;
			foreach (Terrain t in terrains)
			{
				min = Vector3.Min(min, t.GetPosition());
				max = Vector3.Max(max, t.GetPosition() + t.terrainData.size);
			}
			return Rect.MinMaxRect(min.x, min.z, max.x, max.z);
		}

		private static float HeightOn(List<Terrain> terrains, Vector2 p)
		{
			foreach (Terrain t in terrains)
			{
				Vector3 o = t.GetPosition();
				Vector3 s = t.terrainData.size;
				if (p.x >= o.x && p.x <= o.x + s.x && p.y >= o.z && p.y <= o.z + s.z)
				{
					return t.SampleHeight(new Vector3(p.x, 0f, p.y)) + o.y;
				}
			}
			return float.NaN;
		}

		private static float GroundAt(Scene scene, Vector2 p)
		{
			foreach (Terrain t in Terrain.activeTerrains)
			{
				if (t == null || t.terrainData == null || (scene.IsValid() && t.gameObject.scene != scene))
				{
					continue;
				}
				Vector3 o = t.GetPosition();
				Vector3 s = t.terrainData.size;
				if (p.x >= o.x && p.x <= o.x + s.x && p.y >= o.z && p.y <= o.z + s.z)
				{
					return t.SampleHeight(new Vector3(p.x, 0f, p.y)) + o.y;
				}
			}
			return 0f;
		}

		// ── Drawing ─────────────────────────────────────────────────────

		private void Draw(Material material, WeatherRenderProfile profile, Camera camera, Bounds bounds, Vector3 vent, int population, float share, float opacity,
			Vector4 rise, Vector4 lean, Vector4 drift, Vector4 ballistic, Color colour)
		{
			block.Clear();
			if (profile != null && profile.PrecipitationAtlas != null)
			{
				block.SetTexture(MainTexId, profile.PrecipitationAtlas);
			}
			block.SetVector(VentId, new Vector4(vent.x, vent.y, vent.z, clock));
			block.SetVector(FormId, new Vector4(population, share, opacity, AshRow));
			block.SetVector(RiseId, rise);
			block.SetVector(LeanId, lean);
			block.SetVector(DriftId, drift);
			block.SetVector(BallisticId, ballistic);
			block.SetColor(ColorId, colour);
			var rp = new RenderParams(material)
			{
				camera = camera,
				matProps = block,
				worldBounds = bounds,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
			};
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}

		/// <summary>A field of quads: per vertex its corner, and per quad four uniform numbers, the same for each corner.</summary>
		public static Mesh BuildMesh(int particles)
		{
			var random = new System.Random(1883);
			var positions = new Vector3[particles * 4];
			var corners = new Vector2[particles * 4];
			var randoms = new Vector4[particles * 4];
			var indices = new int[particles * 6];
			for (int i = 0; i < particles; i++)
			{
				var r = new Vector4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
				for (int c = 0; c < 4; c++)
				{
					int v = i * 4 + c;
					corners[v] = new Vector2(c == 1 || c == 2 ? 1f : 0f, c >= 2 ? 1f : 0f);
					randoms[v] = r;
				}
				int k = i * 6, b = i * 4;
				indices[k] = b; indices[k + 1] = b + 1; indices[k + 2] = b + 2;
				indices[k + 3] = b; indices[k + 4] = b + 2; indices[k + 5] = b + 3;
			}
			var mesh = new Mesh { name = "Volcanic Plume Particles", hideFlags = HideFlags.HideAndDontSave };
			mesh.indexFormat = particles * 4 > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.SetVertices(positions);
			mesh.SetUVs(0, corners);
			mesh.SetUVs(1, randoms);
			mesh.SetIndices(indices, MeshTopology.Triangles, 0);
			// Placed by the shader, so the mesh's own bounds mean nothing: the draw's are used.
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
			return mesh;
		}
	}
}
