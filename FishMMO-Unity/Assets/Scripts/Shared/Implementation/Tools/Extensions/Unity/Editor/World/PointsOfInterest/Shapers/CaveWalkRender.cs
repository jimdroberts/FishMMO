#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Walks a camera through one generated cave system and renders it frame by frame (2026-10-13: "Render a 'walk' through a
	/// cave … Ensure we can see and use the 'torch' or a stronger light source"): in at the mouth, down the trunk, through the
	/// hubs to the chasm, over its bridge, and down its climb, carrying the player's light; then the same three spots lit by
	/// nothing but the cave, by the torch and by the lantern; and what it all costs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>-executeMethod FishMMO.Shared.WorldDesign.CaveWalkRender.Run</c> in an editor under xvfb (graphics needed). Nothing
	/// is saved but the frames and <c>index.txt</c>: a fresh untitled scene, a terrain in memory. Generates the structure
	/// kit's missing pieces first (the cave glow art and the climbing pieces live there), as the dashboard would.
	/// </para>
	/// <para>
	/// The cave is the first of its seeds with a chasm, a climb and a hall; its props stand as plain objects (not the
	/// instanced prop set), and its emitters light it through a pool of the nearest sixteen, as the client's SiteLightPool
	/// does. The cave's darkness is the scene's ambient turned all but off inside, as SkySystem turns it underground.
	/// </para>
	/// <para>
	/// Environment: <c>FISHMMO_CAVE_WALK_OUT</c> (default <c>WorldRenders/cavewalk</c> under <see cref="ProbeOutput.Root"/>);
	/// <c>FISHMMO_CAVE_WALK_CAVE</c> (<c>Form:size</c>, default <c>Cave:2</c>); <c>FISHMMO_CAVE_WALK_SEED</c> (a seed to
	/// use as it is); <c>FISHMMO_CAVE_WALK_FRAMES</c> (default 720).
	/// </para>
	/// </remarks>
	public static class CaveWalkRender
	{
		private const int Width = 1280, Height = 720;
		private const float TileMetres = 1000f, HeightRange = 320f;
		private const int Resolution = 513;
		private const float Eye = 1.65f;
		private const int PoolSize = 16;

		public static void Run()
		{
			int code = 1;
			bool async = ShaderUtil.allowAsyncCompilation;
			try
			{
				ShaderUtil.allowAsyncCompilation = false;
				code = Render() ? 0 : 1;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				ShaderUtil.allowAsyncCompilation = async;
			}
			EditorApplication.Exit(code);
		}

		private static string Env(string name, string fallback)
		{
			string v = Environment.GetEnvironmentVariable(name);
			return string.IsNullOrEmpty(v) ? fallback : v;
		}

		/// <summary>A foot at 60 m, a face rising 140 m over 60 m, a rolling upland behind: rock enough for an enormous hall.</summary>
		private static float Escarpment(float x, float z)
		{
			float t = Mathf.Clamp01(z / 60f);
			float face = 60f + 140f * (t * t * (3f - 2f * t));
			float roll = 4f * Mathf.Sin(x * 0.03f) + 3f * Mathf.Sin(z * 0.021f) * Mathf.Clamp01(z / 60f);
			return face + roll * Mathf.Clamp01((z + 10f) / 30f) - 0.04f * Mathf.Max(0f, -z);
		}

		private static bool Render()
		{
			string output = ProbeOutput.FromEnvironment("FISHMMO_CAVE_WALK_OUT", "WorldRenders", "cavewalk");
			string frames = Path.Combine(output, "frames");
			Directory.CreateDirectory(frames);
			foreach (string old in Directory.GetFiles(frames))
			{
				File.Delete(old);
			}
			var log = new List<string>();
			var total = System.Diagnostics.Stopwatch.StartNew();

			// ── The art the dressing places ──
			if (!CaveArt.TryGlow(CaveGlowKind.Crystal, 0, 0, out _) || CaveArt.Piece(CaveArt.Ladder, 0) == null)
			{
				var art = System.Diagnostics.Stopwatch.StartNew();
				BiomeArtGenerator.Report report = BiomeArtGenerator.GenerateStructuresOnly(ProceduralArtMode.MissingOnly, ProceduralArtCatalogue.DefaultSeed);
				log.Add($"structure kit generated (missing only) in {art.Elapsed.TotalSeconds:0} s: {report}");
			}

			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			Light sun = Sun();

			// ── The terrain, one tile in memory ──
			var data = new TerrainData { heightmapResolution = Resolution };
			data.size = new Vector3(TileMetres, HeightRange, TileMetres);
			var heights = new float[Resolution, Resolution];
			float step = TileMetres / (Resolution - 1);
			for (int k = 0; k < Resolution; k++)
			{
				for (int i = 0; i < Resolution; i++)
				{
					heights[k, i] = Mathf.Clamp01(Escarpment(-0.5f * TileMetres + i * step, -0.5f * TileMetres + k * step) / HeightRange);
				}
			}
			data.SetHeights(0, 0, heights);
			data.terrainLayers = new[] { new TerrainLayer { diffuseTexture = Flat(new Color(0.32f, 0.38f, 0.24f)), tileSize = new Vector2(4f, 4f) } };
			GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
			terrainObject.transform.position = new Vector3(-0.5f * TileMetres, 0f, -0.5f * TileMetres);
			Terrain terrain = terrainObject.GetComponent<Terrain>();
			Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
			if (terrainShader != null)
			{
				terrain.materialTemplate = new Material(terrainShader);
			}
			var terrains = new Terrain[1, 1];
			terrains[0, 0] = terrain;
			var tiles = new TerrainTilePlan { CountX = 1, CountZ = 1, TileMetres = TileMetres, Resolution = Resolution };
			Func<float, float, float> groundAt = (east, north) => terrain.SampleHeight(new Vector3(east, 0f, north)) + terrain.transform.position.y;
			CaveShaper.HoleGrid(terrains, tiles, out float holeStep, out float gridX, out float gridZ);

			// ── The cave: the first seed with a chasm, a climb and a hall ──
			string[] wanted = Env("FISHMMO_CAVE_WALK_CAVE", "Cave:2").Split(':');
			Enum.TryParse(wanted[0], true, out CaveForm form);
			int size = wanted.Length > 1 && int.TryParse(wanted[1], out int s) ? s : 2;
			CaveGround ground = CaveShaper.SampleGround(groundAt, tiles, 0f, -1f, 460f);
			CaveSystem system = null;
			int seed = 0;
			var planClock = new System.Diagnostics.Stopwatch();
			if (int.TryParse(Env("FISHMMO_CAVE_WALK_SEED", ""), out int forced))
			{
				seed = forced;
				planClock.Start();
				system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), 180f, seed, 1, ground, 0f, out _);
				planClock.Stop();
			}
			else
			{
				for (int tryIndex = 0; tryIndex < 80 && system == null; tryIndex++)
				{
					int candidate = 5000 + tryIndex * 7919;
					planClock.Restart();
					CaveSystem planned = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), 180f, candidate, 1, ground, 0f, out _);
					planClock.Stop();
					if (planned != null && planned.Rifts.Count > 0 && planned.Climbs.Count > 0
						&& planned.Chambers.Exists(c => Mathf.Max(c.Radii.x, c.Radii.z) >= 26f) && planned.Solids.Exists(o => o.Kind == CaveSolidKind.Bridge))
					{
						system = planned;
						seed = candidate;
					}
				}
			}
			if (system == null)
			{
				log.Add("no seed gave a cave with a chasm, a bridge, a climb and a hall");
				File.WriteAllLines(Path.Combine(output, "index.txt"), log);
				return false;
			}
			log.Add($"cave {form} size {size} seed {seed}: {system.Character}, {system.PassageLength:0} m of passages, {system.Depth:0} m deep, {system.Chambers.Count} chambers " +
				$"(largest {system.Chambers.Max(c => Mathf.Max(c.Radii.x, c.Radii.z)) * 2f:0} m across), {system.Rifts.Count} chasm(s), {system.Shafts.Count} shaft(s), " +
				$"{system.Solids.Count} solid(s) [{string.Join(", ", system.Solids.GroupBy(o => o.Kind).Select(g => $"{g.Count()} {g.Key}"))}], {system.Climbs.Count} climb(s), " +
				$"{system.Crossings.Count} built crossing(s), plan {planClock.Elapsed.TotalMilliseconds:0} ms");

			List<Vector2Int> cells = system.HoleCells(0, groundAt, holeStep, gridX, gridZ);
			CaveShaper.CutHoles(terrains, tiles, cells, holeStep, gridX, gridZ);
			terrain.Flush();
			Bounds e = system.Extent;
			CaveGround meshGround = CaveShaper.SampleGround(groundAt, tiles, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f, system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f);
			var meshClock = System.Diagnostics.Stopwatch.StartNew();
			List<CaveMesher.Piece> pieces = CaveMesher.Build(system, meshGround, out string meshReport);
			Mesh[] meshes = CaveShaper.WriteMeshes(null, 1, pieces, null);
			meshClock.Stop();
			Material rock = PointOfInterestRock.MaterialOf("Limestone") ?? new Material(Shader.Find("Universal Render Pipeline/Lit"));
			var caveRoot = new GameObject("Cave").transform;
			GameObject caveRock = CaveShaper.Build(caveRoot, system, pieces, meshes, rock);
			long meshBytes = meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
			long triangles = meshes.Sum(m => (long)m.triangles.Length / 3);
			log.Add($"mesh: {pieces.Count} pieces, {triangles:N0} triangles, {meshes.Sum(m => (long)m.vertexCount):N0} vertices, {meshBytes / 1048576.0:0.0} MB of mesh (render and collider share it), {meshClock.Elapsed.TotalSeconds:0.0} s; {meshReport}");

			// ── Its dressing, as a cut lays it ──
			var points = ScriptableObject.CreateInstance<ScenePointsOfInterest>();
			var record = new PointOfInterestRecord { Id = 1, Kind = form == CaveForm.Mine ? POIType.Mine : POIType.Cave, Position = new Vector3(0f, groundAt(0f, -1f), -1f), Yaw = 180f, SizeClass = (byte)size, SiteSeed = seed };
			points.Shapes.Add(CaveShaper.ShapeOf(record, system));
			var context = new PointOfInterestSiteContext
			{
				Record = record,
				Points = points,
				GroundAt = groundAt,
				Notes = new List<string>(),
				Random = new DeterministicRNG(seed),
				Sink = new PointOfInterestPropSink(),
			};
			var dressClock = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				new PointOfInterestCaveDressing().Build(context);
			}
			catch (Exception ex)
			{
				log.Add($"dressing threw: {ex.Message}");
				Debug.LogException(ex);
			}
			dressClock.Stop();
			var propRoot = new GameObject("Props").transform;
			var kinds = new Dictionary<string, int>();
			foreach (ScenePropSet.Prop prop in context.Sink.Props)
			{
				GameObject prefab = context.Sink.Prototypes[prop.Prototype].Prefab;
				var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, propRoot);
				go.transform.SetPositionAndRotation(prop.Position, prop.Rotation);
				go.transform.localScale = prop.Scale;
				string family = prefab.name.Split('_')[0] == "Cave" ? string.Join("_", prefab.name.Split('_').Take(2)) : prefab.name.Split('_').Length > 1 ? prefab.name.Split('_')[1] : prefab.name;
				kinds.TryGetValue(family, out int n);
				kinds[family] = n + 1;
			}
			log.Add($"dressing: {context.Sink.Props.Count} props of {context.Sink.Prototypes.Count} prefabs in {dressClock.Elapsed.TotalSeconds:0.0} s, {points.Emitters.Count} emitters, {points.Climbables.Count} climbables");
			log.Add("  " + string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
			foreach (string note in context.Notes)
			{
				log.Add("  note: " + note);
			}

			// ── Lights: the nearest sixteen emitters, as the client's pool lights them ──
			var pool = new Light[PoolSize];
			for (int i = 0; i < PoolSize; i++)
			{
				pool[i] = new GameObject("Pool " + i).AddComponent<Light>();
				pool[i].type = LightType.Point;
				pool[i].shadows = LightShadows.None;
				pool[i].enabled = false;
			}
			void LightNear(Vector3 eye)
			{
				List<SceneEmitter> near = points.Emitters.Where(x => x.Kind != SceneEmitterKind.Smoke && x.Kind != SceneEmitterKind.Hearth)
					.OrderBy(x => (x.Position - eye).sqrMagnitude).Take(PoolSize).ToList();
				for (int i = 0; i < PoolSize; i++)
				{
					if (i >= near.Count || (near[i].Position - eye).sqrMagnitude > 70f * 70f)
					{
						pool[i].enabled = false;
						continue;
					}
					SceneEmitter x = near[i];
					EmitterLight(x, out Color colour, out float range, out float intensity);
					pool[i].transform.position = x.Position;
					pool[i].color = colour;
					pool[i].range = range;
					pool[i].intensity = intensity;
					pool[i].enabled = true;
				}
			}

			// ── The camera and the player's light ──
			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.55f, 0.66f, 0.8f);
			camera.fieldOfView = 62f;
			camera.nearClipPlane = 0.05f;
			camera.farClipPlane = 1500f;
			camera.allowHDR = true;
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;
			Light carried = PlayerLight(camera.transform, out string lightSource);
			log.Add($"player light: {lightSource}");
			for (int warm = 0; warm < 3; warm++)
			{
				camera.Render();
			}

			// ── The walk ──
			List<(Vector3 Eye, Vector3 Look)> route = Route(system, groundAt, log);
			int wantFrames = int.TryParse(Env("FISHMMO_CAVE_WALK_FRAMES", "720"), out int f) ? f : 720;
			List<(Vector3 Eye, Vector3 Look)> walk = Resample(route, wantFrames);
			var renderTimes = new List<double>();
			var frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
			int written = 0;
			void Shot(string file, Vector3 eye, Vector3 look)
			{
				// Under the hill (the eye may graze the rock stepping over a terrace's edge; that is not the outside).
				bool inside = groundAt(eye.x, eye.z) > eye.y + 1.5f;
				// Underground, the sky's ambient at the tenth or less SkySystem leaves it.
				RenderSettings.ambientLight = inside ? new Color(0.028f, 0.03f, 0.034f) : new Color(0.35f, 0.38f, 0.42f);
				sun.enabled = !inside || (eye - system.Origin).magnitude < 25f;
				LightNear(eye);
				camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((look - eye).sqrMagnitude > 1e-4f ? look - eye : Vector3.forward, Vector3.up));
				var clock = System.Diagnostics.Stopwatch.StartNew();
				camera.Render();
				renderTimes.Add(clock.Elapsed.TotalMilliseconds);
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = target;
				frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
				frame.Apply();
				RenderTexture.active = previous;
				File.WriteAllBytes(file, frame.EncodeToJPG(88));
				written++;
			}
			SetMode(carried, 1);
			for (int i = 0; i < walk.Count; i++)
			{
				Shot(Path.Combine(frames, $"walk_{i:D5}.jpg"), walk[i].Eye, walk[i].Look);
			}

			// ── The player's light compared, at three spots along the walk ──
			string stills = Path.Combine(output, "stills");
			Directory.CreateDirectory(stills);
			int[] spots = { walk.Count / 4, walk.Count / 2, walk.Count * 4 / 5 };
			string[] modes = { "none", "torch", "lantern" };
			for (int k = 0; k < spots.Length; k++)
			{
				for (int m = 0; m < modes.Length; m++)
				{
					SetMode(carried, m);
					Shot(Path.Combine(stills, $"spot{k + 1}_{modes[m]}.jpg"), walk[spots[k]].Eye, walk[spots[k]].Look);
				}
			}
			SetMode(carried, 1);
			UnityEngine.Object.DestroyImmediate(frame);

			renderTimes.Sort();
			log.Add($"rendered {written} frames at {Width}x{Height}: render CPU median {renderTimes[renderTimes.Count / 2]:0.0} ms, 95th {renderTimes[renderTimes.Count * 95 / 100]:0.0} ms (xvfb, editor, no GPU timing)");
			log.Add($"memory: Unity allocated {Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:0} MB, reserved {Profiler.GetTotalReservedMemoryLong() / 1048576.0:0} MB, graphics {Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576.0:0} MB (whole editor)");
			log.Add($"total {total.Elapsed.TotalSeconds:0} s");
			File.WriteAllLines(Path.Combine(output, "index.txt"), log);
			return written > 0;
		}

		// ── The route ─────────────────────────────────────────────

		/// <summary>
		/// The way a player would walk: from out in front of the mouth, down the trunk, through the chambers and passages to
		/// the chamber with the chasm, over its bridge; then to the climb's head, down it, and a look along the chasm's floor.
		/// Eye height over the floor found under each point.
		/// </summary>
		private static List<(Vector3 Eye, Vector3 Look)> Route(CaveSystem system, Func<float, float, float> groundAt, List<string> log)
		{
			Vector3 o = system.Origin;
			var path = new List<Vector3>();
			CaveMouth mouth = system.Mouths[0];
			Vector3 outward = new Vector3(Mathf.Sin(mouth.Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(mouth.Yaw * Mathf.Deg2Rad));
			for (float d = 28f; d > 2f; d -= 2f)
			{
				Vector3 p = o + mouth.Floor + outward * d;
				p.y = groundAt(p.x, p.z);
				path.Add(p);
			}

			// The chamber the bridge stands in, and the passages from the mouth to it.
			CaveSolid bridge = system.Solids.First(x => x.Kind == CaveSolidKind.Bridge);
			Vector3 deck = (bridge.A + bridge.B) * 0.5f;
			int goal = Nearest(system, deck);
			List<(CavePassage Passage, bool Forward, int Into)> ways = Ways(system, goal);
			foreach ((CavePassage passage, bool forward, int into) in ways)
			{
				if (passage != null)
				{
					IEnumerable<CaveNode> nodes = forward ? passage.Nodes : Enumerable.Reverse(passage.Nodes);
					foreach (CaveNode n in nodes)
					{
						path.Add(o + new Vector3(n.Centre.x, n.Floor, n.Centre.z));
					}
				}
				// Across the chamber it reaches, by its middle (a lobe of a hall is walked into this way).
				CaveChamber c = system.Chambers[into];
				if (into != goal)
				{
					path.Add(o + new Vector3(c.Centre.x, c.Floor, c.Centre.z));
				}
			}
			log.Add($"route: {ways.Count} passage(s) from the mouth to the chasm's chamber");

			// Over the bridge, from the end nearer where we came in.
			Vector3 last = path[path.Count - 1] - o;
			bool fromA = (bridge.A - last).sqrMagnitude <= (bridge.B - last).sqrMagnitude;
			Vector3 near = fromA ? bridge.A : bridge.B, far = fromA ? bridge.B : bridge.A;
			Vector3 across = (far - near).normalized;
			path.Add(o + near - across * 3f);
			for (float t = 0f; t <= 1f; t += 0.1f)
			{
				path.Add(o + Vector3.Lerp(near, far, t));
			}
			path.Add(o + far + across * 2.5f);

			var route = new List<(Vector3, Vector3)>();
			for (int i = 0; i < path.Count; i++)
			{
				Vector3 p = path[i];
				float floor = system.FloorBelow(p - o + Vector3.up * 1.2f, 4f);
				Vector3 eye = new Vector3(p.x, (float.IsNaN(floor) ? p.y - o.y : floor) + o.y + Eye, p.z);
				Vector3 ahead = path[Mathf.Min(path.Count - 1, i + 3)];
				route.Add((eye, new Vector3(ahead.x, eye.y - 0.25f, ahead.z)));
			}

			// The climb: its head looking down, then down it facing the rock, then the chasm's floor.
			CaveClimb climb = system.Climbs.OrderBy(c => (c.Top - far).sqrMagnitude).First();
			Vector3 head = o + climb.Top + climb.TopExit * 1.6f + Vector3.up * Eye;
			route.Add((head, o + climb.Bottom));
			float height = climb.Top.y - climb.Bottom.y;
			for (float d = 0f; d <= height; d += 0.8f)
			{
				Vector3 on = o + Vector3.Lerp(climb.Top, climb.Bottom, d / height) + climb.Out * 0.7f + Vector3.up * 1.2f;
				route.Add((on, on - climb.Out + Vector3.down * (0.6f + 1.2f * d / height)));
			}
			Vector3 floorEye = o + climb.Bottom + climb.Out * 1.2f + Vector3.up * Eye;
			CaveRift rift = system.Rifts.OrderBy(r => DistanceToLine(climb.Bottom, r.A, r.B)).First();
			Vector3 along = (rift.B - rift.A).normalized;
			route.Add((floorEye, floorEye + Vector3.up * 14f + climb.Out * 3f));
			route.Add((floorEye, floorEye + along * 20f));
			route.Add((floorEye + along * 4f, floorEye + along * 30f));
			log.Add($"route: {route.Count} waypoints, bridge {Vector3.Distance(near, far):0.0} m, climb {height:0.0} m ({climb.Kind})");
			return route;
		}

		private static float DistanceToLine(Vector3 p, Vector3 a, Vector3 b)
		{
			Vector3 ab = b - a;
			float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
			return (p - (a + ab * t)).magnitude;
		}

		/// <summary>The chamber nearest a point (system frame), by plan distance to its middle over its reach.</summary>
		private static int Nearest(CaveSystem system, Vector3 p)
		{
			int best = 0;
			float score = float.MaxValue;
			for (int i = 0; i < system.Chambers.Count; i++)
			{
				CaveChamber c = system.Chambers[i];
				float d = new Vector2(p.x - c.Centre.x, p.z - c.Centre.z).magnitude / Mathf.Max(c.Radii.x, c.Radii.z) + Mathf.Abs(p.y - c.Floor) * 0.1f;
				if (d < score)
				{
					score = d;
					best = i;
				}
			}
			return best;
		}

		/// <summary>The passages from the mouth to a chamber: a breadth-first search over chambers joined by passages.</summary>
		private static List<(CavePassage, bool, int)> Ways(CaveSystem system, int goal)
		{
			int Holder(Vector3 p, float reach)
			{
				for (int i = 0; i < system.Chambers.Count; i++)
				{
					CaveChamber c = system.Chambers[i];
					if (new Vector2(p.x - c.Centre.x, p.z - c.Centre.z).magnitude < Mathf.Max(c.Radii.x, c.Radii.z) + reach && Mathf.Abs(p.y - c.Floor) < c.Radii.y)
					{
						return i;
					}
				}
				return -2;
			}
			// −1 is the mouth.
			var edges = new List<(int From, int To, CavePassage Passage)>();
			foreach (CavePassage p in system.Passages)
			{
				CaveNode a = p.Nodes[0], b = p.Nodes[p.Nodes.Count - 1];
				int from = p.Kind == CavePassageKind.Trunk ? -1 : Holder(new Vector3(a.Centre.x, a.Floor, a.Centre.z), a.Radius + 2f);
				int to = Holder(new Vector3(b.Centre.x, b.Floor, b.Centre.z), b.Radius + 2f);
				edges.Add((from, to, p));
			}
			// A hall's lobes are one room: they overlap, no passage joins them.
			for (int i = 0; i < system.Chambers.Count; i++)
			{
				for (int j = i + 1; j < system.Chambers.Count; j++)
				{
					CaveChamber a = system.Chambers[i], b = system.Chambers[j];
					float apart = new Vector2(a.Centre.x - b.Centre.x, a.Centre.z - b.Centre.z).magnitude;
					if (apart < 0.8f * (Mathf.Max(a.Radii.x, a.Radii.z) + Mathf.Max(b.Radii.x, b.Radii.z)) && Mathf.Abs(a.Floor - b.Floor) < 9f)
					{
						edges.Add((i, j, null));
					}
				}
			}
			var previous = new Dictionary<int, (int, CavePassage, bool)>();
			var queue = new Queue<int>();
			queue.Enqueue(-1);
			previous[-1] = (-1, null, true);
			while (queue.Count > 0)
			{
				int at = queue.Dequeue();
				if (at == goal)
				{
					break;
				}
				foreach ((int from, int to, CavePassage passage) in edges)
				{
					if (from == at && to != -2 && !previous.ContainsKey(to))
					{
						previous[to] = (at, passage, true);
						queue.Enqueue(to);
					}
					else if (to == at && from != -2 && !previous.ContainsKey(from))
					{
						previous[from] = (at, passage, false);
						queue.Enqueue(from);
					}
				}
			}
			var ways = new List<(CavePassage, bool, int)>();
			for (int at = goal; previous.ContainsKey(at) && at != -1; at = previous[at].Item1)
			{
				ways.Add((previous[at].Item2, previous[at].Item3, at));
			}
			ways.Reverse();
			return ways;
		}

		/// <summary>The route as <paramref name="count"/> evenly spaced frames, eye and gaze each eased along it.</summary>
		private static List<(Vector3 Eye, Vector3 Look)> Resample(List<(Vector3 Eye, Vector3 Look)> route, int count)
		{
			var lengths = new List<float> { 0f };
			for (int i = 1; i < route.Count; i++)
			{
				lengths.Add(lengths[i - 1] + Mathf.Max(0.3f, Vector3.Distance(route[i - 1].Eye, route[i].Eye)));
			}
			float total = lengths[lengths.Count - 1];
			var frames = new List<(Vector3, Vector3)>();
			int k = 1;
			Vector3 gaze = (route[0].Look - route[0].Eye).normalized;
			for (int f = 0; f < count; f++)
			{
				float s = total * f / Mathf.Max(1, count - 1);
				while (k < lengths.Count - 1 && lengths[k] < s)
				{
					k++;
				}
				float t = Mathf.InverseLerp(lengths[k - 1], lengths[k], s);
				Vector3 eye = Vector3.Lerp(route[k - 1].Eye, route[k].Eye, t);
				Vector3 want = Vector3.Lerp((route[k - 1].Look - route[k - 1].Eye).normalized, (route[k].Look - route[k].Eye).normalized, t).normalized;
				gaze = Vector3.Slerp(gaze, want, 0.18f).normalized;
				frames.Add((eye, eye + gaze * 5f));
			}
			return frames;
		}

		// ── Lighting ──────────────────────────────────────────────

		/// <summary>The light an emitter gives, as SiteLightPool lights it underground (no flicker in a still).</summary>
		private static void EmitterLight(SceneEmitter e, out Color colour, out float range, out float intensity)
		{
			switch (e.Kind)
			{
				case SceneEmitterKind.Crystal:
				{
					int hue = (int)((e.Seed >> 8) & 3u);
					colour = hue == 0 ? new Color(0.62f, 0.4f, 1f) : hue == 2 ? new Color(0.4f, 1f, 0.7f) : hue == 3 ? new Color(1f, 0.68f, 0.32f) : new Color(0.35f, 0.62f, 1f);
					range = e.Reach > 0f ? e.Reach : 6f;
					intensity = 1.4f;
					return;
				}
				case SceneEmitterKind.Glow:
					// Underground a glow is all lit (SceneGlow.Light), at the top of its pulse in a still.
					colour = SceneGlow.Colour(SceneGlow.HueOf(e.Seed));
					range = e.Reach > 0f ? e.Reach : SceneGlow.Range;
					intensity = SceneGlow.Intensity;
					return;
				case SceneEmitterKind.Torch:
					colour = new Color(1f, 0.66f, 0.36f);
					range = e.Reach > 0f ? e.Reach : 8f;
					intensity = 1.6f;
					return;
				case SceneEmitterKind.Fire:
					colour = new Color(1f, 0.56f, 0.24f);
					range = 11f;
					intensity = 3.2f;
					return;
				default:
					colour = new Color(1f, 0.72f, 0.42f);
					range = e.Reach > 0f ? e.Reach : 9f;
					intensity = 2.2f;
					return;
			}
		}

		private static Light carriedTorch;
		private static object[] carriedModes;
		private static MethodInfo carriedSet;

		/// <summary>
		/// The player's carried light on the camera: the game's own (a <c>CarriedLight</c> type, found by name so this editor
		/// assembly need not reference the client), else a stand-in matching the wall torches.
		/// </summary>
		private static Light PlayerLight(Transform holder, out string source)
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type type = assembly.GetTypes().FirstOrDefault(t => t.Name == "CarriedLight" && t.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static) != null);
				if (type == null)
				{
					continue;
				}
				MethodInfo attach = type.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static);
				Type modeType = attach.GetParameters().Length > 1 ? attach.GetParameters()[1].ParameterType : null;
				if (modeType == null || !modeType.IsEnum)
				{
					continue;
				}
				carriedModes = Enum.GetValues(modeType).Cast<object>().ToArray();
				carriedSet = attach;
				object made = attach.Invoke(null, new[] { holder, carriedModes[Mathf.Min(1, carriedModes.Length - 1)] });
				carriedTorch = made as Light ?? (made as Component)?.GetComponentInChildren<Light>();
				if (carriedTorch != null)
				{
					source = $"the game's {type.FullName} ({string.Join("/", carriedModes)})";
					return carriedTorch;
				}
			}
			carriedSet = null;
			var light = new GameObject("Carried light").AddComponent<Light>();
			light.type = LightType.Point;
			light.transform.SetParent(holder, false);
			light.transform.localPosition = new Vector3(0.25f, -0.25f, 0.3f);
			light.shadows = LightShadows.Soft;
			carriedTorch = light;
			source = "stand-in (no CarriedLight type found)";
			return light;
		}

		/// <summary>0 none, 1 torch, 2 lantern.</summary>
		private static void SetMode(Light light, int mode)
		{
			if (carriedSet != null && carriedModes != null && carriedModes.Length >= 3)
			{
				Transform holder = light.transform.parent != null ? light.transform.parent : light.transform;
				object made = carriedSet.Invoke(null, new[] { holder, carriedModes[Mathf.Clamp(mode, 0, carriedModes.Length - 1)] });
				Light now = made as Light ?? (made as Component)?.GetComponentInChildren<Light>();
				if (now != null && now != light)
				{
					light.enabled = false;
				}
				if (now != null)
				{
					now.enabled = mode != 0;
				}
				return;
			}
			light.enabled = mode != 0;
			light.color = mode == 2 ? new Color(1f, 0.86f, 0.68f) : new Color(1f, 0.66f, 0.36f);
			light.range = mode == 2 ? 22f : 12f;
			light.intensity = mode == 2 ? 3.2f : 1.9f;
		}

		private static Light Sun()
		{
			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			// From the south, so the escarpment's face (it looks south, down −z) stands in the sun.
			sun.transform.rotation = Quaternion.Euler(38f, 330f, 0f);
			sun.intensity = 1.3f;
			sun.shadows = LightShadows.Soft;
			RenderSettings.ambientMode = AmbientMode.Flat;
			RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.42f);
			RenderSettings.reflectionIntensity = 0f;
			QualitySettings.shadowDistance = 220f;
			return sun;
		}

		private static Texture2D Flat(Color c)
		{
			var t = new Texture2D(4, 4);
			var pixels = new Color[16];
			for (int i = 0; i < 16; i++)
			{
				pixels[i] = c;
			}
			t.SetPixels(pixels);
			t.Apply();
			return t;
		}
	}
}
#endif
