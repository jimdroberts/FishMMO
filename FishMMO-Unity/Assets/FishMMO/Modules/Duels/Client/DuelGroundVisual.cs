using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The duel ground made visible to its duellists: a low golden ward rising from the ground's floor and fading out a
	/// few metres up, with its corners and roof edge drawn as thin lines, so both can see where stepping out forfeits.
	/// </summary>
	/// <remarks>
	/// Built at run time, like the staff test regions: the built-in sprite shader is unlit, alpha-blended, writes no depth
	/// and culls nothing, so the walls show from inside the ground as well as from outside, and the fade is the mesh's own
	/// vertex colours. No collider: nothing can walk into it or be blocked by it.
	/// </remarks>
	public static class DuelGroundVisual
	{
		/// <summary>The ward's colour: warm gold, so it reads as a duelling circle rather than an enemy's spell.</summary>
		public static readonly Color WardColor = new Color(1f, 0.78f, 0.36f, 1f);

		/// <summary>The walls' opacity at the floor.</summary>
		private const float WallAlpha = 0.32f;

		/// <summary>Metres above the floor where the walls have faded out.</summary>
		private const float FadeHeight = 4.5f;

		/// <summary>The corner and rim lines' width.</summary>
		private const float LineWidth = 0.06f;

		/// <summary>Builds the ground's visual; destroy the returned object to remove it. Null when the shader is missing.</summary>
		public static GameObject Create(in DuelArena arena)
		{
			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				return null;
			}

			GameObject root = new GameObject("Duel Ground");
			root.transform.SetPositionAndRotation(arena.Center, DuelRules.Rotation(arena));

			Vector3 h = arena.Size * 0.5f;
			float fade = Mathf.Min(FadeHeight, arena.Size.y);

			Mesh mesh = BuildWalls(h, fade);
			Material wallMaterial = new Material(shader) { name = "Duel Ground Ward" };
			GameObject walls = new GameObject("Walls");
			walls.transform.SetParent(root.transform, false);
			walls.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = walls.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = wallMaterial;
			Quiet(renderer);

			Material lineMaterial = new Material(shader) { name = "Duel Ground Edge" };
			Color post = WardColor;
			post.a = 0.85f;
			Vector3[] corners =
			{
				new Vector3(-h.x, 0f, -h.z), new Vector3(h.x, 0f, -h.z), new Vector3(h.x, 0f, h.z), new Vector3(-h.x, 0f, h.z),
			};
			for (int i = 0; i < corners.Length; ++i)
			{
				AddLine(root.transform, lineMaterial, post,
					new[] { corners[i] + Vector3.down * h.y, corners[i] + Vector3.up * h.y }, false);
			}
			Vector3[] rim = new Vector3[4];
			for (int i = 0; i < corners.Length; ++i)
			{
				rim[i] = corners[i] + Vector3.up * h.y;
			}
			Color rimColor = WardColor;
			rimColor.a = 0.45f;
			AddLine(root.transform, lineMaterial, rimColor, rim, true);

			root.AddComponent<DuelGroundVisualOwner>().Own(mesh, wallMaterial, lineMaterial);
			return root;
		}

		/// <summary>Four walls, each two bands: opaque-ish at the floor fading to nothing at <paramref name="fade"/>, then clear to the roof.</summary>
		private static Mesh BuildWalls(Vector3 h, float fade)
		{
			Vector3[] c =
			{
				new Vector3(-h.x, 0f, -h.z), new Vector3(h.x, 0f, -h.z), new Vector3(h.x, 0f, h.z), new Vector3(-h.x, 0f, h.z),
			};
			float floor = -h.y;
			float faded = floor + fade;
			var vertices = new Vector3[16];
			var colors = new Color[16];
			var triangles = new int[24];
			Color bottom = WardColor;
			bottom.a = WallAlpha;
			Color top = WardColor;
			top.a = 0f;
			for (int wall = 0; wall < 4; ++wall)
			{
				Vector3 a = c[wall];
				Vector3 b = c[(wall + 1) % 4];
				int v = wall * 4;
				vertices[v + 0] = new Vector3(a.x, floor, a.z);
				vertices[v + 1] = new Vector3(b.x, floor, b.z);
				vertices[v + 2] = new Vector3(b.x, faded, b.z);
				vertices[v + 3] = new Vector3(a.x, faded, a.z);
				colors[v + 0] = bottom;
				colors[v + 1] = bottom;
				colors[v + 2] = top;
				colors[v + 3] = top;
				int t = wall * 6;
				triangles[t + 0] = v + 0;
				triangles[t + 1] = v + 2;
				triangles[t + 2] = v + 1;
				triangles[t + 3] = v + 0;
				triangles[t + 4] = v + 3;
				triangles[t + 5] = v + 2;
			}
			Mesh mesh = new Mesh { name = "Duel Ground Walls" };
			mesh.SetVertices(vertices);
			mesh.SetColors(colors);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		private static void AddLine(Transform parent, Material material, Color color, Vector3[] points, bool loop)
		{
			GameObject child = new GameObject("Edge");
			child.transform.SetParent(parent, false);
			LineRenderer line = child.AddComponent<LineRenderer>();
			line.useWorldSpace = false;
			line.loop = loop;
			line.sharedMaterial = material;
			line.startColor = line.endColor = color;
			line.widthMultiplier = LineWidth;
			line.positionCount = points.Length;
			line.SetPositions(points);
			Quiet(line);
		}

		private static void Quiet(Renderer renderer)
		{
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.lightProbeUsage = LightProbeUsage.Off;
			renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
		}
	}
}
