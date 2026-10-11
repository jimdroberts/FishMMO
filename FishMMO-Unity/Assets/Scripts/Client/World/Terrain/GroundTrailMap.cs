using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Where feet have gone round the camera: a world-aligned map, 128 m at about 12 cm a texel, that every character in
	/// view treads stride prints into and pushes the grass aside in. r how hard the ground is trodden, g how flat the
	/// grass lies (FishWeather.hlsl FishTrailAt).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What reads it.</b> The ground's shaders (FishSurface.hlsl FishTrailSurface): snow, ash and sand take a packed
	/// furrow, wet ground darker, glossier prints that fill with water; on the tier that lifts the ground under snow the
	/// lift is trodden down too (FishSnowLiftMetres) and the prints are looked into. The grass blades lie over away from
	/// where a body is or has just been (FishGrassBlades.hlsl) and rise again over a few seconds. Bushes do not.
	/// </para>
	/// <para>
	/// <b>Nothing on the wire.</b> Each client treads the characters it can see: a trail is there from the moment its
	/// maker came into view, and goes with the ground it is on when that leaves the map.
	/// </para>
	/// <para>
	/// <b>Stride prints, not feet.</b> No rig is asked where its feet are. Each body lays alternate left and right prints a
	/// stride apart, the stride from its speed and size, and running or wading deep snow drags them out into a furrow; so
	/// a creature, a mount and the world test bed's capsule all leave a trail. Only on terrain: a body on a bridge or a rock
	/// leaves no print in the ground below it.
	/// </para>
	/// <para>
	/// <b>One map, never copied.</b> A texel holds the world texel it equals modulo the map's size, so moving the camera only
	/// clears the strip it moves into. A frame's work is that, a fade of the whole map (one full-screen pass with nothing
	/// read), and the stamps (one draw, up to <see cref="MaxStamps"/>).
	/// </para>
	/// </remarks>
	public static class GroundTrailMap
	{
		public const string ShaderName = "FishMMO/Ground Trail";
		private const string ShaderAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishGroundTrail.shader";

		/// <summary>The map's side, metres, and texels a side.</summary>
		public const float SizeMetres = 128f;
		public const int Resolution = 1024;
		public static float TexelMetres => SizeMetres / Resolution;

		/// <summary>The most stamps one frame lays; the rest wait for the next.</summary>
		public const int MaxStamps = 128;

		/// <summary>Seconds a print takes to fade on its own to about a third: churned ground settles, packed snow sags.</summary>
		private const float PrintSeconds = 900f;
		/// <summary>Seconds the heaviest snowfall takes to fill a print to about a third.</summary>
		private const float SnowFillSeconds = 90f;
		/// <summary>Seconds pushed-over grass takes to rise to about a third of the way down; it is all but up after five.</summary>
		private const float GrassRiseSeconds = 1.7f;

		/// <summary>A person's half-width, metres: what a body's prints and push are sized against.</summary>
		private const float PersonRadius = 0.35f;
		/// <summary>Faster than this, metres a second, a body's prints drag out into a furrow.</summary>
		private const float FurrowSpeed = 4.5f;
		/// <summary>Deeper snow than this past the blanket, metres, and every step is a furrow.</summary>
		private const float FurrowSnow = 0.3f;
		/// <summary>A jump further than this in a frame is a teleport, not a step: no print is laid along it.</summary>
		private const float TeleportMetres = 5f;
		/// <summary>How often each body asks whether it is standing on terrain, seconds.</summary>
		private const float GroundCheckSeconds = 0.2f;

		private static readonly int TrailTexId = Shader.PropertyToID("_FishTrailTex");
		private static readonly int TrailRectId = Shader.PropertyToID("_FishTrailRect");
		private static readonly int TrailParamsId = Shader.PropertyToID("_FishTrailParams");
		private static readonly int FadeId = Shader.PropertyToID("_TrailFade");
		private static readonly int RectsId = Shader.PropertyToID("_TrailRects");
		private static readonly int StampsId = Shader.PropertyToID("_TrailStamps");

		/// <summary>One body being followed: where it was, how far it has gone since its last print, which foot is next.</summary>
		private sealed class Walker
		{
			public Vector3 Last;
			public float Travelled;
			public bool Left;
			public bool OnTerrain;
			public float NextGroundCheck;
			public float Radius;
			public int Seen;
		}

		private static readonly Dictionary<long, Walker> walkers = new Dictionary<long, Walker>();
		private static readonly List<long> gone = new List<long>();
		/// <summary>Bodies that are not characters (the world test bed's walker), with their half-widths.</summary>
		private static readonly Dictionary<Transform, float> extraSources = new Dictionary<Transform, float>();
		private static readonly Vector4[] stamps = new Vector4[MaxStamps * 2];
		private static readonly Vector4[] rects = new Vector4[8];
		private static int stampCount;
		private static int rectCount;

		private static bool hooked;
		private static RenderTexture map;
		private static Material material;
		private static CommandBuffer commands;
		private static Vector2Int origin;
		private static bool valid;
		private static int lastFrame = -1;
		// The world clock at the last fade, seconds (NaN before one): prints settle and fill in world time.
		private static double fadedWorldSeconds = double.NaN;

		/// <summary>Whether the map is drawn and published this frame.</summary>
		public static bool IsValid => valid;

		/// <summary>Treads a body that is not a character — the world test bed's walker — as a character is trodden.</summary>
		public static void AddSource(Transform body, float radius = PersonRadius)
		{
			if (body != null)
			{
				extraSources[body] = Mathf.Max(0.05f, radius);
			}
		}

		public static void RemoveSource(Transform body)
		{
			if (body != null)
			{
				extraSources.Remove(body);
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			hooked = false;
			walkers.Clear();
			extraSources.Clear();
			valid = false;
			lastFrame = -1;
			fadedWorldSeconds = double.NaN;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			Application.quitting += Release;
#if UNITY_EDITOR
			// Scripts recompiled while playing reload in place: the map is let go first.
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Release;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
#endif
		}

		private static void Release()
		{
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			hooked = false;
			Disable();
			if (map != null)
			{
				map.Release();
				Object.Destroy(map);
				map = null;
			}
			if (material != null)
			{
				Object.Destroy(material);
				material = null;
			}
			commands?.Release();
			commands = null;
		}

		/// <summary>Nothing published: every reader sees untrodden ground.</summary>
		private static void Disable()
		{
			valid = false;
			Shader.SetGlobalVector(TrailRectId, Vector4.zero);
		}

		private static bool EnsureResources(WeatherRenderProfile profile)
		{
			if (material == null)
			{
				Shader shader = profile.GroundTrailShader;
#if UNITY_EDITOR
				if (shader == null)
				{
					shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
				}
#endif
				if (shader == null)
				{
					shader = Shader.Find(ShaderName);
				}
				if (shader == null || !shader.isSupported)
				{
					return false;
				}
				material = new Material(shader) { hideFlags = HideFlags.DontSave };
			}
			if (map == null)
			{
				// Two half floats a texel, 4 MB; four where the platform cannot render to two.
				GraphicsFormat format = SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, GraphicsFormatUsage.Render)
					&& SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, GraphicsFormatUsage.Blend)
					? GraphicsFormat.R16G16_SFloat
					: GraphicsFormat.R16G16B16A16_SFloat;
				map = new RenderTexture(Resolution, Resolution, GraphicsFormat.None, GraphicsFormat.None)
				{
					graphicsFormat = format,
					name = "Ground Trails",
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Repeat,
					useMipMap = false,
					hideFlags = HideFlags.DontSave,
				};
				map.Create();
				valid = false;
			}
			commands ??= new CommandBuffer { name = "Ground trails" };
			return true;
		}

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			// Once a frame, whatever renders: the trail is the world's, not a camera's.
			if (Time.frameCount == lastFrame)
			{
				return;
			}
			lastFrame = Time.frameCount;
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Camera camera = Camera.main;
			if (profile == null || !profile.GroundTrails || camera == null || !EnsureResources(profile))
			{
				Disable();
				return;
			}

			Vector3 eye = camera.transform.position;
			float texel = TexelMetres;
			var wanted = new Vector2Int(Mathf.FloorToInt(eye.x / texel) - Resolution / 2, Mathf.FloorToInt(eye.z / texel) - Resolution / 2);

			commands.Clear();
			commands.SetRenderTarget(map);
			// The map's own 0..1 square: the clear and stamp passes place themselves in it.
			commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
			if (!valid || !map.IsCreated())
			{
				if (!map.IsCreated())
				{
					map.Create();
				}
				commands.ClearRenderTarget(false, true, Color.clear);
			}
			else if (wanted != origin)
			{
				if (Mathf.Abs(wanted.x - origin.x) >= Resolution || Mathf.Abs(wanted.y - origin.y) >= Resolution)
				{
					commands.ClearRenderTarget(false, true, Color.clear);
				}
				else
				{
					rectCount = 0;
					EnteredStrips(origin.x, wanted.x, true);
					EnteredStrips(origin.y, wanted.y, false);
					if (rectCount > 0)
					{
						material.SetVectorArray(RectsId, rects);
						commands.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, rectCount * 6);
					}
				}
			}
			origin = wanted;

			// Prints settle and snow fills them in the world's own seconds: held, they stay as trodden; raced, they go
			// at the world's pace; a clock set forward finds them long gone, set back leaves them. The grass rises at the
			// world's motion, still when the world is held, like the wind that moves it.
			double worldNow = (WorldDayNightCycle.SkyClockHours ?? WorldTime.Hours) * 3600.0;
			float worldStep = double.IsNaN(fadedWorldSeconds) ? 0f : (float)System.Math.Max(0.0, System.Math.Min(worldNow - fadedWorldSeconds, 1e6));
			fadedWorldSeconds = worldNow;
			material.SetVector(FadeId, new Vector4(worldStep, 1f / PrintSeconds, 1f / SnowFillSeconds, WorldMotion.Scale(Time.deltaTime) / GrassRiseSeconds));
			commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);

			Tread(eye);
			if (stampCount > 0)
			{
				material.SetVectorArray(StampsId, stamps);
				commands.DrawProcedural(Matrix4x4.identity, material, 2, MeshTopology.Triangles, stampCount * 24);
			}
			Graphics.ExecuteCommandBuffer(commands);
			commands.Clear();

			valid = true;
			Shader.SetGlobalTexture(TrailTexId, map);
			Shader.SetGlobalVector(TrailRectId, new Vector4(origin.x * texel, origin.y * texel, SizeMetres, 1f));
			Shader.SetGlobalVector(TrailParamsId, new Vector4(texel, Resolution, 0f, 0f));
		}

		/// <summary>
		/// The world texels the window took in moving from <paramref name="from"/> to <paramref name="to"/> along one axis, as
		/// rectangles of the map to clear: where they are kept, which may run round the wrap.
		/// </summary>
		private static void EnteredStrips(int from, int to, bool alongX)
		{
			int moved = to - from;
			if (moved == 0)
			{
				return;
			}
			int start = moved > 0 ? from + Resolution : to;
			int count = Mathf.Abs(moved);
			int kept = ((start % Resolution) + Resolution) % Resolution;
			int first = Mathf.Min(count, Resolution - kept);
			AddRect(kept, first, alongX);
			if (count > first)
			{
				AddRect(0, count - first, alongX);
			}
		}

		private static void AddRect(int startTexel, int texels, bool alongX)
		{
			if (rectCount >= rects.Length)
			{
				return;
			}
			float a = startTexel / (float)Resolution;
			float b = (startTexel + texels) / (float)Resolution;
			rects[rectCount++] = alongX ? new Vector4(a, 0f, b, 1f) : new Vector4(0f, a, 1f, b);
		}

		// ── Who treads it ────────────────────────────────────────────────

		/// <summary>Every character in view and every extra body: this frame's prints and pushes.</summary>
		private static void Tread(Vector3 eye)
		{
			stampCount = 0;
			int frame = Time.frameCount;
			float reach = SizeMetres * 0.5f;
			foreach (KeyValuePair<long, ICharacter> pair in BaseCharacter.ClientCharacters)
			{
				Transform body = pair.Value?.Transform;
				if (body != null)
				{
					Follow(pair.Key, body, 0f, eye, reach, frame);
				}
			}
			foreach (KeyValuePair<Transform, float> pair in extraSources)
			{
				if (pair.Key != null)
				{
					// Not a character ID: negative, from the object's own.
					Follow(-1L - (long)(uint)pair.Key.GetInstanceID(), pair.Key, pair.Value, eye, reach, frame);
				}
			}
			gone.Clear();
			foreach (KeyValuePair<long, Walker> pair in walkers)
			{
				if (pair.Value.Seen != frame)
				{
					gone.Add(pair.Key);
				}
			}
			for (int i = 0; i < gone.Count; i++)
			{
				walkers.Remove(gone[i]);
			}
		}

		/// <summary>
		/// One body: a push through the grass where it stands, and a print each time it has gone a stride since the last.
		/// Its pivot is taken for its feet, as a character's is.
		/// </summary>
		private static void Follow(long key, Transform body, float radius, Vector3 eye, float reach, int frame)
		{
			Vector3 feet = body.position;
			if (!walkers.TryGetValue(key, out Walker walker))
			{
				walker = new Walker { Last = feet, Radius = radius > 0f ? radius : BodyRadius(body), Left = (key & 1L) == 0L };
				walkers.Add(key, walker);
			}
			walker.Seen = frame;
			Vector3 moved = feet - walker.Last;
			walker.Last = feet;
			var step = new Vector2(moved.x, moved.z);
			float distance = step.magnitude;
			if (distance > TeleportMetres || Mathf.Abs(feet.x - eye.x) > reach || Mathf.Abs(feet.z - eye.z) > reach)
			{
				walker.Travelled = 0f;
				return;
			}
			float now = Time.time;
			if (now >= walker.NextGroundCheck)
			{
				walker.NextGroundCheck = now + GroundCheckSeconds;
				walker.OnTerrain = OnTerrain(body, feet);
			}
			if (!walker.OnTerrain)
			{
				walker.Travelled = 0f;
				return;
			}

			float scale = walker.Radius / PersonRadius;
			var at = new Vector2(feet.x, feet.z);
			// The grass round it, pushed aside, whether it moves or not.
			float push = walker.Radius * 1.3f + 0.15f;
			AddStamp(at, Vector2.up, push, push, 0f, 1f);
			if (distance < 1e-4f)
			{
				return;
			}

			float speed = distance / Mathf.Max(1e-4f, Time.deltaTime);
			Vector2 heading = step / distance;
			var side = new Vector2(-heading.y, heading.x);
			float stride = Mathf.Lerp(0.65f, 1.5f, Mathf.Clamp01(speed / 8f)) * scale;
			WeatherPresentation presentation = WeatherPresentation.Instance;
			float deep = presentation != null && presentation.CoverMap != null ? presentation.CoverMap.SampleDeepSnow(feet) : 0f;
			bool furrow = speed > FurrowSpeed || deep > FurrowSnow;
			walker.Travelled += distance;
			while (walker.Travelled >= stride)
			{
				walker.Travelled -= stride;
				// Where the foot came down: back along the step by what has been walked since.
				Vector2 down = at - heading * walker.Travelled + side * ((walker.Left ? -1f : 1f) * (furrow ? 0.05f : 0.11f) * scale);
				walker.Left = !walker.Left;
				float length = furrow ? stride * 0.6f : 0.15f * scale;
				float width = furrow ? 0.12f * scale : 0.065f * scale;
				AddStamp(down, heading, length, width, 1f, 0f);
			}
		}

		/// <summary>Half the body's width: its capsule's radius, or a person's.</summary>
		private static float BodyRadius(Transform body)
		{
			CapsuleCollider capsule = body.GetComponentInChildren<CapsuleCollider>();
			if (capsule == null)
			{
				return PersonRadius;
			}
			Vector3 scale = capsule.transform.lossyScale;
			return Mathf.Clamp(capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)), 0.1f, 3f);
		}

		private static readonly RaycastHit[] hits = new RaycastHit[8];

		/// <summary>
		/// Whether the nearest thing under the feet that is not the body itself is the terrain, in the body's own scene's
		/// physics (world scenes keep their own).
		/// </summary>
		private static bool OnTerrain(Transform body, Vector3 feet)
		{
			PhysicsScene physics = body.gameObject.scene.GetPhysicsScene();
			int count = physics.Raycast(feet + Vector3.up * 0.4f, Vector3.down, hits, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			float nearest = float.MaxValue;
			bool terrain = false;
			for (int i = 0; i < count; i++)
			{
				RaycastHit hit = hits[i];
				if (hit.distance < nearest && !hit.transform.IsChildOf(body))
				{
					nearest = hit.distance;
					terrain = hit.collider is TerrainCollider;
				}
			}
			return terrain;
		}

		/// <summary>
		/// One stamp: its middle where it is kept (the world position modulo the map, in double so a far-off coordinate
		/// keeps its texel), which way it points, and its half length and width at least most of a texel, so it is never lost
		/// between texels' middles.
		/// </summary>
		private static void AddStamp(Vector2 world, Vector2 heading, float halfLength, float halfWidth, float press, float grass)
		{
			if (stampCount >= MaxStamps)
			{
				return;
			}
			double u = world.x / (double)SizeMetres;
			double v = world.y / (double)SizeMetres;
			float least = TexelMetres * 0.75f;
			int i = stampCount * 2;
			stamps[i] = new Vector4((float)(u - System.Math.Floor(u)), (float)(v - System.Math.Floor(v)), heading.x, heading.y);
			stamps[i + 1] = new Vector4(Mathf.Max(least, halfLength) / SizeMetres, Mathf.Max(least, halfWidth) / SizeMetres, press, grass);
			stampCount++;
		}
	}
}
