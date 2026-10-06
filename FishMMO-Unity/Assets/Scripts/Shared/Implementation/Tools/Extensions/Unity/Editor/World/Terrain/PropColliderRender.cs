#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Batch probe: renders generated biome-art prefabs with their collision drawn over them, to check by eye what
	/// players and NPCs actually collide with.
	/// </summary>
	/// <remarks>
	/// Three overlays: green, the prefab's own colliders; magenta, the proxy <see cref="ScenePropBaker.PrototypeCollision"/>
	/// bakes from them (what <see cref="PropColliderStreamer"/> places and the NavMesh carves); yellow, the plain
	/// axis-aligned box round the visual. Two views per prefab, side by side, and a line per prefab in
	/// <c>report.txt</c> with sizes and triangle counts. Run with
	/// <c>-executeMethod FishMMO.Shared.WorldDesign.PropColliderRender.Run</c>; FISHMMO_COLLIDER_OUT is the output
	/// folder and FISHMMO_COLLIDER_PREFABS a semicolon list of prefab names under the generated art folder (default:
	/// one of each family). Builds its own scratch scene and saves nothing but the proxies the baker would make anyway.
	/// </remarks>
	public static class PropColliderRender
	{
		private const int ViewWidth = 900, ViewHeight = 800;

		private static readonly string[] DefaultPrefabs = { "Boulder_Basalt_Round", "Boulder_Sandstone_Spire", "Detail_Rocks_Grey", "Detail_Fern", "Detail_CactusBarrel" };

		/// <summary>Brings the generated art up to date first (as a build does), then renders: for checking a change to how the generator makes colliders.</summary>
		public static void RegenerateAndRun()
		{
			var log = new List<string>();
			var clock = System.Diagnostics.Stopwatch.StartNew();
			BiomeArtGenerator.EnsureCurrentForBuild(log);
			Debug.Log($"[Collider render] art brought up to date in {clock.Elapsed.TotalSeconds:0} s:\n" + string.Join("\n", log));
			Run();
		}

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_COLLIDER_OUT") ?? Path.Combine(Path.GetTempPath(), "fishmmo-colliders");
			Directory.CreateDirectory(output);
			string list = Environment.GetEnvironmentVariable("FISHMMO_COLLIDER_PREFABS");
			string[] names = string.IsNullOrEmpty(list) ? DefaultPrefabs : list.Split(';').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
			var report = new List<string>();
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.3f;
			sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
			RenderSettings.ambientMode = AmbientMode.Flat;
			RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
			camera.fieldOfView = 35f;
			var target = new RenderTexture(ViewWidth, ViewHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;
			var lines = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
			lines.SetInt("_ZTest", (int)CompareFunction.Always);
			lines.SetInt("_Cull", (int)CullMode.Off);
			lines.SetInt("_ZWrite", 0);

			foreach (string name in names)
			{
				string[] found = AssetDatabase.FindAssets($"{name} t:Prefab", new[] { ProceduralArtCatalogue.Root });
				string path = found.Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
				GameObject prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
				if (prefab == null)
				{
					report.Add($"{name}: not found under {ProceduralArtCatalogue.Root}");
					continue;
				}
				GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
				instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
				Mesh proxy = ScenePropBaker.PrototypeCollision(prefab, out int layer);

				// LOD0 drawn, whatever the distance, so the picture and the yellow box are the same mesh.
				LODGroup group = instance.GetComponentInChildren<LODGroup>();
				group?.ForceLOD(0);
				Bounds visual = RenderBounds(instance);
				if (group != null)
				{
					LOD[] levels = group.GetLODs();
					for (int l = 0; l < levels.Length; l++)
					{
						Renderer[] lr = levels[l].renderers.Where(r => r != null).ToArray();
						if (lr.Length > 0)
						{
							Bounds b = lr[0].bounds;
							foreach (Renderer r in lr)
							{
								b.Encapsulate(r.bounds);
							}
							report.Add($"  LOD{l}: y {b.min.y:0.##}..{b.max.y:0.##}, x {b.min.x:0.##}..{b.max.x:0.##}, z {b.min.z:0.##}..{b.max.z:0.##}");
						}
					}
				}
				foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
				{
					report.Add($"  {c.GetType().Name} on '{c.name}': y {c.bounds.min.y:0.##}..{c.bounds.max.y:0.##}, x {c.bounds.min.x:0.##}..{c.bounds.max.x:0.##}, z {c.bounds.min.z:0.##}..{c.bounds.max.z:0.##}");
				}
				var colliders = instance.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToList();
				string kinds = colliders.Count == 0 ? "none" : string.Join(", ", colliders.GroupBy(c => c.GetType().Name + (c is MeshCollider m ? (m.convex ? " convex" : " concave") + $" {Triangles(m.sharedMesh)}t" : "")).Select(g => $"{g.Count()}x {g.Key}"));
				int proxyTris = proxy != null ? Triangles(proxy) : 0;
				float size = proxy != null ? Mathf.Max(proxy.bounds.size.x, proxy.bounds.size.y, proxy.bounds.size.z) : 0f;
				report.Add($"{name}: visual {Fmt(visual.size)}; colliders {kinds}; proxy {(proxy != null ? $"{proxyTris} tris, {Fmt(proxy.bounds.size)}" : "none")} (layer {layer}); blocks at scale 1: {(proxy != null && size >= ScenePropBaker.MinColliderMetres ? "yes" : "no")}");

				var image = new Texture2D(ViewWidth * 2, ViewHeight, TextureFormat.RGB24, false);
				float radius = Mathf.Max(visual.extents.magnitude, 0.25f);
				float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
				camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
				camera.farClipPlane = distance + radius * 2f;
				for (int view = 0; view < 2; view++)
				{
					Vector3 direction = view == 0 ? new Vector3(-1f, 0.7f, -1f).normalized : new Vector3(0f, 0.15f, -1f).normalized;
					camera.transform.position = visual.center + direction * distance;
					camera.transform.LookAt(visual.center);
					for (int i = 0; i < 2; i++)
					{
						camera.Render();
					}
					RenderTexture previous = RenderTexture.active;
					RenderTexture.active = target;
					GL.PushMatrix();
					GL.LoadProjectionMatrix(camera.projectionMatrix);   // GL converts it for the device itself: passing it through GetGPUProjectionMatrix flipped the overlay upside down
					GL.modelview = camera.worldToCameraMatrix;
					lines.SetPass(0);
					GL.Begin(GL.LINES);
					DrawBox(visual, new Color(1f, 0.85f, 0.1f));
					foreach (Collider collider in colliders)
					{
						DrawCollider(collider, new Color(0.2f, 1f, 0.3f));
					}
					if (proxy != null)
					{
						DrawMesh(proxy, Matrix4x4.identity, new Color(1f, 0.25f, 1f));
					}
					GL.End();
					GL.PopMatrix();
					image.ReadPixels(new Rect(0, 0, ViewWidth, ViewHeight), view * ViewWidth, 0);
					RenderTexture.active = previous;
				}
				image.Apply();
				File.WriteAllBytes(Path.Combine(output, $"collider_{name}.png"), image.EncodeToPNG());
				Object.DestroyImmediate(image);
				Object.DestroyImmediate(instance);
			}
			File.WriteAllLines(Path.Combine(output, "report.txt"), report);
			Debug.Log("[Collider render]\n" + string.Join("\n", report));
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		}

		private static string Fmt(Vector3 v) => $"{v.x:0.##} x {v.y:0.##} x {v.z:0.##} m";

		private static int Triangles(Mesh mesh) => mesh == null ? 0 : (int)Enumerable.Range(0, mesh.subMeshCount).Sum(s => (long)mesh.GetIndexCount(s)) / 3;

		private static Bounds RenderBounds(GameObject root)
		{
			// LOD0 only: every LOD covers the same ground, and the lower ones would only add their own slack.
			Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
			LODGroup lods = root.GetComponentInChildren<LODGroup>();
			if (lods != null && lods.GetLODs().Length > 0)
			{
				renderers = lods.GetLODs()[0].renderers.Where(r => r != null).ToArray();
			}
			if (renderers.Length == 0)
			{
				return new Bounds(root.transform.position, Vector3.one);
			}
			Bounds bounds = renderers[0].bounds;
			foreach (Renderer renderer in renderers)
			{
				bounds.Encapsulate(renderer.bounds);
			}
			return bounds;
		}

		private static void Line(Vector3 a, Vector3 b, Color colour)
		{
			GL.Color(colour);
			GL.Vertex(a);
			GL.Vertex(b);
		}

		private static void DrawBox(Bounds bounds, Color colour) => DrawBox(Matrix4x4.TRS(bounds.center, Quaternion.identity, bounds.size), colour);

		/// <summary>A unit cube transformed by <paramref name="m"/>.</summary>
		private static void DrawBox(Matrix4x4 m, Color colour)
		{
			var c = new Vector3[8];
			for (int i = 0; i < 8; i++)
			{
				c[i] = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f));
			}
			int[] edges = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
			for (int i = 0; i < edges.Length; i += 2)
			{
				Line(c[edges[i]], c[edges[i + 1]], colour);
			}
		}

		private static void DrawCollider(Collider collider, Color colour)
		{
			Matrix4x4 local = collider.transform.localToWorldMatrix;
			switch (collider)
			{
				case BoxCollider box:
					DrawBox(local * Matrix4x4.TRS(box.center, Quaternion.identity, box.size), colour);
					break;
				case SphereCollider sphere:
					DrawRings(local, sphere.center, sphere.radius, 0f, 1, colour);
					break;
				case CapsuleCollider capsule:
					DrawRings(local, capsule.center, capsule.radius, Mathf.Max(0f, capsule.height * 0.5f - capsule.radius), capsule.direction, colour);
					break;
				case MeshCollider mesh when mesh.sharedMesh != null:
					DrawMesh(mesh.sharedMesh, local, colour);
					break;
			}
		}

		/// <summary>Three rings round a sphere or capsule, the capsule's end rings offset by <paramref name="half"/> along <paramref name="axis"/>.</summary>
		private static void DrawRings(Matrix4x4 m, Vector3 centre, float radius, float half, int axis, Color colour)
		{
			Vector3 up = axis == 0 ? Vector3.right : axis == 2 ? Vector3.forward : Vector3.up;
			Vector3 u = Vector3.Cross(up, up == Vector3.up ? Vector3.right : Vector3.up).normalized, v = Vector3.Cross(up, u);
			const int steps = 24;
			for (int i = 0; i < steps; i++)
			{
				float a = i * Mathf.PI * 2f / steps, b = (i + 1) * Mathf.PI * 2f / steps;
				foreach (float end in new[] { -half, half })
				{
					Vector3 o = centre + up * end;
					Line(m.MultiplyPoint3x4(o + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius), m.MultiplyPoint3x4(o + (u * Mathf.Cos(b) + v * Mathf.Sin(b)) * radius), colour);
				}
				// The profile through the axis: the side lines and the end caps' arcs.
				foreach (Vector3 side in new[] { u, v })
				{
					Vector3 pa = Arc(a, side, up, half, radius), pb = Arc(b, side, up, half, radius);
					Line(m.MultiplyPoint3x4(centre + pa), m.MultiplyPoint3x4(centre + pb), colour);
				}
			}
		}

		private static Vector3 Arc(float angle, Vector3 side, Vector3 up, float half, float radius)
		{
			float s = Mathf.Sin(angle), c = Mathf.Cos(angle);
			return side * c * radius + up * (s * radius + Mathf.Sign(s) * half);
		}

		private static void DrawMesh(Mesh mesh, Matrix4x4 m, Color colour)
		{
			Vector3[] vertices = mesh.vertices;
			int[] triangles = mesh.triangles;
			// A large render mesh as a collider would be a solid smear of lines; every edge of up to 4000 triangles is plenty to read its shape.
			int step = Mathf.Max(1, triangles.Length / 3 / 4000);
			for (int t = 0; t + 2 < triangles.Length; t += 3 * step)
			{
				Vector3 a = m.MultiplyPoint3x4(vertices[triangles[t]]), b = m.MultiplyPoint3x4(vertices[triangles[t + 1]]), c = m.MultiplyPoint3x4(vertices[triangles[t + 2]]);
				Line(a, b, colour);
				Line(b, c, colour);
				Line(c, a, colour);
			}
		}
	}
}
#endif
