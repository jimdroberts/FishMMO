using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// A sea hall's vents and ice near the camera (2026-10-26, phase I2n-b, decision 83): its smokers' plumes, the smoke ceiling they
	/// spread into, the flange mirrors, the white smokers' carbon-dioxide beads, the brinicles growing and snapping from its dome's roof
	/// and its brine pools' mirrors (<see cref="SceneSeaHallVents"/>; FishSeaHallVents.shader). The hall is the cut's: its Hall, Pool,
	/// Smoker and Brine cave-life spots (every client loads them, PointOfInterestCaveDressing.SeaHall.cs writes them from the hall's stored
	/// smokers). Presentation only: nothing on the wire, no collider, no harm.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shared.</b> Every part is a pure function of the shared motion clock (WorldMotion.Seconds) and the sky's plume tide
	/// (GlowSky.Current, itself the world clock's): a smoker's strength is worked out here each frame from those, its puffs, beads and
	/// brinicles on the GPU from the same clock, so every player sees the same plume falter and the same brinicle snap.
	/// </para>
	/// <para>
	/// <b>Cost.</b> Nothing at all on a scene with no sea hall (every scene until a frozen-through cryovolcanic world's hosts are cut):
	/// one version check a frame. Else, for the one hall nearest the camera within its reach, one RenderMesh call of a pre-built mesh
	/// (~2 600 quads, most collapsed to nothing in the vertex stage). Play mode, client only, self-starting like the cave water life; the
	/// shader comes from the weather render profile (so a client build includes it), else by name in the editor.
	/// </para>
	/// </remarks>
	public static class SeaHallVents
	{
		public const string ShaderName = "FishMMO/World/Sea Hall Vents";
		/// <summary>Metres past a hall's dome a camera still draws it from (its plumes are seen across the water from the ledge).</summary>
		public const float Reach = 60f;
		/// <summary>Metres from the camera a smoker is drawn to.</summary>
		public const float SmokerReach = 160f;
		private const float Refind = 6f;

		/// <summary>The shader's properties (their own class, so nothing native runs until the first draw: the pure readers stay testable).</summary>
		private static class Ids
		{
			public static readonly Unity.Profiling.ProfilerMarker DrawMarker = new Unity.Profiling.ProfilerMarker("SeaHallVents.Draw");
			public static readonly int SmokersId = Shader.PropertyToID("_HallSmokers");
			public static readonly int ShapeId = Shader.PropertyToID("_HallSmokerShape");
			public static readonly int SeedsId = Shader.PropertyToID("_HallSmokerSeed");
			public static readonly int DomeId = Shader.PropertyToID("_HallDome");
			public static readonly int RadiiId = Shader.PropertyToID("_HallRadii");
			public static readonly int InfoId = Shader.PropertyToID("_HallInfo");
			public static readonly int WaterId = Shader.PropertyToID("_HallWater");
			public static readonly int BrineId = Shader.PropertyToID("_HallBrine");
		}

		/// <summary>A smoker as drawn: its spot's numbers.</summary>
		public struct Smoker
		{
			public Vector3 Orifice;
			public float Radius, Height, Clearance, Yaw;
			public int Kind;
			public uint Seed;
		}

		/// <summary>A hall as drawn: its dome, its water, its smokers and brine pools.</summary>
		public sealed class Hall
		{
			public int SiteId;
			public Vector3 Centre, Radii;
			public float Yaw, Water, Waterline;
			public uint Seed;
			public readonly List<Smoker> Smokers = new List<Smoker>();
			public readonly List<Vector4> Brine = new List<Vector4>();
		}

		private static readonly List<Hall> halls = new List<Hall>();
		private static readonly List<int> drawn = new List<int>();
		private static readonly Vector4[] smokers = new Vector4[SceneSeaHallVents.MaxSmokers];
		private static readonly Vector4[] shapes = new Vector4[SceneSeaHallVents.MaxSmokers];
		private static readonly Vector4[] seeds = new Vector4[SceneSeaHallVents.MaxSmokers];
		private static readonly Vector4[] brine = new Vector4[4];
		private static readonly NearestSmokerFirst nearestFirst = new NearestSmokerFirst();
		private static Material material;
		private static Shader materialShader;
		private static Mesh mesh;
		private static Hall near;
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int gatheredLife = -1;
		private static bool hooked;


		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			hooked = false;
			gatheredLife = -1;
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			SceneManager.sceneUnloaded += _ => gatheredLife = -1;
			Application.quitting += Shutdown;
		}

		private static void Shutdown()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			hooked = false;
			Release();
		}

		private static void Release()
		{
			if (material != null)
			{
				Object.Destroy(material);
				material = null;
			}
			materialShader = null;
			if (mesh != null)
			{
				Object.Destroy(mesh);
				mesh = null;
			}
			halls.Clear();
			drawn.Clear();
			near = null;
		}

		/// <summary>
		/// The halls in a scene's cave-life spots (pure, for tests): one per Hall spot, its water its site's widest SeaHall Pool spot, its
		/// smokers and brine pools its site's Smoker and Brine spots.
		/// </summary>
		public static List<Hall> HallsOf(IReadOnlyList<SceneCaveLifeSpot> spots)
		{
			var found = new List<Hall>();
			if (spots == null)
			{
				return found;
			}
			var bySite = new Dictionary<int, Hall>();
			for (int i = 0; i < spots.Count; i++)
			{
				SceneCaveLifeSpot s = spots[i];
				if (s.Kind == SceneCaveLifeKind.Hall && s.Form == SceneCaveForm.SeaHall && !bySite.ContainsKey(s.SiteId))
				{
					var hall = new Hall { SiteId = s.SiteId, Centre = s.Position, Radii = s.Extent, Yaw = s.Yaw, Water = float.NaN, Seed = SceneSeaHallVents.SeedOf(s.Position) };
					bySite[s.SiteId] = hall;
					found.Add(hall);
				}
			}
			for (int i = 0; i < spots.Count; i++)
			{
				SceneCaveLifeSpot s = spots[i];
				if (s.Form != SceneCaveForm.SeaHall || !bySite.TryGetValue(s.SiteId, out Hall hall))
				{
					continue;
				}
				switch (s.Kind)
				{
					case SceneCaveLifeKind.Pool:
						if (float.IsNaN(hall.Water) || s.Extent.x > hall.Waterline)
						{
							hall.Water = s.Position.y;
							hall.Waterline = s.Extent.x;
						}
						break;
					case SceneCaveLifeKind.Smoker:
						hall.Smokers.Add(new Smoker
						{
							Orifice = s.Position, Radius = s.Extent.x, Height = s.Extent.y, Clearance = s.Extent.z, Yaw = s.Yaw,
							Kind = SceneSeaHallVents.KindOf(s.Flags), Seed = SceneSeaHallVents.SeedOf(s.Position),
						});
						break;
					case SceneCaveLifeKind.Brine:
						if (hall.Brine.Count < brine.Length)
						{
							hall.Brine.Add(new Vector4(s.Position.x, s.Position.y, s.Position.z, s.Extent.x));
						}
						break;
				}
			}
			found.RemoveAll(h => float.IsNaN(h.Water));
			return found;
		}

		/// <summary>
		/// How far a smoker's plume climbs, metres: to the water's underside for a smoker under it (half a metre short), to the ice roof
		/// over the air pocket for one whose orifice stands out of the water (the Great Smoker's), never past the roof.
		/// </summary>
		public static float RiseOf(in Smoker smoker, float water)
		{
			float roof = Mathf.Max(0.5f, smoker.Clearance - 1.5f);
			return smoker.Orifice.y < water ? Mathf.Clamp(water - 0.5f - smoker.Orifice.y, 0.5f, roof) : roof;
		}

		private static void Gather()
		{
			gatheredLife = SceneCaveLife.Version;
			halls.Clear();
			near = null;
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
			foreach (SceneCaveLife.Set set in SceneCaveLife.Sets)
			{
				halls.AddRange(HallsOf(set.Spots));
			}
		}

		private static bool ResolveMaterial()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile != null && profile.SeaHallVentsShader != null ? profile.SeaHallVentsShader : materialShader != null ? materialShader : Shader.Find(ShaderName);
			if (shader == null)
			{
				return false;
			}
			if (material == null || materialShader != shader)
			{
				if (material != null)
				{
					Object.Destroy(material);
				}
				material = new Material(shader) { name = "Sea Hall Vents", hideFlags = HideFlags.HideAndDontSave };
				materialShader = shader;
			}
			return true;
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			if (SceneCaveLife.Version != gatheredLife)
			{
				Gather();
			}
			if (halls.Count == 0)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (camera == Camera.main && (eye - foundAt).sqrMagnitude > Refind * Refind)
			{
				foundAt = eye;
				near = Nearest(eye);
			}
			if (near == null)
			{
				return;
			}
			using (Ids.DrawMarker.Auto())
			{
				Draw(camera, eye, near);
			}
		}

		/// <summary>The hall whose dome the eye is within <see cref="Reach"/> of, nearest first; null for none.</summary>
		private static Hall Nearest(Vector3 eye)
		{
			Hall best = null;
			float bestD = float.MaxValue;
			foreach (Hall hall in halls)
			{
				float r = Mathf.Max(hall.Radii.x, Mathf.Max(hall.Radii.y, hall.Radii.z)) + Reach;
				float d = (eye - hall.Centre).sqrMagnitude;
				if (d < r * r && d < bestD)
				{
					best = hall;
					bestD = d;
				}
			}
			return best;
		}

		private static void Draw(Camera camera, Vector3 eye, Hall hall)
		{
			if (!ResolveMaterial())
			{
				return;
			}
			if (mesh == null)
			{
				mesh = BuildMesh();
			}
			double seconds = WorldMotion.Seconds;
			float wrapped = WorldMotion.Wrapped(SceneSeaHallVents.Wrap);
			SceneGlowSky sky = GlowSky.Current;
			float night = SceneGlowModes.Value(SceneGlowMode.Night, 0, default, wrapped, hall.Centre.x, hall.Centre.z, in sky);
			// The nearest smokers first, as many as the shader holds.
			drawn.Clear();
			for (int i = 0; i < hall.Smokers.Count; i++)
			{
				if ((hall.Smokers[i].Orifice - eye).sqrMagnitude <= SmokerReach * SmokerReach)
				{
					drawn.Add(i);
				}
			}
			// A reused comparer: a lambda over the hall and the eye made its closure on every draw.
			nearestFirst.Hall = hall;
			nearestFirst.Eye = eye;
			drawn.Sort(nearestFirst);
			nearestFirst.Hall = null;
			if (drawn.Count > SceneSeaHallVents.MaxSmokers)
			{
				drawn.RemoveRange(SceneSeaHallVents.MaxSmokers, drawn.Count - SceneSeaHallVents.MaxSmokers);
			}
			for (int i = 0; i < SceneSeaHallVents.MaxSmokers; i++)
			{
				if (i >= drawn.Count)
				{
					smokers[i] = shapes[i] = seeds[i] = Vector4.zero;
					continue;
				}
				Smoker s = hall.Smokers[drawn[i]];
				float strength = SceneSeaHallVents.Strength(sky.PlumeTide, wrapped, s.Seed, s.Kind == SceneSeaHallVents.Great);
				float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SmokerReach * 0.75f, SmokerReach, Vector3.Distance(eye, s.Orifice)));
				smokers[i] = new Vector4(s.Orifice.x, s.Orifice.y, s.Orifice.z, s.Radius);
				shapes[i] = new Vector4(s.Height, RiseOf(in s, hall.Water), strength, s.Kind);
				// The seed in two 16-bit halves (a 32-bit seed in one float rounds).
				seeds[i] = new Vector4(s.Seed >> 16, s.Seed & 0xFFFFu, s.Yaw, fade);
			}
			for (int i = 0; i < brine.Length; i++)
			{
				brine[i] = i < hall.Brine.Count ? hall.Brine[i] : Vector4.zero;
			}
			float reach = Mathf.Max(hall.Radii.x, Mathf.Max(hall.Radii.y, hall.Radii.z));
			float hallFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach + 0.6f * Reach, reach + Reach, Vector3.Distance(eye, hall.Centre)));
			/* No property block, so the draw stays in the SRP Batcher: the arrays are globals (only this draw reads them, set in the
			 * camera's own hook before it renders) and the hall's numbers are the material's own (this system's, never shared). */
			Shader.SetGlobalVectorArray(Ids.SmokersId, smokers);
			Shader.SetGlobalVectorArray(Ids.ShapeId, shapes);
			Shader.SetGlobalVectorArray(Ids.SeedsId, seeds);
			Shader.SetGlobalVectorArray(Ids.BrineId, brine);
			material.SetVector(Ids.DomeId, new Vector4(hall.Centre.x, hall.Centre.y, hall.Centre.z, hall.Yaw * Mathf.Deg2Rad));
			material.SetVector(Ids.RadiiId, new Vector4(hall.Radii.x, hall.Radii.y, hall.Radii.z, hall.Water));
			material.SetVector(Ids.InfoId, new Vector4(wrapped, SceneSeaHallVents.BrinicleSeconds(seconds), drawn.Count, night));
			material.SetVector(Ids.WaterId, new Vector4(hall.Waterline, hall.Seed >> 16, hall.Seed & 0xFFFFu, hallFade));
			Graphics.RenderMesh(new RenderParams(material)
			{
				camera = camera,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
				worldBounds = new Bounds(hall.Centre, 2f * (hall.Radii + Vector3.one * 10f)),
			}, mesh, 0, Matrix4x4.identity);
		}

		/// <summary>Orders smoker indices of <see cref="Hall"/> nearest <see cref="Eye"/> first.</summary>
		private sealed class NearestSmokerFirst : IComparer<int>
		{
			public Hall Hall;
			public Vector3 Eye;

			public int Compare(int a, int b)
			{
				return (Hall.Smokers[a].Orifice - Eye).sqrMagnitude.CompareTo((Hall.Smokers[b].Orifice - Eye).sqrMagnitude);
			}
		}

		/// <summary>
		/// The quads, each vertex naming its part, its smoker (or grove, or pool) and which of its parts (POSITION), and its corner
		/// (TEXCOORD0): plumes, ceiling, flanges and beads a smoker, brinicles (two crossed planes each) and snaps a grove, one mirror a pool.
		/// </summary>
		private static Mesh BuildMesh()
		{
			var positions = new List<Vector3>();
			var corners = new List<Vector2>();
			var indices = new List<int>();
			void Quad(int part, int index, int which)
			{
				int v = positions.Count;
				var slot = new Vector3(part, index, which);
				for (int k = 0; k < 4; k++)
				{
					positions.Add(slot);
				}
				corners.Add(new Vector2(0, 0));
				corners.Add(new Vector2(1, 0));
				corners.Add(new Vector2(0, 1));
				corners.Add(new Vector2(1, 1));
				indices.Add(v);
				indices.Add(v + 2);
				indices.Add(v + 1);
				indices.Add(v + 1);
				indices.Add(v + 2);
				indices.Add(v + 3);
			}
			for (int s = 0; s < SceneSeaHallVents.MaxSmokers; s++)
			{
				for (int i = 0; i < SceneSeaHallVents.PlumePuffs; i++)
				{
					Quad(0, s, i);
				}
				for (int i = 0; i < SceneSeaHallVents.CeilingPuffs; i++)
				{
					Quad(1, s, i);
				}
				for (int i = 0; i < SceneSeaHallVents.Flanges; i++)
				{
					Quad(2, s, i);
				}
				for (int i = 0; i < SceneSeaHallVents.Beads; i++)
				{
					Quad(3, s, i);
				}
			}
			for (int g = 0; g < SceneSeaHallVents.GrovePerHall; g++)
			{
				for (int m = 0; m < SceneSeaHallVents.BriniclesPerGrove; m++)
				{
					Quad(4, g, 2 * m);
					Quad(4, g, 2 * m + 1);
					Quad(5, g, m);
				}
			}
			for (int b = 0; b < brine.Length; b++)
			{
				Quad(6, b, 0);
			}
			var built = new Mesh { name = "Sea Hall Vents", hideFlags = HideFlags.HideAndDontSave };
			built.SetVertices(positions);
			built.SetUVs(0, corners);
			built.SetTriangles(indices, 0, false);
			built.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			return built;
		}
	}
}
