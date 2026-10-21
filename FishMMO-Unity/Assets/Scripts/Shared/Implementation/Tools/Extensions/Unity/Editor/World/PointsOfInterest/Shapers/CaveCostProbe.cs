#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What a generated scene's caves cost (2026-10-20, measured before any cave optimisation is built): their rock meshes'
	/// size and memory, what their colliders cost to cook and hold, what physics queries cost against them as they are and
	/// against thinner stand-ins, and, from the middle of every chamber, how much of the rock a camera would draw.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Physics.</b> The pieces' meshes are copied into an empty scene in several forms, each on its own layer: as stored
	/// (the render mesh, which the collider shared until it had its own), welded with positions only (same triangles: the collider
	/// the cut now writes, CaveMesher.ColliderOf), and clustered onto 0.75 m and
	/// 1.5 m grids (stand-ins for a thinner collider, to see how cooking, memory and query time scale with triangles). Each
	/// form's colliders are created (and so cooked) under a clock and a native-memory reading, then the same rays, capsule
	/// casts and overlaps are run against each.
	/// </para>
	/// <para>
	/// <b>Drawing.</b> From every chamber's middle (and every fourth passage node) a 60° camera looks along 8 bearings. Per
	/// view: the pieces in the frustum (what the cave draws now, all of it while the camera is in the cave); those that pass a
	/// depth-pyramid occlusion test built from a 128×72 grid of rays (what the GPU Resident Drawer's occlusion culling leaves);
	/// those a ray of that grid actually reaches (what a perfect per-chamber scheme would leave); and, among those, the
	/// triangles further than 60 m or under one pixel at 2560×1440 (what a distant level of detail could thin).
	/// </para>
	/// <para>
	/// <c>-executeMethod FishMMO.Shared.WorldDesign.CaveCostProbe.Run</c>. Environment: <c>FISHMMO_CAVE_COST_SCENE</c> (the
	/// scene's asset path, default Moron 2), <c>FISHMMO_CAVE_COST_OUT</c> (the report's folder). Saves nothing.
	/// </para>
	/// </remarks>
	public static class CaveCostProbe
	{
		private const int Width = 2560, Height = 1440, GridX = 128, GridY = 72;
		private const float Fov = 60f, Near = 0.3f, Far = 1000f, FarLod = 60f;

		public static void Run()
		{
			try
			{
				Probe();
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(0);
			}
		}

		/// <summary>
		/// Whether a cave piece's render mesh can drop its CPU copy (<c>-executeMethod …CaveCostProbe.Readability</c>, graphics
		/// needed): a copy of one stored piece made unreadable as a player holds it, then its GPU vertex buffer re-targeted raw
		/// as the cave cover does (CaveCoverSystem.TakeOn) and read back; and what an unreadable mesh still allows in the editor.
		/// Environment: <c>FISHMMO_CAVE_COST_ASSET</c> (a caves asset, default Moron 3's), <c>FISHMMO_CAVE_COST_OUT</c>.
		/// </summary>
		public static void Readability()
		{
			var log = new List<string>();
			string output = ProbeOutput.FromEnvironment("FISHMMO_CAVE_COST_OUT", "WorldRenders", "cavecost");
			try
			{
				string asset = Environment.GetEnvironmentVariable("FISHMMO_CAVE_COST_ASSET");
				if (string.IsNullOrEmpty(asset))
				{
					asset = "Assets/Scenes/WorldScene/Moron/Moron 3 Terrain/Moron 3 Caves.asset";
				}
				Mesh source = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<Mesh>().OrderByDescending(m => m.vertexCount).FirstOrDefault();
				log.Add($"source {source?.name}: {source?.vertexCount} verts, readable {source?.isReadable}, graphics {SystemInfo.graphicsDeviceType}");
				Mesh copy = UnityEngine.Object.Instantiate(source);
				Vector3[] before = copy.vertices;
				int stream = copy.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position);
				int offset = copy.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position);
				int stride = copy.GetVertexBufferStride(stream);
				copy.UploadMeshData(true);
				log.Add($"after UploadMeshData(true): readable {copy.isReadable}, vertexCount {copy.vertexCount}");
				copy.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
				copy.indexBufferTarget |= GraphicsBuffer.Target.Raw;
				log.Add($"raw targets taken: vertex {(copy.vertexBufferTarget & GraphicsBuffer.Target.Raw) != 0}, index {(copy.indexBufferTarget & GraphicsBuffer.Target.Raw) != 0}");
				using (GraphicsBuffer buffer = copy.GetVertexBuffer(stream))
				{
					var bytes = new byte[buffer.count * buffer.stride];
					buffer.GetData(bytes);
					int same = 0;
					for (int i = 0; i < before.Length; i++)
					{
						int at = i * stride + offset;
						var p = new Vector3(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4), BitConverter.ToSingle(bytes, at + 8));
						if (p == before[i])
						{
							same++;
						}
					}
					log.Add($"raw vertex buffer read back: {same}/{before.Length} positions intact (buffer {buffer.count}×{buffer.stride})");
				}

				// An unreadable mesh in the editor, as the cut would store it (the serialized flag, its data kept).
				Mesh stored = UnityEngine.Object.Instantiate(source);
				var so = new SerializedObject(stored);
				SerializedProperty readable = so.FindProperty("m_IsReadable");
				readable.boolValue = false;
				so.ApplyModifiedPropertiesWithoutUndo();
				int read = -1;
				try
				{
					read = stored.vertices.Length;
				}
				catch (Exception e)
				{
					log.Add("reading an unreadable mesh in the editor threw: " + e.Message);
				}
				log.Add($"serialized unreadable: isReadable {stored.isReadable}, vertices read in the editor {read}");
				SerializedProperty it = new SerializedObject(source).GetIterator();
				var names = new List<string>();
				while (it.Next(true))
				{
					if (it.name.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0 || it.name.IndexOf("Usage", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						names.Add(it.name);
					}
				}
				log.Add("serialized buffer target fields: " + (names.Count > 0 ? string.Join(", ", names) : "none"));
			}
			catch (Exception e)
			{
				log.Add("failed: " + e);
			}
			Directory.CreateDirectory(output);
			File.WriteAllLines(Path.Combine(output, "readability.txt"), log);
			foreach (string line in log)
			{
				Debug.Log("[cavecost] " + line);
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(0);
			}
		}

		/// <summary>
		/// The cut's collider meshes through a real asset (<c>-executeMethod …CaveCostProbe.Colliders</c>): one small cave meshed
		/// and written to a scratch caves asset under Assets (deleted at the end) as the cut writes it, read back after a save;
		/// kept as the next cut keeps it; an asset from before collider meshes (pieces readable, no <c>C</c> meshes) upgraded on
		/// keeping; its rock built with each piece's collider on its own mesh; and the colliders cast against along every passage.
		/// </summary>
		public static void Colliders()
		{
			var log = new List<string>();
			string output = ProbeOutput.FromEnvironment("FISHMMO_CAVE_COST_OUT", "WorldRenders", "cavecost");
			const string asset = "Assets/CaveColliderProbe Caves.asset";
			const int site = 7;
			bool ok = true;
			void Check(bool condition, string what)
			{
				ok &= condition;
				log.Add((condition ? "ok   " : "FAIL ") + what);
			}
			try
			{
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				Func<float, float, float> g = (x, z) =>
				{
					float t = Mathf.Clamp01(z / 30f);
					return 70f + 60f * (t * t * (3f - 2f * t)) + 2f * Mathf.Sin(x * 0.05f);
				};
				CaveSystem system = CaveSystem.Plan(CaveForm.Cave, 0, new Vector3(0f, 0f, -1f), 180f, 7919, site,
					CaveGround.Sample(g, -560f, -560f, 560f, 560f, 2f), 0f, out string problem);
				Check(system != null, "a cave planned " + problem);
				const float step = 1200f / 512f, grid = -2400f;
				system.HoleCells(0, g, step, grid, grid);
				Bounds e = system.Extent;
				CaveGround ground = CaveGround.Sample(g, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f, system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f, step, grid, grid);
				List<CaveMesher.Piece> pieces = CaveMesher.Build(system, ground, out string report);
				log.Add(report);
				var live = new[] { site };

				Mesh[] meshes = CaveShaper.WriteMeshes(asset, site, pieces, live, out Mesh[] colliders, "probe");
				CaveShaper.SaveMeshes();
				Mesh[] all = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<Mesh>().ToArray();
				int p = all.Count(m => m.name.StartsWith($"Cave {site} P")), c = all.Count(m => m.name.StartsWith($"Cave {site} C"));
				Check(p == pieces.Count && c == pieces.Count, $"written: {p} render and {c} collider meshes for {pieces.Count} pieces");
				Check(all.Where(m => m.name.Contains(" P")).All(m => !m.isReadable), "the render pieces are stored unreadable");
				Check(all.Where(m => m.name.Contains(" C")).All(m => m.isReadable && m.vertexBufferCount == 1 && m.GetVertexBufferStride(0) == 12), "the colliders are readable, positions only (12 B)");
				long renderTris = meshes.Sum(m => (long)m.triangles.Length), colliderTris = colliders.Sum(m => (long)m.triangles.Length);
				Check(renderTris >= colliderTris && colliderTris > 0.99 * renderTris, $"the colliders carry the pieces' triangles ({colliderTris / 3:N0} of {renderTris / 3:N0})");
				log.Add($"memory: render {meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m)) / 1024} KB, colliders {colliders.Sum(m => Profiler.GetRuntimeMemorySizeLong(m)) / 1024} KB");

				// Kept as the next cut keeps it.
				var keep = typeof(CaveShaper).GetMethod("TryKeepMeshes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
				object[] args = { asset, site, "probe", live, null, null, null, 0 };
				Check((bool)keep.Invoke(null, args), "the cave's meshes are kept on an unchanged key");
				Check(args[5] is Mesh[] kept && kept.Length == pieces.Count && kept.All(m => m != null && m.name.Contains(" C")), "with its collider meshes");

				// An asset from before: no collider meshes, readable pieces.
				foreach (Mesh m in AssetDatabase.LoadAllAssetsAtPath(asset).OfType<Mesh>().Where(m => m.name.Contains(" C")).ToArray())
				{
					AssetDatabase.RemoveObjectFromAsset(m);
					UnityEngine.Object.DestroyImmediate(m, true);
				}
				foreach (Mesh m in AssetDatabase.LoadAllAssetsAtPath(asset).OfType<Mesh>())
				{
					var so = new SerializedObject(m);
					so.FindProperty("m_IsReadable").boolValue = true;
					so.ApplyModifiedPropertiesWithoutUndo();
					EditorUtility.SetDirty(m);
				}
				AssetDatabase.SaveAssets();
				args = new object[] { asset, site, "probe", live, null, null, null, 0 };
				Check((bool)keep.Invoke(null, args), "an asset from before collider meshes keeps its pieces");
				Check(args[5] == null, "and says it has no collider meshes");
				var upgrade = typeof(CaveShaper).GetMethod("WriteColliders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
				colliders = (Mesh[])upgrade.Invoke(null, new object[] { asset, site, (Mesh[])args[4] });
				CaveShaper.SaveMeshes();
				all = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<Mesh>().ToArray();
				Check(all.Count(m => m.name.Contains(" C")) == pieces.Count && all.Where(m => m.name.Contains(" P")).All(m => !m.isReadable),
					"upgraded: collider meshes made from the kept pieces, the pieces now unreadable");
				args = new object[] { asset, site, "probe", live, null, null, null, 0 };
				Check((bool)keep.Invoke(null, args) && args[5] is Mesh[], "and kept with them next time");
				meshes = (Mesh[])args[4];
				colliders = (Mesh[])args[5];

				// Built as the cut builds it, and walked.
				var parent = new GameObject("Site").transform;
				GameObject rock = CaveShaper.Build(parent, system, pieces.Select(x => x.Mouth).ToArray(), meshes, null, colliders);
				MeshCollider[] solid = rock.GetComponentsInChildren<MeshCollider>(true);
				MeshFilter[] drawn = rock.GetComponentsInChildren<MeshFilter>(true);
				Check(solid.All(x => x.sharedMesh != null && x.sharedMesh.name.Contains(" C")) && drawn.All(x => x.sharedMesh.name.Contains(" P")),
					$"{solid.Length} colliders on collider meshes, {drawn.Length} renderers on the pieces");
				Physics.SyncTransforms();
				int samples = 0, floored = 0, headroom = 0;
				int mask = 1 << rock.layer;
				foreach (CavePassage passage in system.Passages)
				{
					foreach (CaveNode n in passage.Nodes)
					{
						Vector3 from = system.Origin + new Vector3(n.Centre.x, n.Floor + 1.2f, n.Centre.z);
						samples++;
						if (Physics.Raycast(from, Vector3.down, out RaycastHit floor, 4f, mask, QueryTriggerInteraction.Ignore))
						{
							floored++;
							if (!Physics.CheckCapsule(floor.point + Vector3.up * 0.5f, floor.point + Vector3.up * 1.7f, 0.3f, mask, QueryTriggerInteraction.Ignore))
							{
								headroom++;
							}
						}
					}
				}
				Check(floored > samples * 9 / 10, $"PhysX finds the collider's floor under {floored} of {samples} passage nodes");
				Check(headroom > floored * 9 / 10, $"and a standing capsule fits over {headroom} of them");
			}
			catch (Exception ex)
			{
				ok = false;
				log.Add("failed: " + ex);
			}
			finally
			{
				AssetDatabase.DeleteAsset(asset);
			}
			log.Add(ok ? "ALL OK" : "SOME FAILED");
			Directory.CreateDirectory(output);
			File.WriteAllLines(Path.Combine(output, "colliders.txt"), log);
			foreach (string line in log)
			{
				Debug.Log("[cavecost] " + line);
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(0);
			}
		}

		private sealed class CaveData
		{
			public int SiteId;
			public CaveSystem System;
			public Mesh[] Meshes;
			public bool[] Mouth;
		}

		private sealed class Form
		{
			public string Name;
			public int Layer;
			public long Triangles, Vertices, MeshBytes, CookBytes;
			public double CookMs;
			public readonly List<Mesh> Meshes = new List<Mesh>();
		}

		private static void Probe()
		{
			string path = Environment.GetEnvironmentVariable("FISHMMO_CAVE_COST_SCENE");
			if (string.IsNullOrEmpty(path))
			{
				path = "Assets/Scenes/WorldScene/Moron/Moron 2.unity";
			}
			string output = ProbeOutput.FromEnvironment("FISHMMO_CAVE_COST_OUT", "WorldRenders", "cavecost");
			Directory.CreateDirectory(output);
			var log = new List<string>();
			void Say(string line)
			{
				log.Add(line);
				Debug.Log("[cavecost] " + line);
			}
			Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
			ScenePointsOfInterest points = PointOfInterestProbe.Asset(scene);
			if (points == null)
			{
				Say($"{path}: no points of interest");
				File.WriteAllLines(Path.Combine(output, "report.txt"), log);
				return;
			}
			var rocks = new List<Transform>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
				{
					if (t.name == "Cave Rock")
					{
						rocks.Add(t);
					}
				}
			}
			var caves = new List<CaveData>();
			foreach (PointOfInterestShape shape in points.Shapes)
			{
				if (shape.Values == null || shape.Values.Length == 0 || shape.Shaper != CaveShaper.ShaperName || CaveShaper.IsLink(shape.Values, out _))
				{
					continue;
				}
				CaveSystem system = CaveSystem.FromValues(shape.Values, shape.Position, shape.Seed);
				Transform rock = system != null ? rocks.OrderBy(r => (r.position - system.Origin).sqrMagnitude).FirstOrDefault() : null;
				if (rock == null || (rock.position - system.Origin).sqrMagnitude > 1f)
				{
					continue;
				}
				Transform visual = rock.Find(CliffPlacer.VisualName);
				CaveVisibility vis = visual != null ? visual.GetComponent<CaveVisibility>() : null;
				// The render pieces (the colliders shared them until 2026-10-20; since, they have positions-only meshes of their own).
				MeshFilter[] filters = (visual != null ? visual : rock).GetComponentsInChildren<MeshFilter>(true);
				var data = new CaveData { SiteId = shape.SiteId, System = system, Meshes = filters.Select(f => f.sharedMesh).ToArray(), Mouth = new bool[filters.Length] };
				for (int i = 0; i < filters.Length && vis != null; i++)
				{
					data.Mouth[i] = vis.AtMouth(i);
				}
				caves.Add(data);
			}
			Say($"{path}: {caves.Count} caves");

			// ── Meshes as stored ──
			long allTris = 0, allVerts = 0, allBytes = 0;
			foreach (CaveData cave in caves)
			{
				long tris = 0, verts = 0, bytes = 0, max = 0;
				int stride = 0;
				bool readable = true;
				foreach (Mesh m in cave.Meshes)
				{
					long t = m.triangles.Length / 3;
					tris += t;
					max = Math.Max(max, t);
					verts += m.vertexCount;
					bytes += Profiler.GetRuntimeMemorySizeLong(m);
					stride = 0;
					for (int s = 0; s < m.vertexBufferCount; s++)
					{
						stride += m.GetVertexBufferStride(s);
					}
					readable &= m.isReadable;
				}
				allTris += tris;
				allVerts += verts;
				allBytes += bytes;
				CaveSystem sys = cave.System;
				float hall = sys.Chambers.Count > 0 ? sys.Chambers.Max(c => 2f * Mathf.Max(c.Radii.x, c.Radii.z)) : 0f;
				Say($"site {cave.SiteId} {sys.Form} size {sys.SizeClass}: {cave.Meshes.Length} pieces ({cave.Mouth.Count(x => x)} at a mouth), {tris:N0} tris (largest piece {max:N0}), {verts:N0} verts, " +
					$"stride {stride} B, readable {readable}, mesh memory {bytes / 1048576.0:0.0} MB; {sys.Chambers.Count} chambers (widest {hall:0} m), {sys.PassageLength:0} m of passages");
			}
			Say($"ALL: {allTris:N0} tris, {allVerts:N0} verts, {allBytes / 1048576.0:0.0} MB of mesh");

			// Everything after this works in an empty scene, where the copies' colliders are the only ones.
			var held = caves.SelectMany(c => c.Meshes).ToList();
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			if (held.Any(m => m == null))
			{
				Say("meshes unloaded with the scene; stopping");
				File.WriteAllLines(Path.Combine(output, "report.txt"), log);
				return;
			}

			// ── Colliders: four forms, each cooked under a clock ──
			var forms = new List<Form>
			{
				new Form { Name = "as stored", Layer = 28 },
				new Form { Name = "welded, positions only", Layer = 29 },
				new Form { Name = "clustered 0.75 m", Layer = 30 },
				new Form { Name = "clustered 1.5 m", Layer = 31 },
			};
			var holders = new Dictionary<Form, List<GameObject>>();
			foreach (Form form in forms)
			{
				var objects = new List<GameObject>();
				foreach (CaveData cave in caves)
				{
					foreach (Mesh m in cave.Meshes)
					{
						Mesh copy = form.Layer == 28 ? UnityEngine.Object.Instantiate(m)
							: form.Layer == 29 ? Welded(m, 0f)
							: Welded(m, form.Layer == 30 ? 0.75f : 1.5f);
						form.Meshes.Add(copy);
						form.Triangles += copy.triangles.Length / 3;
						form.Vertices += copy.vertexCount;
						form.MeshBytes += Profiler.GetRuntimeMemorySizeLong(copy);
						var go = new GameObject("piece") { layer = form.Layer };
						go.transform.position = cave.System.Origin;
						objects.Add(go);
					}
				}
				GC.Collect();
				long before = Profiler.GetTotalAllocatedMemoryLong();
				var clock = Stopwatch.StartNew();
				for (int i = 0; i < objects.Count; i++)
				{
					var collider = objects[i].AddComponent<MeshCollider>();
					collider.sharedMesh = form.Meshes[i];
				}
				Physics.SyncTransforms();
				form.CookMs = clock.Elapsed.TotalMilliseconds;
				form.CookBytes = Profiler.GetTotalAllocatedMemoryLong() - before;
				holders[form] = objects;
				Say($"collider {form.Name}: {form.Triangles:N0} tris, {form.Vertices:N0} verts, mesh {form.MeshBytes / 1048576.0:0.0} MB, " +
					$"cooked in {form.CookMs:0} ms, cooked memory ≈ {form.CookBytes / 1048576.0:0.0} MB (native allocation delta)");
			}

			// ── Queries: the same work against each form ──
			var origins = new List<Vector3>();
			foreach (CaveData cave in caves)
			{
				foreach (CavePassage passage in cave.System.Passages)
				{
					foreach (CaveNode n in passage.Nodes)
					{
						origins.Add(cave.System.Origin + new Vector3(n.Centre.x, n.Floor + Mathf.Min(1.6f, 0.6f * n.Radius), n.Centre.z));
					}
				}
				foreach (CaveChamber c in cave.System.Chambers)
				{
					origins.Add(cave.System.Origin + new Vector3(c.Centre.x, c.Floor + 1.6f, c.Centre.z));
				}
			}
			var dirs = new List<Vector3>();
			for (int i = 0; i < 64; i++)
			{
				float y = 1f - 2f * (i + 0.5f) / 64, r = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
				dirs.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
			}
			var reference = new Dictionary<int, float>();
			var hits = new RaycastHit[16];
			var overlaps = new Collider[32];
			foreach (Form form in forms)
			{
				int mask = 1 << form.Layer;
				// Warm.
				foreach (Vector3 o in origins.Take(50))
				{
					Physics.Raycast(o, Vector3.down, 10f, mask);
				}
				var clock = Stopwatch.StartNew();
				int rays = 0, differ = 0, k = 0;
				foreach (Vector3 o in origins)
				{
					foreach (Vector3 d in dirs)
					{
						float dist = Physics.Raycast(o, d, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Ignore) ? hit.distance : -1f;
						rays++;
						if (form.Layer == 28)
						{
							reference[k] = dist;
						}
						else if (Mathf.Abs(reference[k] - dist) > 0.3f)
						{
							differ++;
						}
						k++;
					}
				}
				double rayUs = clock.Elapsed.TotalMilliseconds * 1000.0 / rays;
				clock.Restart();
				int casts = 0;
				for (int rep = 0; rep < 4; rep++)
				{
					foreach (Vector3 o in origins)
					{
						Vector3 foot = o - Vector3.up * 1.2f;
						for (int b = 0; b < 8; b++)
						{
							Vector3 d = Quaternion.Euler(0f, b * 45f, 0f) * Vector3.forward;
							Physics.CapsuleCastNonAlloc(foot + Vector3.up * 0.4f, foot + Vector3.up * 1.4f, 0.4f, d, hits, 0.5f, mask, QueryTriggerInteraction.Ignore);
							casts++;
						}
					}
				}
				double castUs = clock.Elapsed.TotalMilliseconds * 1000.0 / casts;
				clock.Restart();
				int over = 0;
				for (int rep = 0; rep < 8; rep++)
				{
					foreach (Vector3 o in origins)
					{
						Vector3 foot = o - Vector3.up * 1.2f;
						Physics.OverlapCapsuleNonAlloc(foot + Vector3.up * 0.35f, foot + Vector3.up * 1.45f, 0.42f, overlaps, mask, QueryTriggerInteraction.Ignore);
						over++;
					}
				}
				double overUs = clock.Elapsed.TotalMilliseconds * 1000.0 / over;
				Say($"queries {form.Name}: ray {rayUs:0.00} µs ({rays:N0}, {differ:N0} differ by > 0.3 m from as stored), capsule cast 0.5 m {castUs:0.00} µs ({casts:N0}), overlap capsule {overUs:0.00} µs ({over:N0})");
			}

			// ── Drawing, from the chambers and passages, against the stored form ──
			Matrix4x4 projection = Matrix4x4.Perspective(Fov, (float)Width / Height, Near, Far);
			var planes = new Plane[6];
			float tanHalf = Mathf.Tan(0.5f * Fov * Mathf.Deg2Rad), aspect = (float)Width / Height;
			var depth = new float[GridX * GridY];
			int storedLayer = 1 << 28;
			List<GameObject> stored = holders[forms[0]];
			var pieceOf = new Dictionary<Collider, (int Cave, int Piece)>();
			{
				int at = 0;
				for (int c = 0; c < caves.Count; c++)
				{
					for (int p = 0; p < caves[c].Meshes.Length; p++)
					{
						pieceOf[stored[at++].GetComponent<MeshCollider>()] = (c, p);
					}
				}
			}
			for (int c = 0; c < caves.Count; c++)
			{
				CaveData cave = caves[c];
				CaveSystem sys = cave.System;
				var boxes = new Bounds[cave.Meshes.Length];
				var tris = new int[cave.Meshes.Length];
				var verts = new Vector3[cave.Meshes.Length][];
				var index = new int[cave.Meshes.Length][];
				long caveTris = 0;
				for (int p = 0; p < boxes.Length; p++)
				{
					Bounds b = cave.Meshes[p].bounds;
					b.center += sys.Origin;
					boxes[p] = b;
					index[p] = cave.Meshes[p].triangles;
					verts[p] = cave.Meshes[p].vertices;
					tris[p] = index[p].Length / 3;
					caveTris += tris[p];
				}
				var eyes = new List<(Vector3 At, string What)>();
				foreach (CaveChamber ch in sys.Chambers)
				{
					eyes.Add((sys.Origin + new Vector3(ch.Centre.x, ch.Floor + 1.7f, ch.Centre.z), $"chamber {ch.Kind} {2f * Mathf.Max(ch.Radii.x, ch.Radii.z):0} m"));
				}
				foreach (CavePassage passage in sys.Passages)
				{
					for (int n = 2; n < passage.Nodes.Count; n += 4)
					{
						CaveNode node = passage.Nodes[n];
						eyes.Add((sys.Origin + new Vector3(node.Centre.x, node.Floor + Mathf.Min(1.7f, 0.6f * node.Radius), node.Centre.z), "passage " + passage.Kind));
					}
				}
				double sumFrustum = 0, sumHiZ = 0, sumSeen = 0, sumFar = 0, sumMicro = 0, sumReach = 0;
				long maxFrustum = 0, maxHiZ = 0, maxSeen = 0, maxFar = 0;
				string worst = "";
				int views = 0;
				var seenPiece = new bool[boxes.Length];
				foreach (var eye in eyes)
				{
					for (int bearing = 0; bearing < 8; bearing++)
					{
						Quaternion rot = Quaternion.Euler(-5f, bearing * 45f, 0f);
						Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right, up = rot * Vector3.up;
						Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye.At, rot, Vector3.one).inverse;
						Matrix4x4 vp = projection * view;
						GeometryUtility.CalculateFrustumPlanes(vp, planes);
						// The depth grid: view depth of the nearest rock through each cell's middle, and which piece it is.
						Array.Clear(seenPiece, 0, seenPiece.Length);
						for (int gy = 0; gy < GridY; gy++)
						{
							for (int gx = 0; gx < GridX; gx++)
							{
								float sx = ((gx + 0.5f) / GridX * 2f - 1f) * tanHalf * aspect, sy = ((gy + 0.5f) / GridY * 2f - 1f) * tanHalf;
								Vector3 d = (fwd + right * sx + up * sy).normalized;
								float z = Far;
								if (Physics.Raycast(eye.At, d, out RaycastHit hit, Far, storedLayer, QueryTriggerInteraction.Ignore))
								{
									z = hit.distance * Vector3.Dot(d, fwd);
									if (pieceOf.TryGetValue(hit.collider, out var id) && id.Cave == c)
									{
										seenPiece[id.Piece] = true;
									}
								}
								depth[gy * GridX + gx] = z;
							}
						}
						long frustum = 0, hiz = 0, seen = 0, far = 0, micro = 0, reach = 0;
						for (int p = 0; p < boxes.Length; p++)
						{
							if (!GeometryUtility.TestPlanesAABB(planes, boxes[p]))
							{
								continue;
							}
							frustum += tris[p];
							if (boxes[p].SqrDistance(eye.At) <= 72f * 72f)
							{
								reach += tris[p];
							}
							if (!Occluded(boxes[p], eye.At, fwd, right, up, tanHalf, aspect, depth))
							{
								hiz += tris[p];
							}
							if (!seenPiece[p])
							{
								continue;
							}
							seen += tris[p];
							// The seen pieces' triangles: how far, how small on screen.
							Vector3[] v = verts[p];
							int[] ix = index[p];
							for (int t = 0; t < ix.Length; t += 3)
							{
								Vector3 a = v[ix[t]] + sys.Origin, b = v[ix[t + 1]] + sys.Origin, cc = v[ix[t + 2]] + sys.Origin;
								Vector3 mid = (a + b + cc) / 3f;
								float mz = Vector3.Dot(mid - eye.At, fwd);
								if (mz < Near)
								{
									continue;
								}
								if ((mid - eye.At).sqrMagnitude > FarLod * FarLod)
								{
									far++;
								}
								Vector2 Px(Vector3 w)
								{
									Vector3 r = w - eye.At;
									float z = Mathf.Max(Near, Vector3.Dot(r, fwd));
									return new Vector2(Vector3.Dot(r, right) / (z * tanHalf * aspect) * 0.5f * Width, Vector3.Dot(r, up) / (z * tanHalf) * 0.5f * Height);
								}
								Vector2 pa = Px(a), pb = Px(b), pc = Px(cc);
								float area = Mathf.Abs((pb.x - pa.x) * (pc.y - pa.y) - (pc.x - pa.x) * (pb.y - pa.y)) * 0.5f;
								if (area < 1f)
								{
									micro++;
								}
							}
						}
						views++;
						sumFrustum += frustum;
						sumHiZ += hiz;
						sumSeen += seen;
						sumFar += far;
						sumMicro += micro;
						sumReach += reach;
						if (frustum > maxFrustum)
						{
							maxFrustum = frustum;
							worst = eye.What;
						}
						maxHiZ = Math.Max(maxHiZ, hiz);
						maxSeen = Math.Max(maxSeen, seen);
						maxFar = Math.Max(maxFar, far);
					}
				}
				if (views == 0)
				{
					continue;
				}
				Say($"draw site {cave.SiteId} {sys.Form} ({caveTris:N0} tris, {boxes.Length} pieces), {views} views: mean/max tris in frustum {sumFrustum / views:N0}/{maxFrustum:N0} (worst at {worst}), " +
					$"after Hi-Z {sumHiZ / views:N0}/{maxHiZ:N0}, in pieces a ray reaches {sumSeen / views:N0}/{maxSeen:N0}, within 72 m {sumReach / views:N0}; " +
					$"of the reached pieces' tris: past {FarLod:0} m {sumFar / views:N0}/{maxFar:N0}, under 1 px {sumMicro / views:N0}");
			}
			File.WriteAllLines(Path.Combine(output, "report.txt"), log);
		}

		/// <summary>
		/// Whether a box is hidden by the depth grid: its nearest depth beyond the farthest rock in every cell its screen rect
		/// covers (the depth-pyramid test, conservative: a box reaching the eye's plane is never hidden).
		/// </summary>
		private static bool Occluded(Bounds box, Vector3 eye, Vector3 fwd, Vector3 right, Vector3 up, float tanHalf, float aspect, float[] depth)
		{
			float minZ = float.MaxValue, x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = new Vector3((i & 1) == 0 ? box.min.x : box.max.x, (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z) - eye;
				float z = Vector3.Dot(corner, fwd);
				if (z < Near)
				{
					return false;
				}
				minZ = Mathf.Min(minZ, z);
				float sx = Vector3.Dot(corner, right) / (z * tanHalf * aspect), sy = Vector3.Dot(corner, up) / (z * tanHalf);
				x0 = Mathf.Min(x0, sx);
				x1 = Mathf.Max(x1, sx);
				y0 = Mathf.Min(y0, sy);
				y1 = Mathf.Max(y1, sy);
			}
			int gx0 = Mathf.Clamp(Mathf.FloorToInt((x0 * 0.5f + 0.5f) * GridX), 0, GridX - 1), gx1 = Mathf.Clamp(Mathf.FloorToInt((x1 * 0.5f + 0.5f) * GridX), 0, GridX - 1);
			int gy0 = Mathf.Clamp(Mathf.FloorToInt((y0 * 0.5f + 0.5f) * GridY), 0, GridY - 1), gy1 = Mathf.Clamp(Mathf.FloorToInt((y1 * 0.5f + 0.5f) * GridY), 0, GridY - 1);
			float maxDepth = 0f;
			for (int gy = gy0; gy <= gy1; gy++)
			{
				for (int gx = gx0; gx <= gx1; gx++)
				{
					maxDepth = Mathf.Max(maxDepth, depth[gy * GridX + gx]);
				}
			}
			return minZ > maxDepth + 0.5f;
		}

		/// <summary>
		/// A positions-only copy of a mesh: welded where its vertices meet exactly (<paramref name="cell"/> 0), else every vertex
		/// moved to the mean of its <paramref name="cell"/>-metre grid cell (in the system's frame, so neighbouring pieces agree)
		/// and the triangles that collapse dropped.
		/// </summary>
		private static Mesh Welded(Mesh source, float cell)
		{
			Vector3[] v = source.vertices;
			int[] t = source.triangles;
			var keyOf = new Dictionary<Vector3Int, int>();
			var sum = new List<Vector3>();
			var count = new List<int>();
			var map = new int[v.Length];
			for (int i = 0; i < v.Length; i++)
			{
				Vector3Int key = cell > 0f ? Vector3Int.FloorToInt(v[i] / cell) : Vector3Int.RoundToInt(v[i] * 10000f);
				if (!keyOf.TryGetValue(key, out int id))
				{
					keyOf[key] = id = sum.Count;
					sum.Add(Vector3.zero);
					count.Add(0);
				}
				sum[id] += v[i];
				count[id]++;
				map[i] = id;
			}
			var positions = new Vector3[sum.Count];
			for (int i = 0; i < positions.Length; i++)
			{
				positions[i] = sum[i] / count[i];
			}
			var kept = new List<int>(t.Length);
			for (int i = 0; i < t.Length; i += 3)
			{
				int a = map[t[i]], b = map[t[i + 1]], c = map[t[i + 2]];
				if (a != b && b != c && c != a)
				{
					kept.Add(a);
					kept.Add(b);
					kept.Add(c);
				}
			}
			var mesh = new Mesh { indexFormat = positions.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
			mesh.vertices = positions;
			mesh.triangles = kept.ToArray();
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
#endif
