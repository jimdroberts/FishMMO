#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Finds holes in a generated scene's caves (2026-10-20: "Ensure they are fully enclosed as we noticed some holes in some
	/// smaller areas of the cave on moron 2"): the saved rock pieces of every cave, welded, and two kinds of opening.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A torn inner face.</b> The rock is a shell round the void: its inner face (the cave's walls, where the void's field is
	/// about 0) is closed everywhere; only its outer face, buried under the hill, is dropped away from the mouths (CaveMesher).
	/// So an edge on one face only, lying on the inner face, is a hole a player sees through.
	/// </para>
	/// <para>
	/// <b>No roof.</b> The shell's top is cut flat a skin under the ground (CaveSystem.Field); air standing above that cut,
	/// away from every mouth, has no rock over it but the terrain, which is drawn from above only.
	/// </para>
	/// <para>
	/// <c>-executeMethod FishMMO.Shared.WorldDesign.CaveHoleProbe.Run</c>. Environment: <c>FISHMMO_CAVE_HOLE_SCENE</c> (the
	/// scene's asset path, default Moron 2), <c>FISHMMO_CAVE_HOLE_OUT</c> (the report's folder). Opens the scene, changes and
	/// saves nothing.
	/// </para>
	/// </remarks>
	public static class CaveHoleProbe
	{
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

		private static void Probe()
		{
			string path = Environment.GetEnvironmentVariable("FISHMMO_CAVE_HOLE_SCENE");
			if (string.IsNullOrEmpty(path))
			{
				path = "Assets/Scenes/WorldScene/Moron/Moron 2.unity";
			}
			string output = ProbeOutput.FromEnvironment("FISHMMO_CAVE_HOLE_OUT", "WorldRenders", "caveholes");
			Directory.CreateDirectory(output);
			var log = new List<string>();
			Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
			Terrain[] terrains = Terrain.activeTerrains.Where(t => t.gameObject.scene == scene).ToArray();
			float GroundAt(float x, float z)
			{
				foreach (Terrain t in terrains)
				{
					Vector3 o = t.transform.position, s = t.terrainData.size;
					if (x >= o.x && z >= o.z && x <= o.x + s.x && z <= o.z + s.z)
					{
						return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
					}
				}
				return float.NaN;
			}
			ScenePointsOfInterest points = PointOfInterestProbe.Asset(scene);
			if (points == null)
			{
				log.Add($"{path}: no points of interest");
				File.WriteAllLines(Path.Combine(output, "report.txt"), log);
				return;
			}
			// The caves' rock objects, by their origin (CaveShaper.Build stands "Cave Rock" at the system's origin).
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
			log.Add($"{path}: {points.Shapes.Count} shapes, {rocks.Count} cave rock objects, {terrains.Length} terrains");
			int totalTorn = 0, totalRoofless = 0, totalFlipped = 0;
			foreach (PointOfInterestShape shape in points.Shapes)
			{
				if (shape.Values == null || shape.Values.Length == 0 || shape.Shaper != CaveShaper.ShaperName || CaveShaper.IsLink(shape.Values, out _))
				{
					continue;
				}
				CaveSystem system;
				try
				{
					system = CaveSystem.FromValues(shape.Values, shape.Position, shape.Seed);
				}
				catch (Exception e)
				{
					log.Add($"site {shape.SiteId}: could not rebuild ({e.Message})");
					continue;
				}
				if (system == null)
				{
					continue;
				}
				Transform rock = rocks.OrderBy(r => (r.position - system.Origin).sqrMagnitude).FirstOrDefault();
				if (rock == null || (rock.position - system.Origin).sqrMagnitude > 1f)
				{
					log.Add($"site {shape.SiteId} {system.Form}: no rock object at its origin {system.Origin}");
					continue;
				}
				Transform visual = rock.Find(CliffPlacer.VisualName);
				MeshFilter[] filters = (visual != null ? visual : rock).GetComponentsInChildren<MeshFilter>(true);

				// ── Torn inner face: welded edges on one face whose middle lies on the void's face ──
				var weld = new Dictionary<Vector3Int, int>();
				var edges = new Dictionary<(int, int), (int Count, Vector3 Mid)>();
				int Id(Vector3 p)
				{
					var k = Vector3Int.RoundToInt(p * 1000f);
					if (!weld.TryGetValue(k, out int id))
					{
						weld[k] = id = weld.Count;
					}
					return id;
				}
				long triangles = 0;
				foreach (MeshFilter f in filters)
				{
					Mesh m = f.sharedMesh;
					if (m == null)
					{
						continue;
					}
					Vector3[] v = m.vertices;
					int[] tri = m.triangles;
					Matrix4x4 toSystem = Matrix4x4.Translate(-system.Origin) * f.transform.localToWorldMatrix;
					triangles += tri.Length / 3;
					for (int t = 0; t < tri.Length; t += 3)
					{
						Vector3 a = toSystem.MultiplyPoint3x4(v[tri[t]]), b = toSystem.MultiplyPoint3x4(v[tri[t + 1]]), c = toSystem.MultiplyPoint3x4(v[tri[t + 2]]);
						int ia = Id(a), ib = Id(b), ic = Id(c);
						void Edge(int i, int j, Vector3 p, Vector3 q)
						{
							var key = i < j ? (i, j) : (j, i);
							edges.TryGetValue(key, out var e);
							edges[key] = (e.Count + 1, (p + q) * 0.5f);
						}
						Edge(ia, ib, a, b);
						Edge(ib, ic, b, c);
						Edge(ic, ia, c, a);
					}
				}
				var torn = new List<Vector3>();
				int open = 0;
				foreach (var e in edges.Values)
				{
					if ((e.Count & 1) == 0)
					{
						continue;
					}
					open++;
					if (Mathf.Abs(system.Void(e.Mid)) < 0.6f)
					{
						torn.Add(e.Mid);
					}
				}

				// ── No roof: air within the clipped skin under the ground, away from every mouth ──
				var roofless = new List<Vector3>();
				Bounds extent = system.Extent;
				for (float x = extent.min.x; x <= extent.max.x; x += 1f)
				{
					for (float z = extent.min.z; z <= extent.max.z; z += 1f)
					{
						float g = GroundAt(x + system.Origin.x, z + system.Origin.z);
						if (float.IsNaN(g))
						{
							continue;
						}
						float top = g - system.Origin.y - 0.5f * system.Skin;
						var p = new Vector3(x, top, z);
						if (NearMouth(system, p, 6f))
						{
							continue;
						}
						if (system.Void(p) < -0.15f)
						{
							roofless.Add(p);
						}
					}
				}
				totalTorn += torn.Count;
				totalRoofless += roofless.Count;
				log.Add($"site {shape.SiteId} {system.Form} size {system.SizeClass} at {system.Origin:F0}: {filters.Length} pieces, {triangles:N0} triangles, " +
					$"{open} open edges, {torn.Count} on the inner face (holes), {roofless.Count} m² of air with no rock roof");

				// ── Seen through: rays from the middles of the void, which must meet the front of something ──
				(int rays, int escaped, List<(Vector3 At, string What)> seen) = Rays(system, rock);
				log.Add($"   rays: {rays} from {system.Passages.Sum(x => x.Nodes.Count)} passage nodes and {system.Chambers.Count} chambers; " +
					$"{escaped} escaped (not by a mouth), {seen.Count - escaped} met the back of a face");
				foreach (var cluster in Clusters(seen.Select(x => x.At).ToList(), 3f).Take(15))
				{
					string what = seen.OrderBy(x => (x.At - cluster.Centre).sqrMagnitude).First().What;
					log.Add($"   seen through: {cluster.Count} rays round {cluster.Centre:F1} ({what}), {Where(system, cluster.Centre - system.Origin)}");
				}
				totalFlipped += seen.Count;
				foreach (var cluster in Clusters(torn, 3f).Take(12))
				{
					Vector3 w = cluster.Centre + system.Origin;
					log.Add($"   hole: {cluster.Count} edges round {w:F1} (system {cluster.Centre:F1}), void {system.Void(cluster.Centre):F2}, " +
						$"ground over {GroundAt(w.x, w.z) - w.y:F1} m, {Where(system, cluster.Centre)}");
				}
				foreach (var cluster in Clusters(roofless, 3f).Take(12))
				{
					Vector3 w = cluster.Centre + system.Origin;
					log.Add($"   roofless: {cluster.Count} m² round {w:F1}, {Where(system, cluster.Centre)}");
				}
			}
			log.Add($"TOTAL: {totalTorn} torn inner-face edges, {totalRoofless} m² roofless, {totalFlipped} rays seen through the rock");
			File.WriteAllLines(Path.Combine(output, "report.txt"), log);
		}

		/// <summary>
		/// Rays from the void's middles (every passage node a man's height over its floor, every chamber's middle) in 64 even
		/// directions, against everything solid in the scene: one meeting the back of a face (the cave's rock seen from inside the
		/// rock, the terrain from under it) or meeting nothing (not out by a mouth) has seen through the cave's walls.
		/// </summary>
		private static (int rays, int escaped, List<(Vector3 At, string What)> seen) Rays(CaveSystem system, Transform rock)
		{
			Physics.SyncTransforms();
			bool backfaces = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			var origins = new List<Vector3>();
			foreach (CavePassage passage in system.Passages)
			{
				foreach (CaveNode n in passage.Nodes)
				{
					origins.Add(new Vector3(n.Centre.x, n.Floor + Mathf.Min(1.6f, 0.5f * n.Radius * n.Height), n.Centre.z));
				}
			}
			foreach (CaveChamber c in system.Chambers)
			{
				origins.Add(new Vector3(c.Centre.x, c.Floor + Mathf.Min(2.5f, 0.5f * c.Radii.y), c.Centre.z));
			}
			var dirs = new List<Vector3>();
			const int count = 64;
			for (int i = 0; i < count; i++)
			{
				float y = 1f - 2f * (i + 0.5f) / count, r = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
				dirs.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
			}
			var seen = new List<(Vector3, string)>();
			int rays = 0, escaped = 0;
			foreach (Vector3 local in origins)
			{
				if (system.Void(local) > -0.4f || NearMouth(system, local, 0f))
				{
					continue;
				}
				Vector3 from = local + system.Origin;
				foreach (Vector3 d in dirs)
				{
					rays++;
					if (Physics.Raycast(from, d, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore))
					{
						if (Vector3.Dot(hit.normal, d) > 0.05f)
						{
							bool own = hit.collider.transform.IsChildOf(rock);
							seen.Add((hit.point, own ? "the back of the cave's rock" : hit.collider is TerrainCollider ? "the terrain from under it" : "the back of " + hit.collider.name));
						}
						continue;
					}
					// Out by a mouth is the way out, not a hole.
					bool outByMouth = false;
					foreach (CaveMouth m in system.Mouths)
					{
						Vector3 toMouth = m.Floor - local;
						float along = Vector3.Dot(toMouth, d);
						if (along > 0f && (toMouth - d * along).magnitude < m.Zone + 2f * m.Radius)
						{
							outByMouth = true;
						}
					}
					if (!outByMouth)
					{
						escaped++;
						seen.Add((from + d * 20f, "nothing: out into the open"));
					}
				}
			}
			Physics.queriesHitBackfaces = backfaces;
			return (rays, escaped, seen);
		}

		private static bool NearMouth(CaveSystem system, Vector3 p, float grow)
		{
			foreach (CaveMouth m in system.Mouths)
			{
				float reach = m.Zone + 2f * m.Radius + grow;
				if ((p - m.Floor).sqrMagnitude < reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>What of the system a point is nearest: a chamber, a passage, or the variety's features.</summary>
		private static string Where(CaveSystem system, Vector3 p)
		{
			float best = float.MaxValue;
			string what = "?";
			for (int i = 0; i < system.Chambers.Count; i++)
			{
				CaveChamber c = system.Chambers[i];
				float d = new Vector2(p.x - c.Centre.x, p.z - c.Centre.z).magnitude - Mathf.Max(c.Radii.x, c.Radii.z);
				if (d < best)
				{
					best = d;
					what = $"chamber {i} ({c.Kind}, {Mathf.Max(c.Radii.x, c.Radii.z) * 2f:0} m across, {d:0.0} m from its edge)";
				}
			}
			for (int i = 0; i < system.Passages.Count; i++)
			{
				foreach (CaveNode n in system.Passages[i].Nodes)
				{
					float d = (p - new Vector3(n.Centre.x, n.Floor, n.Centre.z)).magnitude - n.Radius;
					if (d < best)
					{
						best = d;
						what = $"passage {i} ({system.Passages[i].Kind}, radius {n.Radius:0.0} m, {d:0.0} m from its line)";
					}
				}
			}
			return what;
		}

		private static List<(Vector3 Centre, int Count)> Clusters(List<Vector3> points, float reach)
		{
			var result = new List<(Vector3, int)>();
			var used = new bool[points.Count];
			for (int i = 0; i < points.Count; i++)
			{
				if (used[i])
				{
					continue;
				}
				Vector3 sum = Vector3.zero;
				int n = 0;
				var stack = new Stack<int>();
				stack.Push(i);
				used[i] = true;
				while (stack.Count > 0)
				{
					int k = stack.Pop();
					sum += points[k];
					n++;
					for (int j = 0; j < points.Count; j++)
					{
						if (!used[j] && (points[j] - points[k]).sqrMagnitude < reach * reach)
						{
							used[j] = true;
							stack.Push(j);
						}
					}
				}
				result.Add((sum / n, n));
			}
			return result.OrderByDescending(c => c.Item2).ToList();
		}
	}
}
#endif
